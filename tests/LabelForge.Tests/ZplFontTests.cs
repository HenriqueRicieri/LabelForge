using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// The printer's built-in fonts. Every number here comes from the ZPL manual's font
/// matrices and was then confirmed against Labelary, which renders what a printer prints:
/// the measured advance per character is width plus intercharacter gap for all eight
/// bitmapped fonts, and the measured baseline is the manual's number for all eight.
///
/// That is why this is the model's authority rather than a measurement of our own offline
/// renderer, which is the usual rule here. The renderer draws fonts A, C and D exactly
/// right and is wrong about the other five - 7.33 against 9 for B, 13.89 against 19 for H
/// - so matching it would mean generating ZPL that prints at a width the model does not
/// know about. The printed label is the thing that has to be right; the properties panel
/// says where the preview cannot be trusted.
/// </summary>
public sealed class ZplFontTests
{
    /// <summary>The manual's 203 dpi matrix, which Labelary reproduces to the dot.</summary>
    [Theory]
    [InlineData('A', 9, 5, 1, 7)]
    [InlineData('B', 11, 7, 2, 11)]
    [InlineData('C', 18, 10, 2, 14)]
    [InlineData('D', 18, 10, 2, 14)]
    [InlineData('E', 28, 15, 5, 23)]
    [InlineData('F', 26, 13, 3, 21)]
    [InlineData('G', 60, 40, 8, 48)]
    [InlineData('H', 21, 13, 6, 21)]
    public void TheCellsAreTheManuals(char font, int h, int w, int gap, int baseline)
    {
        Assert.Equal(new FontCell(h, w, gap, baseline), ZplFont.Cell(font, 8));
    }

    [Fact]
    public void TheScalableFontHasNoCell()
    {
        Assert.Null(ZplFont.Cell('0', 8));
        Assert.True(ZplFont.IsScalable('0'));
        Assert.False(ZplFont.IsScalable('D'));
    }

    /// <summary>Only OCR-A and OCR-B grow with the printhead; every other cell is the
    /// same number of dots at any density, which is why a 9 dot cell is a smaller mark on
    /// a 300 dpi printer than on a 203 dpi one.</summary>
    [Fact]
    public void OnlyTheOcrFontsChangeWithDensity()
    {
        foreach (char font in "ABCDFG")
        {
            Assert.Equal(ZplFont.Cell(font, 8), ZplFont.Cell(font, 12));
        }

        Assert.Equal(28, ZplFont.Cell('E', 8)!.Value.HeightDots);
        Assert.Equal(42, ZplFont.Cell('E', 12)!.Value.HeightDots);
        Assert.Equal(21, ZplFont.Cell('H', 8)!.Value.HeightDots);
        Assert.Equal(34, ZplFont.Cell('H', 12)!.Value.HeightDots);
    }

    /// <summary>
    /// Fixed pitch is the whole difference from font 0: the manual says the gap between M
    /// and W is the same as between I and E, and Labelary agrees. So a bitmapped field's
    /// width is a count of cells and the characters do not enter into it.
    /// </summary>
    [Fact]
    public void ABitmappedFontIsMeasuredByCountRatherThanByCharacters()
    {
        var wide = new TextElement { Font = 'D', Text = "MMMM", FontHeightDots = 18, FontWidthDots = 10 };
        var narrow = new TextElement { Font = 'D', Text = "iiii", FontHeightDots = 18, FontWidthDots = 10 };

        Assert.Equal(TextMetrics.WidthDots(wide), TextMetrics.WidthDots(narrow));

        // Four cells of 10 plus three gaps of 2. The trailing gap follows the last
        // character rather than separating it from anything, so it is not counted.
        Assert.Equal(46, TextMetrics.WidthDots(wide));
    }

    /// <summary>And the scalable font stays proportional, which is the case that would
    /// break if the two paths ever got crossed.</summary>
    [Fact]
    public void TheScalableFontStaysProportional()
    {
        var wide = new TextElement { Font = '0', Text = "MMMM", FontHeightDots = 30 };
        var narrow = new TextElement { Font = '0', Text = "iiii", FontHeightDots = 30 };

        Assert.True(
            TextMetrics.WidthDots(wide) > TextMetrics.WidthDots(narrow),
            "font 0 is proportional, so M must cost more than i");
    }

    /// <summary>ZPL takes the size in dots but a bitmapped font only prints whole
    /// multiples of its cell, 1 to 10.</summary>
    [Theory]
    [InlineData(18, 1)]
    [InlineData(36, 2)]
    [InlineData(53, 2)]   // Not quite 3 cells, so the printer draws 2.
    [InlineData(54, 3)]
    [InlineData(9999, 10)]
    public void ABitmappedSizeIsAWholeMultipleOfItsCell(int requested, int expected)
    {
        Assert.Equal(expected, ZplFont.Magnification('D', requested, vertical: true, 8));
    }

    [Fact]
    public void MagnificationScalesTheWholeField()
    {
        var once = new TextElement { Font = 'D', Text = "ABCD", FontHeightDots = 18, FontWidthDots = 10 };
        var thrice = new TextElement { Font = 'D', Text = "ABCD", FontHeightDots = 54, FontWidthDots = 30 };

        Assert.Equal(46, TextMetrics.WidthDots(once));
        Assert.Equal(46 * 3, TextMetrics.WidthDots(thrice));
    }

    // ---- The ZPL itself ---------------------------------------------------------------

    [Fact]
    public void TheFontDesignatorIsPartOfTheCommandName()
    {
        var document = new LabelDocument { WidthMm = 60, HeightMm = 40, Dpmm = 8 };
        document.Elements.Add(new TextElement
        {
            X = 10, Y = 10, Font = 'D', Text = "hello", FontHeightDots = 18, FontWidthDots = 10,
        });
        document.Elements.Add(new TextElement { X = 10, Y = 60, Text = "scalable", FontHeightDots = 30 });

        string zpl = new ZplGenerator().Generate(document);

        Assert.Contains("^FO10,10^ADN,18,10^FDhello^FS", zpl, StringComparison.Ordinal);
        Assert.Contains("^FO10,60^A0N,30^FDscalable^FS", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_KeepsTheFontPerField()
    {
        var document = new LabelDocument { WidthMm = 60, HeightMm = 40, Dpmm = 8 };
        foreach (char f in ZplFont.Supported)
        {
            FontCell? cell = ZplFont.Cell(f, 8);
            document.Elements.Add(new TextElement
            {
                X = 10, Y = 10 + (document.Elements.Count * 20), Font = f, Text = "SAMPLE",
                FontHeightDots = cell?.HeightDots ?? 30,
                FontWidthDots = cell?.WidthDots ?? 0,
            });
        }

        string first = new ZplGenerator().Generate(document);
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(first, dpmm: 8);

        Assert.Empty(imported.Warnings);
        Assert.Equal(first, new ZplGenerator().Generate(imported.Document));
        Assert.Equal(
            ZplFont.Supported,
            imported.Document.Elements.Cast<TextElement>().Select(t => t.Font).ToArray());
    }

    /// <summary>The shape 542 (2).zpl is written in, and what used to be reported as a
    /// loss. A bitmapped font with no size asked for is its own cell, which is the
    /// printer's own fallback.</summary>
    [Fact]
    public void AFontWithNoSize_IsItsOwnCell()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^PW800^LL600^FO10,10^ADN^FDhello^FS^XZ", 8);

        var text = Assert.IsType<TextElement>(Assert.Single(result.Document.Elements));
        Assert.Equal('D', text.Font);
        Assert.Equal(18, text.FontHeightDots);
        Assert.Equal(10, text.FontWidthDots);
        Assert.Empty(result.Warnings);
    }

    /// <summary>A font the printer was given rather than born with lives in its memory,
    /// so there is nothing here to draw it. Reported rather than silently substituted,
    /// because the width will be wrong and that is worth knowing.</summary>
    [Fact]
    public void ADownloadedFont_FallsBackAndSaysSo()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^PW800^LL600^FO10,10^AZN,40,30^FDhello^FS^XZ", 8);

        var text = Assert.IsType<TextElement>(Assert.Single(result.Document.Elements));
        Assert.Equal(ZplFont.Scalable, text.Font);
        Assert.Contains("^AZ", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    /// <summary>The baseline is published per font rather than being a fraction of the
    /// size, so `^FT` places a bitmapped field from the manual's number. Font D's baseline
    /// is 14 of its 18 dots, leaving 4 below it.</summary>
    [Fact]
    public void ATypesetBitmappedField_UsesItsOwnBaseline()
    {
        var element = new TextElement
        {
            X = 100, Y = 300, Font = 'D', Text = "Mg", FontHeightDots = 18, FontWidthDots = 10,
            Anchor = FieldAnchor.Baseline,
        };

        DotRect bounds = new ElementBoundsCalculator().GetBounds(element);

        Assert.Equal(300 - 14, bounds.Y);
    }

    /// <summary>Which fonts the preview can be trusted on, pinned because the panel says
    /// so to the user and because it is a fact about a dependency rather than about us.</summary>
    [Theory]
    [InlineData('0', true)]
    [InlineData('A', true)]
    [InlineData('C', true)]
    [InlineData('D', true)]
    [InlineData('B', false)]
    [InlineData('E', false)]
    [InlineData('G', false)]
    [InlineData('H', false)]
    public void TheRenderersFaithfulnessIsKnownPerFont(char font, bool faithful)
    {
        Assert.Equal(faithful, ZplFont.RendersFaithfully(font));
    }
}
