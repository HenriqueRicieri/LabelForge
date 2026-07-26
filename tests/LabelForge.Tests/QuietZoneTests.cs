using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// The blank margin a symbol needs around it. A barcode that takes a second pass to
/// scan is a label that failed, and the crowding that causes it looks like tidy layout
/// on screen, so it has to be measured rather than eyeballed.
/// </summary>
public sealed class QuietZoneTests
{
    private static LabelDocument Label(params Element[] elements)
    {
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        foreach (Element element in elements)
        {
            document.Elements.Add(element);
        }

        return document;
    }

    private static BarcodeElement Code128(int x, int y, string data = "ABC12345") => new()
    {
        X = x, Y = y, Symbology = BarcodeSymbology.Code128, Data = data,
        HeightDots = 80, ModuleWidthDots = 2, PrintInterpretationLine = false,
    };

    /// <summary>
    /// The footprints the quiet zone is measured from, against the ink the renderer
    /// actually lays down. Every case is exact, which is what the whole feature rests on:
    /// a margin measured from a footprint that guesses is a margin that lies.
    ///
    /// Two of these were wrong before and the errors ran opposite ways. Code 128 assumed
    /// digits pair into one symbol, which needs subset C and neither the renderer nor a
    /// printer in ^BC's default mode uses it, so a 16-digit barcode's outline stopped 132
    /// dots short of its own ink. EAN-13 and UPC-A went the other way, carrying the quiet
    /// zone inside the footprint and only on the trailing side.
    /// </summary>
    [Theory]
    [InlineData(BarcodeSymbology.Code128, "A", 46)]
    [InlineData(BarcodeSymbology.Code128, "ABC12345", 123)]
    [InlineData(BarcodeSymbology.Code128, "ABCDEFGHIJ", 145)]
    [InlineData(BarcodeSymbology.Code128, "12345678", 123)]
    [InlineData(BarcodeSymbology.Code128, "1234567890123456", 211)]
    [InlineData(BarcodeSymbology.Code39, "A", 47)]
    [InlineData(BarcodeSymbology.Code39, "ABC12345", 159)]
    [InlineData(BarcodeSymbology.Code39, "ABCDEFGHIJ", 191)]
    [InlineData(BarcodeSymbology.Ean13, "5901234123457", 95)]
    [InlineData(BarcodeSymbology.Ean13, "590123412345", 95)]
    [InlineData(BarcodeSymbology.UpcA, "036000291452", 95)]
    [InlineData(BarcodeSymbology.UpcA, "03600029145", 95)]
    public void BarcodeFootprint_IsTheSymbolAndNothingElse(
        BarcodeSymbology symbology, string data, int expectedModules)
    {
        const int module = 2;
        var element = new BarcodeElement
        {
            X = 100, Y = 100, Symbology = symbology, Data = data, HeightDots = 80,
            ModuleWidthDots = module, PrintInterpretationLine = false,
        };

        var document = new LabelDocument { WidthMm = 160, HeightMm = 60, Dpmm = 8 };
        document.Elements.Add(element);

        RenderResult result = new BinaryKitsRenderer()
            .Render(new ZplGenerator().Generate(document), 160, 60, 8);
        Assert.Empty(result.Errors);

        (int inkX, int inkWidth) = InkColumns(result);
        DotRect bounds = new ElementBoundsCalculator().GetBounds(element);

        // The renderer draws no quiet zone: the first bar lands on the field origin,
        // which is exactly why one has to be kept clear by hand.
        Assert.Equal(100, inkX);
        Assert.Equal(expectedModules * module, inkWidth);
        Assert.Equal(expectedModules * module, bounds.Width);
    }

    [Theory]
    [InlineData(BarcodeSymbology.Code128, 10, 10)]
    [InlineData(BarcodeSymbology.Code39, 10, 10)]
    [InlineData(BarcodeSymbology.Ean13, 11, 7)]
    [InlineData(BarcodeSymbology.UpcA, 9, 9)]
    public void LinearSymbologies_ReserveTheirStandardMargin(
        BarcodeSymbology symbology, int leftModules, int rightModules)
    {
        var element = new BarcodeElement
        {
            Symbology = symbology, Data = "12345678901", ModuleWidthDots = 3,
        };

        QuietZoneMargin margin = QuietZone.For(element);

        Assert.Equal(leftModules * 3, margin.Left);
        Assert.Equal(rightModules * 3, margin.Right);

        // Linear symbologies ask for nothing above or below the bars, which is why a
        // caption can sit right under one.
        Assert.Equal(0, margin.Top);
        Assert.Equal(0, margin.Bottom);
    }

    [Fact]
    public void TwoDimensionalSymbologies_ReserveTheirMarginOnEverySide()
    {
        Assert.Equal(
            new QuietZoneMargin(16, 16, 16, 16),
            QuietZone.For(new QrCodeElement { Magnification = 4 }));
        Assert.Equal(
            new QuietZoneMargin(4, 4, 4, 4),
            QuietZone.For(new DataMatrixElement { ModuleSizeDots = 4 }));
        Assert.Equal(
            new QuietZoneMargin(6, 6, 6, 6),
            QuietZone.For(new Pdf417Element { ModuleWidthDots = 3 }));
    }

    [Fact]
    public void ElementsThatAreNotSymbols_HaveNoQuietZone()
    {
        Assert.True(QuietZone.For(new TextElement { Text = "x" }).IsEmpty);
        Assert.False(QuietZone.Applies(new BoxElement()));
        Assert.True(QuietZone.Applies(new QrCodeElement()));
    }

    [Fact]
    public void ANeighbourInTheMargin_IsReported()
    {
        BarcodeElement barcode = Code128(200, 100);

        // 10 modules at 2 dots is 20 dots of blank; a box ending 8 dots short of the
        // first bar is inside it while never touching the ink.
        var crowding = new BoxElement { X = 100, Y = 100, WidthDots = 92, HeightDots = 40 };

        IReadOnlyList<QuietZoneFinding> findings =
            QuietZoneChecker.Check(Label(barcode, crowding));

        QuietZoneFinding finding = Assert.Single(findings);
        Assert.Same(barcode, finding.Code);
        Assert.Same(crowding, finding.Intruder);
    }

    [Fact]
    public void AClearMargin_IsNotReported()
    {
        BarcodeElement barcode = Code128(200, 100);
        var clear = new BoxElement { X = 100, Y = 100, WidthDots = 79, HeightDots = 40 };

        Assert.Empty(QuietZoneChecker.Check(Label(barcode, clear)));
    }

    /// <summary>Above and below a linear barcode is not its quiet zone, so a caption
    /// tucked under the bars is fine and must not be nagged about.</summary>
    [Fact]
    public void ACaptionUnderALinearBarcode_IsFine()
    {
        BarcodeElement barcode = Code128(200, 100);
        var caption = new TextElement { X = 200, Y = 185, Text = "ABC12345", FontHeightDots = 24 };

        Assert.Empty(QuietZoneChecker.Check(Label(barcode, caption)));
    }

    [Fact]
    public void ASymbolFlushWithTheStockEdge_IsReported()
    {
        BarcodeElement barcode = Code128(0, 100);

        QuietZoneFinding finding = Assert.Single(QuietZoneChecker.Check(Label(barcode)));

        Assert.Same(barcode, finding.Code);
        Assert.Null(finding.Intruder);
    }

    /// <summary>A roll has no bottom edge, so a symbol far down it has not run off
    /// anything; the sides still count.</summary>
    [Fact]
    public void OnContinuousStock_OnlyTheSidesCanBeRunOff()
    {
        LabelDocument roll = Label(Code128(200, 4000));
        roll.IsContinuous = true;

        Assert.Empty(QuietZoneChecker.Check(roll));
    }

    [Fact]
    public void NothingThatDoesNotPrint_Counts()
    {
        BarcodeElement barcode = Code128(200, 100);
        var hidden = new BoxElement { X = 100, Y = 100, WidthDots = 92, HeightDots = 40 };
        hidden.IsVisible = false;
        var suppressed = new BoxElement { X = 100, Y = 100, WidthDots = 92, HeightDots = 40 };
        suppressed.DoNotPrint = true;

        Assert.Empty(QuietZoneChecker.Check(Label(barcode, hidden)));
        Assert.Empty(QuietZoneChecker.Check(Label(barcode, suppressed)));

        // And a symbol that will not print does not need a margin either.
        BarcodeElement offDuty = Code128(200, 100);
        offDuty.DoNotPrint = true;
        Assert.Empty(QuietZoneChecker.Check(
            Label(offDuty, new BoxElement { X = 100, Y = 100, WidthDots = 92, HeightDots = 40 })));
    }

    [Fact]
    public void TheCheck_CanBeTurnedOff()
    {
        LabelDocument document = Label(
            Code128(200, 100),
            new BoxElement { X = 100, Y = 100, WidthDots = 92, HeightDots = 40 });

        Assert.NotEmpty(QuietZoneChecker.Check(document));

        document.CheckQuietZones = false;

        Assert.Empty(QuietZoneChecker.Check(document));
    }

    /// <summary>A quiet zone is blank stock, so none of this can reach the printer.</summary>
    [Fact]
    public void NoneOfThisChangesTheZpl()
    {
        LabelDocument crowded = Label(
            Code128(0, 100),
            new BoxElement { X = 100, Y = 100, WidthDots = 92, HeightDots = 40 });
        string checkedZpl = new ZplGenerator().Generate(crowded);

        crowded.CheckQuietZones = false;

        Assert.Equal(checkedZpl, new ZplGenerator().Generate(crowded));
        Assert.DoesNotContain("quiet", checkedZpl, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The leftmost and rightmost ink columns, in dots.</summary>
    private static (int X, int Width) InkColumns(RenderResult result)
    {
        using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
        Assert.NotNull(bitmap);

        int minX = int.MaxValue, maxX = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red >= 128)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
            }
        }

        return maxX < 0 ? (0, 0) : (minX, maxX - minX + 1);
    }
}
