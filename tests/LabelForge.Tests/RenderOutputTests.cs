using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// The preview stopped going through a PNG. These pin what that is allowed to change,
/// which is nothing anyone can see: the pixels a caller gets from the raw path have to be
/// the pixels it would have decoded out of the encoded one, or the canvas has quietly
/// stopped being what the printer prints.
///
/// Two engine behaviours are pinned here rather than described in a comment, because both
/// decide what a gesture layer is able to do: DrawSurface paints no background of its own,
/// and it ignores DrawerOptions.OpaqueBackground, so the surface's clear is the only thing
/// that decides what the ink sits on.
/// </summary>
public sealed class RenderOutputTests
{
    private const int Dpmm = 8;
    private const double WidthMm = 60;
    private const double HeightMm = 40;

    /// <summary>A label with one of everything the engine draws differently: glyphs, bars,
    /// a stroked rectangle and a filled one, so a difference in antialiasing, in alpha or
    /// in where the origin sits has somewhere to show up.</summary>
    private static string GoldenLabel()
    {
        var document = new LabelDocument { WidthMm = WidthMm, HeightMm = HeightMm, Dpmm = Dpmm };
        document.Elements.Add(new TextElement
        {
            X = 24, Y = 16, Text = "GOLDEN 123", FontHeightDots = 40, ZOrder = 0,
        });
        document.Elements.Add(new BarcodeElement
        {
            X = 24, Y = 72, Data = "7891234567895", HeightDots = 80, ModuleWidthDots = 2, ZOrder = 1,
        });
        document.Elements.Add(new BoxElement
        {
            X = 8, Y = 8, WidthDots = 464, HeightDots = 304, ThicknessDots = 3, ZOrder = 2,
        });
        document.Elements.Add(new BoxElement
        {
            X = 300, Y = 200, WidthDots = 120, HeightDots = 60, ThicknessDots = 60, ZOrder = 3,
        });

        return new ZplGenerator().Generate(document);
    }

    private static RenderResult Render(RenderOutput output) =>
        new BinaryKitsRenderer().Render(GoldenLabel(), WidthMm, HeightMm, Dpmm, 0, output);

    [Fact]
    public void PixelsAreTheSamePictureAsTheDecodedPng()
    {
        RenderResult encoded = Render(RenderOutput.Png);
        RenderResult raw = Render(RenderOutput.Pixels);

        Assert.Empty(encoded.Errors);
        Assert.Empty(raw.Errors);
        Assert.NotNull(raw.Pixels);
        Assert.Equal(encoded.WidthDots, raw.PixelWidth);
        Assert.Equal(encoded.HeightDots, raw.PixelHeight);

        var info = new SKImageInfo(
            raw.PixelWidth, raw.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKBitmap? decoded = SKBitmap.Decode(encoded.Png, info);
        Assert.NotNull(decoded);

        // The engine's own encode and our surface must agree that the label is this many
        // dots across before comparing a single pixel of it.
        Assert.Equal(raw.PixelWidth, decoded.Width);
        Assert.Equal(raw.PixelHeight, decoded.Height);

        ReadOnlySpan<byte> expected = decoded.GetPixelSpan();
        ReadOnlySpan<byte> actual = raw.Pixels;

        int differing = 0;
        (int X, int Y)? first = null;
        for (int y = 0; y < raw.PixelHeight; y++)
        {
            for (int x = 0; x < raw.PixelWidth; x++)
            {
                int here = y * raw.Stride + x * 4;
                int there = y * decoded.RowBytes + x * 4;
                if (expected.Slice(there, 4).SequenceEqual(actual.Slice(here, 4)))
                {
                    continue;
                }

                differing++;
                first ??= (x, y);
            }
        }

        Assert.True(
            differing == 0,
            $"{differing} of {raw.PixelWidth * raw.PixelHeight} pixels differ between the "
            + $"encoded and the raw render, the first at {first}.");
    }

    [Fact]
    public void PixelsAreOpaqueBecauseAPrintedLabelIs()
    {
        RenderResult raw = Render(RenderOutput.Pixels);
        Assert.NotNull(raw.Pixels);

        int transparent = 0;
        for (int i = 3; i < raw.Pixels.Length; i += 4)
        {
            if (raw.Pixels[i] != 255)
            {
                transparent++;
            }
        }

        Assert.Equal(0, transparent);
    }

    /// <summary>DrawSurface draws onto whatever it is handed and paints no background, so a
    /// surface cleared to transparent comes back as ink on nothing. This is what makes a
    /// gesture layer composite over the rest of the label instead of covering it.</summary>
    [Fact]
    public void TransparentPixelsLeaveTheStockClear()
    {
        RenderResult raw = Render(RenderOutput.TransparentPixels);
        Assert.NotNull(raw.Pixels);
        Assert.Empty(raw.Errors);

        int clear = 0;
        int inked = 0;
        for (int i = 3; i < raw.Pixels.Length; i += 4)
        {
            if (raw.Pixels[i] == 0)
            {
                clear++;
            }
            else
            {
                inked++;
            }
        }

        // Most of a label is empty stock, and the elements above are drawn on well under
        // half of it, so both counts being substantial is the whole assertion: ink landed,
        // and it did not land on a background of its own.
        Assert.True(clear > inked, $"{clear} clear pixels against {inked} inked ones.");
        Assert.True(inked > 0, "the transparent render drew no ink at all.");
    }

    /// <summary>Where the ink is must not depend on what it sits on. Only the two ends of
    /// the alpha range can be compared directly: a pixel the ink covers completely is the
    /// same colour either way, and a pixel it does not reach is bare stock. The edges in
    /// between are exactly what compositing changes, and the byte-for-byte test above is
    /// what pins those.</summary>
    [Fact]
    public void TheInkLandsInTheSamePlaceWithOrWithoutTheStockBeneathIt()
    {
        RenderResult onWhite = Render(RenderOutput.Pixels);
        RenderResult onNothing = Render(RenderOutput.TransparentPixels);
        Assert.NotNull(onWhite.Pixels);
        Assert.NotNull(onNothing.Pixels);
        Assert.Equal(onWhite.Stride, onNothing.Stride);

        int coveredDifferently = 0;
        int untouchedButNotStock = 0;
        for (int i = 0; i < onNothing.Pixels.Length; i += 4)
        {
            byte alpha = onNothing.Pixels[i + 3];
            if (alpha == 255)
            {
                if (onNothing.Pixels[i] != onWhite.Pixels[i]
                    || onNothing.Pixels[i + 1] != onWhite.Pixels[i + 1]
                    || onNothing.Pixels[i + 2] != onWhite.Pixels[i + 2])
                {
                    coveredDifferently++;
                }
            }
            else if (alpha == 0)
            {
                if (onWhite.Pixels[i] != 255
                    || onWhite.Pixels[i + 1] != 255
                    || onWhite.Pixels[i + 2] != 255
                    || onWhite.Pixels[i + 3] != 255)
                {
                    untouchedButNotStock++;
                }
            }
        }

        Assert.Equal(0, coveredDifferently);
        Assert.Equal(0, untouchedButNotStock);
    }

    /// <summary>A caller tells the two outputs apart by whether Pixels is null, so the PNG
    /// path must leave it that way and the pixel path must leave the PNG empty.</summary>
    [Fact]
    public void EachOutputFillsOnlyItsOwnBuffer()
    {
        RenderResult encoded = Render(RenderOutput.Png);
        Assert.Null(encoded.Pixels);
        Assert.Equal(0, encoded.Stride);
        Assert.Equal(0, encoded.PixelWidth);
        Assert.Equal(0, encoded.PixelHeight);
        Assert.NotEmpty(encoded.Png);
        Assert.True(encoded.HasImage);

        RenderResult raw = Render(RenderOutput.Pixels);
        Assert.Empty(raw.Png);
        Assert.NotNull(raw.Pixels);
        Assert.True(raw.Stride >= raw.PixelWidth * 4);
        Assert.Equal(raw.Stride * raw.PixelHeight, raw.Pixels.Length);
        Assert.True(raw.HasImage);
    }

    /// <summary>Rendering must degrade rather than throw, whichever output was asked for.
    /// A template marker inside a barcode is the case that reaches this in real use: no
    /// linear symbology can encode it, and the engine throws rather than skipping it.</summary>
    [Fact]
    public void AFailedPixelRenderReportsTheReasonAndNoImage()
    {
        var document = new LabelDocument { WidthMm = WidthMm, HeightMm = HeightMm, Dpmm = Dpmm };
        document.Elements.Add(new BarcodeElement
        {
            X = 24,
            Y = 24,
            Symbology = BarcodeSymbology.Ean13,
            Data = "##CODIGO_BARRAS##",
            HeightDots = 80,
            ModuleWidthDots = 2,
        });

        string zpl = new ZplGenerator().Generate(document);
        RenderResult raw = new BinaryKitsRenderer()
            .Render(zpl, WidthMm, HeightMm, Dpmm, 0, RenderOutput.Pixels);

        Assert.NotEmpty(raw.Errors);
        Assert.NotNull(raw.Pixels);
        Assert.Empty(raw.Pixels);
        Assert.False(raw.HasImage);
    }
}
