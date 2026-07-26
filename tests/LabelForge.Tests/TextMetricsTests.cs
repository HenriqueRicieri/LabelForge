using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// The footprints that were still guesses. Each is now measured against the ink the
/// renderer lays down, because "close enough for selection" is what let a lowercase word
/// sit half again too wide and a numeric Data Matrix draw two sizes too big.
/// </summary>
public sealed class TextMetricsTests
{
    /// <summary>
    /// Text width against rendered ink. Font 0 is proportional, so the cases are chosen
    /// to span the range a single average hid: narrow lowercase, capitals, digits, and
    /// the widest glyphs in the font.
    ///
    /// The tolerance is a few dots rather than zero because a run's outer side bearings
    /// are not part of any glyph's advance. Under the old constant the same cases were
    /// out by up to 160 per cent.
    /// </summary>
    [Theory]
    [InlineData("HELLO", 40, 0)]
    [InlineData("hello", 40, 0)]
    [InlineData("iiiii", 40, 0)]
    [InlineData("WWWWW", 40, 0)]
    [InlineData("12345", 40, 0)]
    [InlineData("Mixed Case 123", 40, 0)]
    [InlineData("DESCRICAO DO PRODUTO", 30, 0)]
    [InlineData("HELLO", 20, 0)]
    [InlineData("HELLO", 80, 0)]
    [InlineData("HELLO", 40, 20)]
    [InlineData("HELLO", 40, 60)]
    [InlineData("WWWWW", 40, 40)]
    public void TextWidth_MatchesTheRenderedInk(string text, int height, int width)
    {
        var element = new TextElement
        {
            X = 100, Y = 40, Text = text, FontHeightDots = height, FontWidthDots = width,
        };

        int ink = InkWidth(element);
        int bounds = new ElementBoundsCalculator().GetBounds(element).Width;

        Assert.InRange(bounds, ink - 6, ink + 6);
    }

    /// <summary>The proportions are what a single constant cannot express: the widest
    /// glyph in the font costs three and a half times the narrowest.</summary>
    [Fact]
    public void TheFontIsProportional_AndTheTableSaysSo()
    {
        Assert.True(TextMetrics.Ratio('W') > 3 * TextMetrics.Ratio('i'));
        Assert.True(TextMetrics.Ratio('H') > TextMetrics.Ratio('h'));

        // Anything unnamed, including the accented letters real labels are full of,
        // lands between a lowercase and a capital rather than at either extreme.
        Assert.Equal(TextMetrics.DefaultRatio, TextMetrics.Ratio('ç'));
    }

    /// <summary>An explicit width stretches the font rather than making it fixed pitch,
    /// so every glyph keeps its proportion and only the scale changes.</summary>
    [Fact]
    public void AnExplicitWidth_ScalesEveryGlyphAlike()
    {
        int narrow = TextMetrics.WidthDots("HELLO", 40, 20);
        int wide = TextMetrics.WidthDots("HELLO", 40, 60);

        // Within a dot rather than exactly, because the total is rounded once at the end:
        // the same string is 54.6 dots at one width and 163.8 at three times it, and those
        // round in opposite directions.
        Assert.InRange(wide, narrow * 3 - 2, narrow * 3 + 2);
    }

    /// <summary>
    /// Data Matrix against the ink. ECC 200's ASCII mode packs a digit pair into one
    /// codeword, so counting characters chose a symbol too big for anything numeric.
    /// </summary>
    [Theory]
    [InlineData("AB", 10)]
    [InlineData("LF-000123", 14)]
    [InlineData("0123456789012345", 14)]
    [InlineData("0123456789012345678901234567890123456789", 20)]
    public void DataMatrix_MatchesTheRenderedInk(string data, int expectedModules)
    {
        var element = new DataMatrixElement { X = 100, Y = 40, Data = data, ModuleSizeDots = 2 };

        Assert.Equal(expectedModules * 2, InkWidth(element));
        Assert.Equal(expectedModules * 2, new ElementBoundsCalculator().GetBounds(element).Width);
    }

    /// <summary>
    /// The QR offset was already right and is pinned so it stays that way. It is a fixed
    /// number of dots, not a number of modules: across magnifications and symbol versions
    /// it never moved, which is why nothing scales it.
    /// </summary>
    [Theory]
    [InlineData("HI", 2, 21)]
    [InlineData("HI", 4, 21)]
    [InlineData("HI", 8, 21)]
    [InlineData("LF-000123456789012345", 4, 25)]
    public void Qr_MatchesTheRenderedInkAndItsTenDotOffset(
        string data, int magnification, int expectedModules)
    {
        var element = new QrCodeElement
        {
            X = 100, Y = 40, Data = data, Magnification = magnification,
        };

        DotRect ink = Ink(element);
        DotRect bounds = new ElementBoundsCalculator().GetBounds(element);

        Assert.Equal(expectedModules * magnification, ink.Width);
        Assert.Equal(ink.Width, bounds.Width);
        Assert.Equal(ink.Y, bounds.Y);
        Assert.Equal(50, ink.Y);
    }

    private static int InkWidth(Element element) => Ink(element).Width;

    private static DotRect Ink(Element element)
    {
        var document = new LabelDocument { WidthMm = 260, HeightMm = 60, Dpmm = 8 };
        document.Elements.Add(element);

        RenderResult result = new BinaryKitsRenderer()
            .Render(new ZplGenerator().Generate(document), 260, 60, 8);
        Assert.Empty(result.Errors);

        using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
        Assert.NotNull(bitmap);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red >= 128)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0 ? default : new DotRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
