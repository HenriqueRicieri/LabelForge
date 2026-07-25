using LabelForge.Core.Imaging;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// The image pipeline is three pure stages (rasterize, dither, encode); these tests
/// pin the dithering decisions and the exact ^GFA byte layout, including row padding.
/// </summary>
public sealed class ImagingTests
{
    [Fact]
    public void Threshold_SplitsAt128()
    {
        bool[] black = ImageDitherer.Dither([0, 127, 128, 255], 4, 1, DitherMode.Threshold);

        Assert.Equal([true, true, false, false], black);
    }

    [Fact]
    public void FloydSteinberg_RendersMidGrayAsMix()
    {
        var gray = new byte[16 * 16];
        Array.Fill(gray, (byte)128);

        bool[] black = ImageDitherer.Dither(gray, 16, 16, DitherMode.FloydSteinberg);

        int blackCount = black.Count(b => b);

        // 128/255 is just on the white side of the midpoint: diffusion must produce
        // a mix close to half, not a solid field (which is what thresholding gives).
        Assert.InRange(blackCount, 64, 192);
    }

    [Fact]
    public void Ordered_IsDeterministic()
    {
        var gray = new byte[8 * 8];
        Array.Fill(gray, (byte)100);

        bool[] first = ImageDitherer.Dither(gray, 8, 8, DitherMode.Ordered);
        bool[] second = ImageDitherer.Dither(gray, 8, 8, DitherMode.Ordered);

        Assert.Equal(first, second);
        Assert.Contains(true, first);
        Assert.Contains(false, first);
    }

    [Fact]
    public void EncodeGfa_PacksBitsMsbFirst()
    {
        // 8x2: top row all black (FF), bottom row alternating starting black (AA).
        bool[] black =
        [
            true, true, true, true, true, true, true, true,
            true, false, true, false, true, false, true, false,
        ];

        Assert.Equal("^GFA,2,2,1,FFAA^FS", ZplImageEncoder.EncodeGfa(black, 8, 2));
    }

    [Fact]
    public void EncodeGfa_PadsRowsToWholeBytes()
    {
        // 12 wide needs 2 bytes per row; the last 4 bits of each row are padding,
        // and padding at the end of a row is exactly what the comma stands for.
        var black = new bool[12];
        Array.Fill(black, true);

        Assert.Equal("^GFA,2,2,2,FFF,^FS", ZplImageEncoder.EncodeGfa(black, 12, 1));
    }

    [Fact]
    public void ToGrayscale_ReturnsNullOnUndecodableBytes()
    {
        Assert.Null(ImageRasterizer.ToGrayscale([1, 2, 3, 4], 8, 8));
        Assert.Null(ImageRasterizer.Probe([1, 2, 3, 4]));
    }

    [Fact]
    public void ToGrayscale_CompositesTransparencyOverWhite()
    {
        byte[] png = TestImages.SolidPng(4, 4, red: 0, green: 0, blue: 0, alpha: 0);

        byte[]? gray = ImageRasterizer.ToGrayscale(png, 4, 4);

        Assert.NotNull(gray);
        Assert.All(gray, value => Assert.True(value > 250, "transparent must read as white"));
    }
}

/// <summary>Tiny encoded images built with Skia for tests; kept out of the fixture
/// folder because they are one-liners.</summary>
internal static class TestImages
{
    public static byte[] SolidPng(int width, int height, byte red, byte green, byte blue, byte alpha = 255)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(new SkiaSharp.SKColor(red, green, blue, alpha));
        }

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        return image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray();
    }

    /// <summary>An 8x1 PNG: the left half black, the right half white.</summary>
    public static byte[] HalfBlackPng()
    {
        using var bitmap = new SkiaSharp.SKBitmap(8, 1);
        for (int x = 0; x < 8; x++)
        {
            bitmap.SetPixel(x, 0, x < 4 ? SkiaSharp.SKColors.Black : SkiaSharp.SKColors.White);
        }

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        return image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray();
    }
}
