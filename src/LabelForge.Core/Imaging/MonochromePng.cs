using SkiaSharp;

namespace LabelForge.Core.Imaging;

/// <summary>
/// Turns a 1-bit mask into PNG bytes. An imported graphic becomes an ordinary
/// <see cref="Model.ImageElement"/>, and that element's contract is that it holds an
/// encoded image the rest of the pipeline can decode, so a recovered ~DG bitmap has to
/// re-enter the app as a real image file rather than as a special case.
///
/// The output is pure black and white with no alpha, so re-rasterizing it at its
/// natural size and thresholding gives back the bits it came from.
/// </summary>
public static class MonochromePng
{
    /// <summary>Encodes a row-major black mask (true = black) as an opaque PNG.</summary>
    public static byte[] Encode(bool[] black, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(black);
        if (width <= 0 || height <= 0 || black.Length != width * height)
        {
            throw new ArgumentException("Mask does not match the given size.", nameof(black));
        }

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < black.Length; i++)
        {
            byte level = black[i] ? (byte)0 : (byte)255;
            int p = i * 4;
            pixels[p] = level;
            pixels[p + 1] = level;
            pixels[p + 2] = level;
            pixels[p + 3] = 255;
        }

        using SKImage image = SKImage.FromPixelCopy(info, pixels);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
