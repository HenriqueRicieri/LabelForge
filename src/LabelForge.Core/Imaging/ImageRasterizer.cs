using SkiaSharp;

namespace LabelForge.Core.Imaging;

/// <summary>
/// Decodes an encoded image (PNG, JPEG, BMP...) and scales it to a target dot size
/// as 8-bit grayscale, compositing transparency over white the way blank label
/// stock reads. Skia is the only decoder in the app (same engine as rendering).
/// </summary>
public static class ImageRasterizer
{
    /// <summary>Pixel size of an encoded image, or null when it cannot be decoded.
    /// Reads the header only; cheap enough for import-time probing.</summary>
    public static (int Width, int Height)? Probe(byte[] imageData)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        using var codec = SKCodec.Create(new MemoryStream(imageData));
        return codec is null ? null : (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>Grayscale buffer (row-major, 0 = black) at exactly the target size,
    /// or null when the bytes cannot be decoded. Never throws on bad input: the
    /// generator skips an undecodable image the way the renderer degrades.</summary>
    public static byte[]? ToGrayscale(byte[] imageData, int targetWidth, int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            return null;
        }

        // SKBitmap.Decode(byte[]) throws on undecodable input; probing the codec
        // first keeps the never-throw contract.
        using var codec = SKCodec.Create(new MemoryStream(imageData));
        if (codec is null)
        {
            return null;
        }

        using SKBitmap? source = SKBitmap.Decode(codec);
        if (source is null)
        {
            return null;
        }

        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var target = new SKBitmap(info);
        using (var canvas = new SKCanvas(target))
        using (SKImage image = SKImage.FromBitmap(source))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(
                image,
                new SKRect(0, 0, targetWidth, targetHeight),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            canvas.Flush();
        }

        ReadOnlySpan<byte> pixels = target.GetPixelSpan();
        var gray = new byte[targetWidth * targetHeight];
        for (int i = 0; i < gray.Length; i++)
        {
            int p = i * 4;

            // Rec. 601 luma over the white-composited pixels (BGRA order).
            gray[i] = (byte)((pixels[p] * 114 + pixels[p + 1] * 587 + pixels[p + 2] * 299) / 1000);
        }

        return gray;
    }
}
