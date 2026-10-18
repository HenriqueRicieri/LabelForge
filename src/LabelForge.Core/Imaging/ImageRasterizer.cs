using SkiaSharp;

namespace LabelForge.Core.Imaging;

/// <summary>
/// Decodes an encoded image (PNG, JPEG, BMP...) and scales it to a target dot size
/// as 8-bit grayscale, compositing transparency over white the way blank label
/// stock reads. Skia is the only decoder in the app (same engine as rendering).
/// </summary>
public static class ImageRasterizer
{
    /// <summary>Pixel size of an encoded image as it is shown, or null when it cannot be
    /// decoded. Reads the header only; cheap enough for import-time probing. A photo whose
    /// EXIF orientation turns it a quarter reports its sides swapped, the way it displays.</summary>
    public static (int Width, int Height)? Probe(byte[] imageData)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        using var codec = SKCodec.Create(new MemoryStream(imageData));
        if (codec is null)
        {
            return null;
        }

        return TurnsAQuarter(codec.EncodedOrigin)
            ? (codec.Info.Height, codec.Info.Width)
            : (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>The EXIF orientations that turn the stored pixels a quarter (5 to 8).</summary>
    private static bool TurnsAQuarter(SKEncodedOrigin origin) => origin is
        SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or
        SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>
    /// The decoded pixels turned the way the file says to display them. A phone stores a
    /// portrait photo as landscape pixels plus an EXIF orientation, and the decoder hands
    /// back the stored pixels, so without this the photo lands on the label sideways.
    /// Returns the source itself when nothing needs turning; otherwise a new bitmap the
    /// caller owns.
    /// </summary>
    private static SKBitmap Upright(SKBitmap source, SKEncodedOrigin origin)
    {
        float w = source.Width;
        float h = source.Height;

        // Where each stored pixel goes: x' = ScaleX*x + SkewX*y + TransX and
        // y' = SkewY*x + ScaleY*y + TransY, for the eight EXIF orientations.
        SKMatrix? turn = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => null,
        };

        if (turn is not { } matrix)
        {
            return source;
        }

        bool quarter = TurnsAQuarter(origin);
        var upright = new SKBitmap(new SKImageInfo(
            quarter ? source.Height : source.Width,
            quarter ? source.Width : source.Height,
            source.ColorType,
            source.AlphaType));
        using var canvas = new SKCanvas(upright);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return upright;
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

        using SKBitmap? decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return null;
        }

        SKBitmap upright = Upright(decoded, codec.EncodedOrigin);
        using SKBitmap? turned = ReferenceEquals(upright, decoded) ? null : upright;
        SKBitmap source = upright;

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
