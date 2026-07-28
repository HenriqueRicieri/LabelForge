using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// Reading what the ZDesigner Windows driver produces (backlog E4).
///
/// A second kind of real input, and it is not shaped like the first. The Atak corpus is
/// hand-written and states almost nothing; driver output states its printer setup up
/// front, splits one print into several `^XA` blocks, compresses its graphics, and cleans
/// up after itself. `Fixtures/zdesigner-driver-output.zpl` is one capture of it: a line of
/// plain text printed through the driver, which it rasterized.
///
/// That rasterization is the point rather than a disappointment. The driver is a GDI
/// driver, so unless every object in a design names a printer-resident font or barcode,
/// what comes out is a bitmap. Anyone expecting these captures to be an oracle for our
/// font metrics should read that off this test first.
/// </summary>
public sealed class DriverOutputTests
{
    private static string DriverZpl() =>
        ZplTextFile.ReadFile(
            Path.Combine(TestCorpus.FixturesDirectory(), "zdesigner-driver-output.zpl")).Text;

    /// <summary>The other capture: the same driver asked for the printer's own resident
    /// fonts instead, which is what makes it emit ^A commands rather than a bitmap.</summary>
    private static string PrinterFontZpl() =>
        ZplTextFile.ReadFile(
            Path.Combine(TestCorpus.FixturesDirectory(), "zdesigner-printer-fonts.zpl")).Text;

    /// <summary>
    /// The size comes from the setup block, not from measuring the content.
    ///
    /// This is the bug the driver found on its first capture. `^PW` and `^LL` are printer
    /// settings, not properties of one label: the manual says an `^LL` is "retained until
    /// you turn off the printer or send a new ^LL", and `^PW` defaults to "the last
    /// permanently saved value". The driver relies on exactly that, stating the width once
    /// in a configuration block and never again, so scoping it to its own block left the
    /// real label with no stated size and it was measured at half the width instead.
    /// </summary>
    [Fact]
    public void TheSetupBlocksPrintWidthAppliesToTheLabelThatFollows()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(DriverZpl(), dpmm: 8);

        Assert.Equal(822, result.Document.WidthDots);

        // The driver states a width and no length, so the length is measured and the report
        // names that one alone. Worth asserting rather than ignoring: it is the evidence
        // that the width was taken from the file and only the length was worked out.
        Assert.Contains("no label length", result.MeasuredSize);
        Assert.DoesNotContain("no label width", result.MeasuredSize);
    }

    /// <summary>The shape of a driver print: a configuration block, the label, and a block
    /// that deletes the graphic again. The block holding something is the one opened, which
    /// is the rule real files needed and this confirms on a second source.</summary>
    [Fact]
    public void ADriverPrintIsSeveralBlocksAndOnlyOneIsTheLabel()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(DriverZpl(), dpmm: 8);

        Assert.Equal(3, result.LabelCount);
        Assert.Equal([0, 1, 0], result.BlockElementCounts);
        Assert.Equal(1, result.SelectedIndex);
    }

    /// <summary>The driver compresses its graphics with :Z64: and downloads them outside
    /// any block, which is the reading half B9 built. One image comes back.</summary>
    [Fact]
    public void TheRasterizedTextComesBackAsTheImageItIs()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(DriverZpl(), dpmm: 8);

        var image = Assert.IsType<ImageElement>(Assert.Single(result.Document.Elements));
        Assert.True(image.WidthDots > 0 && image.HeightDots > 0);
        Assert.NotEmpty(image.ImageData);
    }

    /// <summary>
    /// The persistence rule on its own, away from the fixture: a size stated once carries
    /// forward, and a block that states its own still wins.
    /// </summary>
    [Fact]
    public void AStatedSizeCarriesForwardUntilRestated()
    {
        const string zpl =
            "^XA^PW822^LL406^XZ"
            + "^XA^FO10,10^A0N,30^FDinherits^FS^XZ"
            + "^XA^PW600^FO10,10^A0N,30^FDrestates the width^FS^XZ";

        ZplDocumentImportResult inherited = ZplDocumentImport.FromZpl(zpl, dpmm: 8, labelIndex: 1);
        Assert.Equal(822, inherited.Document.WidthDots);
        Assert.Equal(406, inherited.Document.HeightDots);

        ZplDocumentImportResult restated = ZplDocumentImport.FromZpl(zpl, dpmm: 8, labelIndex: 2);
        Assert.Equal(600, restated.Document.WidthDots);

        // The length was never restated, so it is still the one in force.
        Assert.Equal(406, restated.Document.HeightDots);
    }

    /// <summary>A file that never states a size is still measured from its content, which
    /// is E1h's rule and the case 28 of the 29 corpus files are in. Persistence must not
    /// quietly turn "nothing stated" into something stated.</summary>
    [Fact]
    public void NothingStatedIsStillMeasured()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^FO10,10^A0N,30^FDno size anywhere^FS^XZ", dpmm: 8);

        Assert.NotNull(result.MeasuredSize);
    }

    /// <summary>
    /// The bitmapped font cells, confirmed by a third source.
    ///
    /// B14 took these from the ZPL manual and checked them against Labelary, deliberately
    /// against the offline renderer, which draws five of the eight at the wrong width. The
    /// driver is an independent third opinion, and it is the one that matters commercially:
    /// it is what a Windows application actually sends when someone picks a printer font.
    /// Asked for each resident font, it emits exactly the cell `ZplFont` holds.
    ///
    /// Font A arrives at three times its cell rather than at it, which is the other half of
    /// B14 confirmed: a bitmapped font prints whole multiples of its cell, so the driver
    /// picked a multiple rather than the nearest number of dots.
    /// </summary>
    [Theory]
    [InlineData('A', 27, 15, 3)]
    [InlineData('B', 11, 7, 1)]
    [InlineData('E', 28, 15, 1)]
    [InlineData('F', 26, 13, 1)]
    [InlineData('G', 60, 40, 1)]
    [InlineData('H', 21, 13, 1)]
    public void TheDriverAsksForTheCellsTheManualPublishes(
        char font, int heightDots, int widthDots, int magnification)
    {
        string zpl = PrinterFontZpl();
        Assert.Contains(
            $"^A{font}N,{heightDots},{widthDots}", zpl, StringComparison.Ordinal);

        FontCell cell = Assert.NotNull(ZplFont.Cell(font, dpmm: 8));
        Assert.Equal(cell.HeightDots * magnification, heightDots);
        Assert.Equal(cell.WidthDots * magnification, widthDots);
    }

    /// <summary>
    /// Every field the driver placed comes back, in the command it was written with.
    ///
    /// The driver writes text with `^FT` and the barcode with `^FO`, in one file, which is
    /// the mix B5 exists to model. An importer that converted either way would regenerate a
    /// different label; this holds that it does not.
    /// </summary>
    [Fact]
    public void PrinterFontOutputRoundTrips()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(PrinterFontZpl(), dpmm: 8);

        Assert.Equal(8, result.Document.Elements.Count);
        Assert.Equal(7, result.Document.Elements.OfType<TextElement>().Count());

        var barcode = Assert.Single(result.Document.Elements.OfType<BarcodeElement>());
        Assert.Equal(BarcodeSymbology.Code39, barcode.Symbology);
        Assert.Equal("12345678", barcode.Data);
        Assert.Equal(4, barcode.ModuleWidthDots);
        Assert.Equal(2.5, barcode.WideBarRatio);
        Assert.Equal(FieldAnchor.TopLeft, barcode.Anchor);

        // Text is typeset by its baseline, which is how the driver writes it and how every
        // real label does.
        Assert.All(
            result.Document.Elements.OfType<TextElement>(),
            t => Assert.Equal(FieldAnchor.Baseline, t.Anchor));

        // The fonts survive as the designators they arrived as, rather than falling back.
        Assert.Equal(
            ['0', 'A', 'B', 'E', 'F', 'G', 'H'],
            result.Document.Elements.OfType<TextElement>().Select(t => t.Font));
    }

    /// <summary>The driver states its own code page, and it is one we model. Worth pinning
    /// because `^CI` decides what a `^FH` escape decodes to, and E1e settled that against
    /// the renderer rather than against a real producer of ZPL.</summary>
    [Fact]
    public void TheDriverStatesACodePageWeModel()
    {
        foreach (int set in new[] { 0, 27 })
        {
            Assert.Contains($"^CI{set}", PrinterFontZpl(), StringComparison.Ordinal);
            Assert.True(ZplCodePage.IsModelled(set));
        }
    }
}
