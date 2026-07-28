using LabelForge.Core.Io;
using LabelForge.Core.Model;

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
}
