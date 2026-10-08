using LabelForge.Core.Imaging;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// A phone stores a portrait photo as landscape pixels plus an EXIF orientation that says
/// how to turn it for display. Ignoring it put the photo on the label sideways, with its
/// width and height swapped.
/// </summary>
public sealed class ImageOrientationTests
{
    /// <summary>A 40 x 20 JPEG, left half black and right half white, tagged with EXIF
    /// orientation <paramref name="orientation"/>.</summary>
    private static byte[] TaggedJpeg(ushort orientation)
    {
        using var bitmap = new SKBitmap(40, 20);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var black = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(0, 0, 20, 20, black);
        }

        byte[] jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100).ToArray();

        // APP1 Exif with a little-endian TIFF header and one IFD entry: 0x0112, SHORT.
        byte[] app1 =
        [
            0xFF, 0xE1, 0x00, 0x22,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
            (byte)(orientation & 0xFF), (byte)(orientation >> 8), 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];

        return [.. jpeg.AsSpan(0, 2), .. app1, .. jpeg.AsSpan(2)];
    }

    private static double Mean(byte[] gray, int width, int x0, int y0, int x1, int y1)
    {
        double sum = 0;
        int count = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                sum += gray[y * width + x];
                count++;
            }
        }

        return sum / count;
    }

    [Fact]
    public void Probe_ReportsTheSizeTheImageIsShownAt()
    {
        Assert.Equal((40, 20), ImageRasterizer.Probe(TaggedJpeg(1)));
        Assert.Equal((20, 40), ImageRasterizer.Probe(TaggedJpeg(6)));
        Assert.Equal((20, 40), ImageRasterizer.Probe(TaggedJpeg(8)));
    }

    /// <summary>Orientation 6 is a quarter turn clockwise to display, so the black left
    /// half ends up on top; 8 is the other way, and it ends up at the bottom.</summary>
    [Theory]
    [InlineData(6, true)]
    [InlineData(8, false)]
    public void ToGrayscale_TurnsThePhotoUpright(ushort orientation, bool blackOnTop)
    {
        byte[]? gray = ImageRasterizer.ToGrayscale(TaggedJpeg(orientation), 20, 40);

        Assert.NotNull(gray);
        double top = Mean(gray, 20, 2, 2, 18, 18);
        double bottom = Mean(gray, 20, 2, 22, 18, 38);
        Assert.Equal(blackOnTop, top < 64 && bottom > 192);
        Assert.Equal(!blackOnTop, bottom < 64 && top > 192);
    }

    [Fact]
    public void ToGrayscale_LeavesAnUntaggedImageAsItIs()
    {
        byte[]? gray = ImageRasterizer.ToGrayscale(TaggedJpeg(1), 40, 20);

        Assert.NotNull(gray);
        Assert.True(Mean(gray, 40, 2, 2, 18, 18) < 64);
        Assert.True(Mean(gray, 40, 22, 2, 38, 18) > 192);
    }
}
