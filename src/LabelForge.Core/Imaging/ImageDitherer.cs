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

    /// <summary>The 50% gray level every mode splits at by default.</summary>
    public const int DefaultThreshold = 128;

    // Error-diffusion kernels as (dx, dy, weight) over a divisor. Floyd-Steinberg keeps its
    // own code below because golden tests pin its exact output.
    private static readonly (int Dx, int Dy, int Weight)[] AtkinsonKernel =
        [(1, 0, 1), (2, 0, 1), (-1, 1, 1), (0, 1, 1), (1, 1, 1), (0, 2, 1)];

    private static readonly (int Dx, int Dy, int Weight)[] StuckiKernel =
    [
        (1, 0, 8), (2, 0, 4),
        (-2, 1, 2), (-1, 1, 4), (0, 1, 8), (1, 1, 4), (2, 1, 2),
        (-2, 2, 1), (-1, 2, 2), (0, 2, 4), (1, 2, 2), (2, 2, 1),
    ];

    private static readonly (int Dx, int Dy, int Weight)[] SierraKernel =
    [
        (1, 0, 5), (2, 0, 3),
        (-2, 1, 2), (-1, 1, 4), (0, 1, 5), (1, 1, 4), (2, 1, 2),
        (-1, 2, 2), (0, 2, 3), (1, 2, 2),
    ];

    /// <summary>Returns one flag per pixel, row-major; true means print black.</summary>
    /// <param name="threshold">Gray level below which a pixel prints black, 1 to 254.
    /// Applied as a shift of the grays, so the patterned and diffused modes lighten or
    /// darken with it the way the plain threshold does.</param>
    /// <param name="invert">Swap black and white before anything else.</param>
    public static bool[] Dither(
        byte[] grayscale, int width, int height, DitherMode mode,
        int threshold = DefaultThreshold, bool invert = false)
    {
        ArgumentNullException.ThrowIfNull(grayscale);
        if (width <= 0 || height <= 0 || grayscale.Length != width * height)
        {
            throw new ArgumentException("Grayscale buffer does not match the given size.");
        }

        byte[] gray = Adjust(grayscale, Math.Clamp(threshold, 1, 254), invert);
        return mode switch
        {
            DitherMode.Ordered => Ordered(gray, width, height),
            DitherMode.FloydSteinberg => FloydSteinberg(gray, width, height),
            DitherMode.Atkinson => Diffuse(gray, width, height, AtkinsonKernel, 8),
            DitherMode.Stucki => Diffuse(gray, width, height, StuckiKernel, 42),
            DitherMode.Sierra => Diffuse(gray, width, height, SierraKernel, 32),
            _ => Threshold(gray),
        };
    }

    /// <summary>
    /// Inverts and shifts the grays so the rest of the pipeline can keep splitting at
    /// 128: with a threshold T, a gray g becomes g + 128 - T, and g &lt; T exactly when
    /// the shifted value is below 128. At the defaults the buffer is returned untouched,
    /// which is what keeps every existing label's graphic bytes the same.
    /// </summary>
    private static byte[] Adjust(byte[] gray, int threshold, bool invert)
    {
        if (threshold == DefaultThreshold && !invert)
        {
            return gray;
        }

        int shift = DefaultThreshold - threshold;
        var adjusted = new byte[gray.Length];
        for (int i = 0; i < gray.Length; i++)
        {
            int value = invert ? 255 - gray[i] : gray[i];
            adjusted[i] = (byte)Math.Clamp(value + shift, 0, 255);
        }

        return adjusted;
    }

    /// <summary>Error diffusion with a forward kernel: each pixel's error is shared out
    /// to the neighbours the kernel names, in integer steps truncated toward zero as the
    /// Floyd-Steinberg path does. A kernel whose weights sum below the divisor (Atkinson)
    /// deliberately drops the rest.</summary>
    private static bool[] Diffuse(
        byte[] gray, int width, int height, (int Dx, int Dy, int Weight)[] kernel, int divisor)
    {
        var black = new bool[gray.Length];
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
                foreach ((int dx, int dy, int weight) in kernel)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx >= 0 && nx < width && ny < height)
                    {
                        work[ny * width + nx] += error * weight / divisor;
                    }
                }
            }
        }

        return black;
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
