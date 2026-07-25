using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// Reverse fields (^FR): the one colour the model has beyond black. A reversed field
/// knocks itself out of the ink underneath, which is how a label puts white text in a
/// black bar. All 89 uses in the corpus are on text, which is why that is the case the
/// renderer guard covers.
/// </summary>
public sealed class ReverseFieldTests
{
    private static LabelDocument Label(params Element[] elements)
    {
        var document = new LabelDocument { WidthMm = 60, HeightMm = 40, Dpmm = 8 };
        foreach (Element element in elements)
        {
            document.Elements.Add(element);
        }

        return document;
    }

    [Fact]
    public void AnOrdinaryField_EmitsNoReverseMarker()
    {
        string zpl = new ZplGenerator().Generate(Label(
            new TextElement { X = 10, Y = 10, Text = "plain", FontHeightDots = 30 }));

        Assert.DoesNotContain("^FR", zpl, StringComparison.Ordinal);
    }

    /// <summary>^FR sits directly after the origin, which is the one position that works
    /// for every field type rather than only for text.</summary>
    [Fact]
    public void AReversedField_MarksItselfRightAfterTheOrigin()
    {
        string zpl = new ZplGenerator().Generate(Label(
            new TextElement
            {
                X = 10, Y = 10, Text = "knockout", FontHeightDots = 30, IsReversed = true,
            },
            new BoxElement
            {
                X = 40, Y = 60, WidthDots = 80, HeightDots = 40, ThicknessDots = 2, IsReversed = true,
            }));

        Assert.Contains("^FO10,10^FR^A0N,30^FDknockout^FS", zpl, StringComparison.Ordinal);
        Assert.Contains("^FO40,60^FR^GB80,40,2,B^FS", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_KeepsTheFlagPerField()
    {
        LabelDocument document = Label(
            new TextElement { X = 10, Y = 10, Text = "reversed", FontHeightDots = 30, IsReversed = true },
            new TextElement { X = 10, Y = 60, Text = "normal", FontHeightDots = 30 },
            new BarcodeElement
            {
                X = 10, Y = 110, Data = "12345", HeightDots = 60, IsReversed = true,
            });

        string first = new ZplGenerator().Generate(document);
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(first, dpmm: 8);

        Assert.Empty(imported.Warnings);
        Assert.Equal(first, new ZplGenerator().Generate(imported.Document));
        Assert.True(imported.Document.Elements[0].IsReversed);
        Assert.False(imported.Document.Elements[1].IsReversed);
        Assert.True(imported.Document.Elements[2].IsReversed);
    }

    /// <summary>The corpus idiom puts ^FR after the font and after ^FH, not after ^FO.
    /// Reading has to accept it wherever in the field it lands.</summary>
    [Fact]
    public void TheCorpusIdiom_IsRead()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^FT372,981^A0R,44,46^FH^FR^FD##DESC_PROD##^FS\n^XZ");

        Element element = Assert.Single(result.Document.Elements);
        Assert.True(element.IsReversed);
        Assert.Equal(Orientation.Rotated90, element.Orientation);
        Assert.Equal("##DESC_PROD##", ((TextElement)element).Text);
    }

    [Fact]
    public void ReverseDoesNotLeakIntoTheNextField()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^FO0,0^A0N,30^FR^FDone^FS\n^FO0,50^A0N,30^FDtwo^FS\n^XZ");

        Assert.True(result.Document.Elements[0].IsReversed);
        Assert.False(result.Document.Elements[1].IsReversed);
    }

    /// <summary>
    /// The WYSIWYG guard. Black text on a black bar is invisible; the same text reversed
    /// has to remove ink. Without this the canvas would show a solid bar either way and
    /// the designer could not tell the two apart.
    /// </summary>
    [Fact]
    public void Renderer_KnocksReversedTextOutOfTheBarBelowIt()
    {
        BoxElement Bar() => new()
        {
            X = 20, Y = 20, WidthDots = 320, HeightDots = 80, ThicknessDots = 80,
        };

        TextElement Caption(bool reversed) => new()
        {
            X = 40, Y = 40, Text = "ABCD", FontHeightDots = 40, FontWidthDots = 40,
            IsReversed = reversed, ZOrder = 1,
        };

        var renderer = new BinaryKitsRenderer();
        int barAlone = Ink(renderer, Label(Bar()));
        int withPlainText = Ink(renderer, Label(Bar(), Caption(reversed: false)));
        int withReversedText = Ink(renderer, Label(Bar(), Caption(reversed: true)));

        // 320 x 80 of solid bar.
        Assert.Equal(25600, barAlone);

        // Black on black adds nothing that can be seen.
        Assert.Equal(barAlone, withPlainText);

        // Reversed, the glyphs come back out of the bar.
        Assert.True(
            withReversedText < barAlone,
            $"reversed text must remove ink ({withReversedText} vs {barAlone})");
    }

    /// <summary>
    /// A limitation, pinned so it stays known: BinaryKits honours ^FR for text and ^GB
    /// but not for a ^GF image, which a real printer does reverse. It costs nothing on
    /// real input, where every one of the corpus's 89 reversed fields is text, and the
    /// model stays uniform because ZPL applies ^FR to any field.
    /// </summary>
    [Fact]
    public void Renderer_IgnoresReverseOnAnImage()
    {
        BoxElement Bar() => new()
        {
            X = 20, Y = 20, WidthDots = 320, HeightDots = 80, ThicknessDots = 80,
        };

        ImageElement Stamp(bool reversed) => new()
        {
            X = 40, Y = 40, ImageData = TestImages.SolidPng(8, 8, 0, 0, 0),
            SourcePixelWidth = 8, SourcePixelHeight = 8,
            WidthDots = 40, HeightDots = 40, Dithering = DitherMode.Threshold,
            IsReversed = reversed, ZOrder = 1,
        };

        var renderer = new BinaryKitsRenderer();

        Assert.Equal(
            Ink(renderer, Label(Bar(), Stamp(reversed: false))),
            Ink(renderer, Label(Bar(), Stamp(reversed: true))));
    }

    private static int Ink(BinaryKitsRenderer renderer, LabelDocument document)
    {
        RenderResult result = renderer.Render(
            new ZplGenerator().Generate(document), 60, 40, 8);
        Assert.Empty(result.Errors);

        using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
        Assert.NotNull(bitmap);

        int ink = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    ink++;
                }
            }
        }

        return ink;
    }
}
