using LabelForge.Core.Model;

namespace LabelForge.Core.Imaging;

/// <summary>
/// Converts 8-bit grayscale (0 = black, 255 = white) to the printer's 1-bit black.
/// Pure and deterministic so golden tests can pin exact ^GF output.
/// </summary>
public static class ImageDitherer
{
    // Standard 8x8 Bayer matrix; thresholds spread evenly over 0..255.
    private static readonly byte[] Bayer8 =
    [
         0, 32,  8, 40,  2, 34, 10, 42,
        48, 16, 56, 24, 50, 18, 58, 26,
        12, 44,  4, 36, 14, 46,  6, 38,
        60, 28, 52, 20, 62, 30, 54, 22,
         3, 35, 11, 43,  1, 33,  9, 41,
        51, 19, 59, 27, 49, 17, 57, 25,
        15, 47,  7, 39, 13, 45,  5, 37,
        63, 31, 55, 23, 61, 29, 53, 21,
    ];

    /// <summary>Returns one flag per pixel, row-major; true means print black.</summary>
    public static bool[] Dither(byte[] grayscale, int width, int height, DitherMode mode)
    {
        ArgumentNullException.ThrowIfNull(grayscale);
        if (width <= 0 || height <= 0 || grayscale.Length != width * height)
        {
            throw new ArgumentException("Grayscale buffer does not match the given size.");
        }

        return mode switch
        {
            DitherMode.Ordered => Ordered(grayscale, width, height),
            DitherMode.FloydSteinberg => FloydSteinberg(grayscale, width, height),
            _ => Threshold(grayscale),
        };
    }

    private static bool[] Threshold(byte[] gray)
    {
        var black = new bool[gray.Length];
        for (int i = 0; i < gray.Length; i++)
        {
            black[i] = gray[i] < 128;
        }

        return black;
    }

    private static bool[] Ordered(byte[] gray, int width, int height)
    {
        var black = new bool[gray.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int threshold = (Bayer8[(y % 8) * 8 + x % 8] * 4) + 2;
                black[y * width + x] = gray[y * width + x] < threshold;
            }
        }

        return black;
    }

    private static bool[] FloydSteinberg(byte[] gray, int width, int height)
    {
        var black = new bool[gray.Length];

        // Work in a wider type: diffused error pushes values outside 0..255.
        var work = new int[gray.Length];
        for (int i = 0; i < gray.Length; i++)
        {
            work[i] = gray[i];
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                int value = work[i];
                bool isBlack = value < 128;
                black[i] = isBlack;

                int error = value - (isBlack ? 0 : 255);
                if (x + 1 < width)
                {
                    work[i + 1] += error * 7 / 16;
                }

                if (y + 1 < height)
                {
                    if (x > 0)
                    {
                        work[i + width - 1] += error * 3 / 16;
                    }

                    work[i + width] += error * 5 / 16;
                    if (x + 1 < width)
                    {
                        work[i + width + 1] += error * 1 / 16;
                    }
                }
            }
        }

        return black;
    }
}
