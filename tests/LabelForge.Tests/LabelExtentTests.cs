using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// Sizing a label the file never sized. `^PW` and `^LL` are optional and real files leave
/// them out: 28 of the 29 in the sample corpus state neither, because a label sent to a
/// printer already set up for its stock has no reason to.
///
/// A fixed default is a guess, and one that comes out too small is not cosmetic. Anything
/// past the edge is off the label, so it stops printing and leaves the generated ZPL
/// entirely. Ten corpus files draw past a 150 mm default and lost the difference; the
/// biggest is 380 mm long.
/// </summary>
public sealed class LabelExtentTests
{
    private static ZplDocumentImportResult Import(string body) =>
        ZplDocumentImport.FromZpl($"^XA{body}^XZ", dpmm: 8);

    [Fact]
    public void AStatedSize_IsUsedAndNothingIsInferred()
    {
        ZplDocumentImportResult result = Import("^PW400^LL800^FO10,10^A0N,30^FDhi^FS");

        Assert.Equal(50, result.Document.WidthMm);
        Assert.Equal(100, result.Document.HeightMm);
        Assert.Null(result.MeasuredSize);
    }

    [Fact]
    public void NoStatedSize_IsMeasuredFromWhatTheLabelDraws()
    {
        // A 320 x 1600 dot box at 8 dpmm is 40 by 200 mm, which is well past the
        // default and is exactly the shape that used to lose its lower half.
        ZplDocumentImportResult result = Import("^FO0,0^GB320,1600,4,B^FS");

        Assert.Equal(40, result.Document.WidthMm);
        Assert.Equal(200, result.Document.HeightMm);
        Assert.NotNull(result.MeasuredSize);
        Assert.Contains("no label size", result.MeasuredSize, StringComparison.Ordinal);
    }

    /// <summary>Each axis is decided on its own, because a file can state one and not
    /// the other.</summary>
    [Theory]
    [InlineData("^PW800", 100.0, 200.0, "no label length")]
    [InlineData("^LL1600", 40.0, 200.0, "no label width")]
    public void OnlyTheAxisTheFileLeftOut_IsMeasured(
        string stated, double widthMm, double heightMm, string says)
    {
        ZplDocumentImportResult result = Import($"{stated}^FO0,0^GB320,1600,4,B^FS");

        Assert.Equal(widthMm, result.Document.WidthMm);
        Assert.Equal(heightMm, result.Document.HeightMm);
        Assert.Contains(says, result.MeasuredSize, StringComparison.Ordinal);
    }

    /// <summary>
    /// The measurement that would otherwise be wrong, and it is the E1g insight again: a
    /// marker's width is not the width of the value that replaces it. 101.zpl writes
    /// `##PESO_LIQUIDO@0,000##` at 89 dots per character, two and a half times the
    /// number that fills it, and measuring that placeholder made the label three quarters
    /// longer than it is.
    /// </summary>
    [Fact]
    public void AMarkerFieldIsMeasuredByItsOriginRatherThanItsPlaceholder()
    {
        // The same field twice over, once carrying a marker and once carrying a value.
        // Rotated, so the placeholder's width is what runs down the label.
        ZplDocumentImportResult marker =
            Import("^FO40,40^A0R,119,89^FD##PESO_LIQUIDO@0,000##^FS^FO0,0^GB320,800,4,B^FS");
        ZplDocumentImportResult value =
            Import("^FO40,40^A0R,119,89^FD12,345^FS^FO0,0^GB320,800,4,B^FS");

        // The box decides the size in both cases; the marker adds nothing beyond it.
        Assert.Equal(100, marker.Document.HeightMm);
        Assert.Equal(marker.Document.HeightMm, value.Document.HeightMm);
    }

    /// <summary>A literal field still counts for everything it draws: only the part
    /// nobody can measure is left out.</summary>
    [Fact]
    public void AFieldWithNoMarkerCountsForItsWholeFootprint()
    {
        ZplDocumentImportResult result = Import("^FO40,40^A0R,119,89^FDLONG ENOUGH TO MATTER^FS");

        Assert.True(
            result.Document.HeightMm > 100,
            $"a rotated line of that size runs well down the label ({result.Document.HeightMm} mm)");
    }

    [Fact]
    public void AnEmptyBlock_KeepsTheDefaultRatherThanCollapsing()
    {
        ZplDocumentImportResult result = Import("^FS");

        Assert.Empty(result.Document.Elements);
        Assert.Equal(new LabelDocument().WidthMm, result.Document.WidthMm);
        Assert.Null(result.MeasuredSize);
    }

    /// <summary>Whole millimetres, rounded up: stock comes in them, a dot-exact size
    /// reads as a measurement artifact, and rounding up means nothing lands past the edge
    /// it was measured from.</summary>
    [Fact]
    public void TheMeasurementRoundsUpToWholeMillimetres()
    {
        // 323 dots at 8 dpmm is 40.375 mm.
        (double WidthMm, double HeightMm)? extent = LabelExtent.MeasureMm(
            [new BoxElement { X = 0, Y = 0, WidthDots = 323, HeightDots = 323 }], 8);

        Assert.Equal((41.0, 41.0), extent);
    }

    [Fact]
    public void NothingToMeasure_IsNoAnswerRatherThanZero()
    {
        Assert.Null(LabelExtent.MeasureMm([], 8));
    }

    /// <summary>
    /// The whole point, end to end: a field past the old default used to be off the label,
    /// which meant it was not printable and never reached the generated ZPL at all.
    /// </summary>
    [Fact]
    public void ContentPastTheOldDefault_StillPrints()
    {
        ZplDocumentImportResult result = Import(
            "^FO20,20^A0N,30^FDtop^FS^FO20,1500^A0N,30^FDbottom^FS");

        Assert.All(
            result.Document.Elements,
            e => Assert.True(ElementPlacement.IsPrintable(e, result.Document)));

        string zpl = new LabelForge.Core.Zpl.ZplGenerator().Generate(result.Document);
        Assert.Contains("^FDbottom^FS", zpl, StringComparison.Ordinal);
    }
}
