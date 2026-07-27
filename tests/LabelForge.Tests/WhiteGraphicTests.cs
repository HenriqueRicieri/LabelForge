using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// ^GB's colour parameter. A white box paints the stock clear, which is how real labels
/// wipe an area before dropping a graphic into it: 116 of the corpus's 704 ^GB commands
/// are white, across six files, and nearly every one of them sits directly in front of an
/// ^XG at the same origin.
///
/// Reading them as black was the whole of E1d: the erase became a filled black rectangle
/// exactly the size of the stamp it was supposed to reveal, which is the worst kind of
/// wrong because it looks deliberate.
///
/// Not the same thing as <see cref="ReverseFieldTests"/>. Reverse inverts the ink it lands
/// on, so over blank stock it prints black; white paints white, so over blank stock it
/// prints nothing. The renderer test below holds both halves of that apart.
/// </summary>
public sealed class WhiteGraphicTests
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
    public void AnOrdinaryBoxAndLine_StayBlack()
    {
        string zpl = new ZplGenerator().Generate(Label(
            new BoxElement { X = 10, Y = 10, WidthDots = 80, HeightDots = 40, ThicknessDots = 2 },
            new LineElement { X = 10, Y = 60, LengthDots = 80, ThicknessDots = 3 }));

        Assert.Contains("^FO10,10^GB80,40,2,B^FS", zpl, StringComparison.Ordinal);
        Assert.Contains("^FO10,60^GB80,3,3,B^FS", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void AWhiteBoxAndLine_SaySoInTheZpl()
    {
        string zpl = new ZplGenerator().Generate(Label(
            new BoxElement
            {
                X = 10, Y = 10, WidthDots = 80, HeightDots = 40, ThicknessDots = 2, IsWhite = true,
            },
            new LineElement
            {
                X = 10, Y = 60, IsVertical = true, LengthDots = 80, ThicknessDots = 3, IsWhite = true,
            }));

        Assert.Contains("^FO10,10^GB80,40,2,W^FS", zpl, StringComparison.Ordinal);
        Assert.Contains("^FO10,60^GB3,80,3,W^FS", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_KeepsTheColourPerElement()
    {
        LabelDocument document = Label(
            new BoxElement
            {
                X = 10, Y = 10, WidthDots = 80, HeightDots = 40, ThicknessDots = 2, IsWhite = true,
            },
            new BoxElement { X = 10, Y = 60, WidthDots = 80, HeightDots = 40, ThicknessDots = 2 },
            new LineElement
            {
                X = 10, Y = 120, LengthDots = 80, ThicknessDots = 3, IsWhite = true,
            });

        string first = new ZplGenerator().Generate(document);
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(first, dpmm: 8);

        Assert.Empty(imported.Warnings);
        Assert.Equal(first, new ZplGenerator().Generate(imported.Document));
        Assert.True(((BoxElement)imported.Document.Elements[0]).IsWhite);
        Assert.False(((BoxElement)imported.Document.Elements[1]).IsWhite);
        Assert.True(((LineElement)imported.Document.Elements[2]).IsWhite);

        // And through the project format, which undo snapshots share.
        LabelDocument reloaded = LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(document));
        Assert.True(((BoxElement)reloaded.Elements[0]).IsWhite);
        Assert.False(((BoxElement)reloaded.Elements[1]).IsWhite);
        Assert.True(((LineElement)reloaded.Elements[2]).IsWhite);
    }

    /// <summary>The shape the driver writes: no width at all, and a thickness that fills
    /// the whole erase. It comes back as a line because a filled ^GB is one, and the
    /// colour has to survive that reading.</summary>
    [Fact]
    public void TheCorpusIdiom_IsReadAsAWhiteBar()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^FO45,42^GB0,394,363,W^FS\n^XZ");

        var line = Assert.IsType<LineElement>(Assert.Single(result.Document.Elements));
        Assert.True(line.IsVertical);
        Assert.Equal(394, line.LengthDots);
        Assert.Equal(363, line.ThicknessDots);
        Assert.True(line.IsWhite);

        // Nothing was lost, so nothing is reported.
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// The WYSIWYG guard, and the measurement that says white is not reverse: over ink
    /// both remove it, but over blank stock white prints nothing while reverse prints
    /// black. That difference is the reason the model needed a colour rather than reusing
    /// <see cref="Element.IsReversed"/>.
    /// </summary>
    [Fact]
    public void Renderer_ErasesWithWhiteAndPrintsNothingOnBlankStock()
    {
        BoxElement Bar() => new()
        {
            X = 20, Y = 20, WidthDots = 320, HeightDots = 80, ThicknessDots = 80,
        };

        BoxElement Patch(bool white, bool reversed) => new()
        {
            X = 60, Y = 40, WidthDots = 40, HeightDots = 40, ThicknessDots = 40,
            IsWhite = white, IsReversed = reversed, ZOrder = 1,
        };

        var renderer = new BinaryKitsRenderer();

        // 320 x 80 of solid bar, less the 40 x 40 the patch takes back out of it.
        Assert.Equal(25600, Ink(renderer, Label(Bar())));
        Assert.Equal(24000, Ink(renderer, Label(Bar(), Patch(white: true, reversed: false))));
        Assert.Equal(24000, Ink(renderer, Label(Bar(), Patch(white: false, reversed: true))));

        // On blank stock the two part company.
        Assert.Equal(0, Ink(renderer, Label(Patch(white: true, reversed: false))));
        Assert.Equal(1600, Ink(renderer, Label(Patch(white: false, reversed: true))));
    }

    /// <summary>
    /// E1d end to end. Take the idiom a real label is written with, import it, generate
    /// our own ZPL from the document, and the picture has to be the one that went in.
    /// Before this the erase came back black and the stamp vanished under it, so the ink
    /// nearly doubled.
    /// </summary>
    [Fact]
    public void AnErasedStamp_RegeneratesAsTheStampAndNotAsASlab()
    {
        string source = File.ReadAllText(
            Path.Combine(TestCorpus.FixturesDirectory(), "white-erase-under-graphic.zpl"));

        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(source, dpmm: 8);
        string regenerated = new ZplGenerator().Generate(imported.Document);

        var renderer = new BinaryKitsRenderer();
        int before = Ink(renderer.Render(source, 50, 37.5, 8));
        int after = Ink(renderer.Render(regenerated, 50, 37.5, 8));

        Assert.True(before > 0, "the fixture must draw something to compare against");
        Assert.Equal(before, after);

        // And the slab that used to appear is 24 x 8 dots of solid black, which is what
        // the erase would paint if its colour were dropped.
        Assert.True(after < before + 192, $"a black slab came back ({after} vs {before})");
    }

    private static int Ink(BinaryKitsRenderer renderer, LabelDocument document) =>
        Ink(renderer.Render(new ZplGenerator().Generate(document), 60, 40, 8));

    private static int Ink(RenderResult result)
    {
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
