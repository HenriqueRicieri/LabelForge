using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// The human-readable interpretation line, and how much room it takes.
///
/// The footprint used to add a flat 30 dots for it whatever the barcode was drawn at. It
/// is not flat: the printer draws the line in font A magnified by the narrow-bar width,
/// so it scales with the module and with nothing else. The old constant was 9 dots too
/// generous at the default module of 2 and 6 dots too short at module 4, and short is the
/// direction that matters, since a footprint inside the ink says a label fits when it clips.
/// </summary>
public sealed class BarcodeInterpretationTests
{
    private const int Dpmm = 8;
    private const int LabelWidthMm = 160;
    private const int LabelHeightMm = 60;

    private static BarcodeElement Barcode(
        BarcodeSymbology symbology, string data, int module, bool line, int height = 100) => new()
        {
            X = 100, Y = 40, Symbology = symbology, Data = data, HeightDots = height,
            ModuleWidthDots = module, PrintInterpretationLine = line,
        };

    /// <summary>
    /// The measurement that matters: the footprint has to cover the ink the renderer puts
    /// down, at every module width, for every symbology.
    ///
    /// Never shorter is the hard rule. Never taller by more than a few dots is the soft
    /// one, and it is what makes this a measurement rather than a safe over-estimate: the
    /// box is the selection outline, the snap target and what a continuous label's length
    /// is measured from.
    /// </summary>
    [Theory]
    [InlineData(BarcodeSymbology.Code128, "12345678")]
    [InlineData(BarcodeSymbology.Code39, "ABC123")]
    [InlineData(BarcodeSymbology.Ean13, "123456789012")]
    [InlineData(BarcodeSymbology.UpcA, "12345678901")]
    [InlineData(BarcodeSymbology.Interleaved2of5, "123456")]
    public void TheFootprintCoversTheInk_AtEveryModuleWidth(
        BarcodeSymbology symbology, string data)
    {
        for (int module = 1; module <= 8; module++)
        {
            BarcodeElement off = Barcode(symbology, data, module, line: false);
            BarcodeElement on = Barcode(symbology, data, module, line: true);

            int barsBottom = InkBox(off).Bottom;
            int inkBottom = InkBox(on).Bottom;
            int boxBottom = new ElementBoundsCalculator().GetBounds(on).Y
                + new ElementBoundsCalculator().GetBounds(on).Height;

            Assert.True(
                boxBottom >= inkBottom,
                $"module {module}: the box stops at {boxBottom} and the ink runs to {inkBottom}");
            Assert.True(
                boxBottom - inkBottom <= 5,
                $"module {module}: the box stands {boxBottom - inkBottom} dots clear of the ink");

            // And the line is what the difference is made of, rather than the bars moving.
            Assert.Equal(
                BarcodeInterpretation.HeightDots(module),
                new ElementBoundsCalculator().GetBounds(on).Height
                    - new ElementBoundsCalculator().GetBounds(off).Height);
            Assert.True(inkBottom > barsBottom, "the line has to draw something");
        }
    }

    /// <summary>Turned off it takes nothing at all, which is what makes a barcode with no
    /// line measure exactly its bars.</summary>
    [Fact]
    public void TurnedOff_ItTakesNothing()
    {
        BarcodeElement off = Barcode(BarcodeSymbology.Code128, "12345678", 2, line: false);

        Assert.Equal(0, BarcodeInterpretation.HeightDots(off));
        Assert.Equal(100, new ElementBoundsCalculator().GetBounds(off).Height);
        Assert.Equal(139, InkBox(off).Bottom);
    }

    /// <summary>
    /// It sits under the bars wherever they end rather than scaling with them, measured at
    /// three heights. That is the half of B4 the premise got the wrong way round: there is
    /// nothing here for a resize to keep in proportion, because the line follows the module
    /// width and a taller barcode is not a wider one.
    /// </summary>
    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void TheBarHeightDoesNotMoveIt(int bars)
    {
        BarcodeElement element = Barcode(
            BarcodeSymbology.Code128, "12345678", 2, line: true, height: bars);

        // The same 19 dots of ink under the bars wherever they end, and inside the 21 the
        // model reserves for it.
        Assert.Equal(19, InkBox(element).Bottom - (element.Y + bars - 1));
        Assert.True(BarcodeInterpretation.HeightDots(2) >= 19);
    }

    /// <summary>A count of dots, not of millimetres: the same label at four print
    /// densities puts the line in the same place.</summary>
    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(24)]
    public void ThePrintDensityDoesNotMoveIt(int dpmm)
    {
        BarcodeElement element = Barcode(BarcodeSymbology.Code128, "12345678", 2, line: true);
        var document = new LabelDocument
        {
            WidthMm = LabelWidthMm, HeightMm = LabelHeightMm, Dpmm = dpmm,
        };
        document.Elements.Add(element);

        Assert.Equal(19, InkBox(document, dpmm).Bottom - (element.Y + element.HeightDots - 1));
    }

    /// <summary>
    /// Dragging the bottom handle sizes the bars so the whole thing lands where the pointer
    /// did, which means taking the line off the target first. It was taking 30 dots off
    /// whatever the module, so a wide barcode came out short and a narrow one tall.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void ResizingTargetsTheWholeFootprint(int module)
    {
        BarcodeElement element = Barcode(BarcodeSymbology.Code128, "12345678", module, line: true);
        DotRect before = new ElementBoundsCalculator().GetBounds(element);

        ElementResizer.Resize(element, before.Width, 200);

        Assert.Equal(200, new ElementBoundsCalculator().GetBounds(element).Height);
        Assert.Equal(200 - BarcodeInterpretation.HeightDots(module), element.HeightDots);
    }

    /// <summary>Nothing here reaches the printer: the line is on or off in the ZPL and its
    /// size is the printer's own arithmetic, so a footprint change cannot alter a byte.</summary>
    [Fact]
    public void NoneOfItReachesTheZpl()
    {
        var document = new LabelDocument { WidthMm = LabelWidthMm, HeightMm = LabelHeightMm, Dpmm = Dpmm };
        document.Elements.Add(Barcode(BarcodeSymbology.Code128, "12345678", 4, line: true));

        Assert.Equal(
            "^XA\n^CI28\n^PW1280\n^LL480\n^LH0,0\n"
            + "^BY4^FO100,40^BCN,100,Y,N,N^FD12345678^FS\n^XZ",
            new ZplGenerator().Generate(document));
    }

    /// <summary>
    /// A font stated before the barcode command sets the interpretation line's font, which
    /// the manual documents and this model does not carry. Named on import rather than
    /// dropped in silence, since the printed line changes size and nothing else in the file
    /// says so. Zero corpus files write one, so it is not wallpaper.
    /// </summary>
    [Fact]
    public void AFontStatedForTheLine_IsReportedRatherThanDroppedQuietly()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^PW800^LL480\n^BY2^FO40,40^A0N,40,40^BCN,100,Y,N,N^FD12345678^FS\n^XZ");

        Element element = Assert.Single(result.Document.Elements);
        Assert.IsType<BarcodeElement>(element);
        Assert.Contains(
            result.Warnings,
            w => w.Contains("interpretation line's font", StringComparison.Ordinal));
    }

    /// <summary>With no line to draw there is no font to lose, so the warning stays for the
    /// case that loses something.</summary>
    [Fact]
    public void AFontOnABarcodeWithNoLine_IsNotWorthReporting()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^PW800^LL480\n^BY2^FO40,40^A0N,40,40^BCN,100,N,N,N^FD12345678^FS\n^XZ");

        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// A known limit, measured and pinned rather than left to be rediscovered: EAN-13 and
    /// UPC-A print a digit outside their own guard bars - the leading digit at the left for
    /// both, the check digit at the right for UPC-A - and the footprint is the symbol, so
    /// that digit falls outside it. It costs nothing today because it lands inside the quiet
    /// zone B3 already draws and checks (11 modules leading for EAN-13 against the 6 the
    /// digit needs), but the box is narrower than the ink there, which is the direction that
    /// matters. Filed as B4a.
    /// </summary>
    [Theory]
    [InlineData(BarcodeSymbology.Ean13, "123456789012")]
    [InlineData(BarcodeSymbology.UpcA, "12345678901")]
    public void TheLeadingDigitFallsOutsideTheFootprint(BarcodeSymbology symbology, string data)
    {
        const int module = 2;
        BarcodeElement element = Barcode(symbology, data, module, line: true);

        DotRect bounds = new ElementBoundsCalculator().GetBounds(element);
        InkBounds ink = InkBox(element);

        Assert.True(ink.Left < bounds.X, $"ink starts at {ink.Left}, box at {bounds.X}");
        Assert.InRange(bounds.X - ink.Left, 1, QuietZone.For(element).Left);
    }

    private readonly record struct InkBounds(int Left, int Right, int Top, int Bottom);

    private static InkBounds InkBox(BarcodeElement element)
    {
        var document = new LabelDocument
        {
            WidthMm = LabelWidthMm, HeightMm = LabelHeightMm, Dpmm = Dpmm,
        };
        document.Elements.Add(element);
        return InkBox(document, Dpmm);
    }

    private static InkBounds InkBox(LabelDocument document, int dpmm)
    {
        RenderResult result = new BinaryKitsRenderer()
            .Render(new ZplGenerator().Generate(document), LabelWidthMm, LabelHeightMm, dpmm);
        Assert.Empty(result.Errors);

        using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
        Assert.NotNull(bitmap);

        int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        Assert.True(bottom >= 0, "the label drew nothing at all");
        return new InkBounds(left, right, top, bottom);
    }
}
