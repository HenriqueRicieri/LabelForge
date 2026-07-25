using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// Continuous stock: a roll with no gaps or die cuts. The label has no height of its
/// own, so it is exactly as long as its content, and the printer has to be told to stop
/// looking for a gap that is not there.
/// </summary>
public sealed class ContinuousMediaTests
{
    private static LabelDocument Roll(params Element[] elements)
    {
        var document = new LabelDocument
        {
            WidthMm = 80, HeightMm = 200, Dpmm = 8, IsContinuous = true, ContinuousMarginMm = 3,
        };
        foreach (Element element in elements)
        {
            document.Elements.Add(element);
        }

        return document;
    }

    private static BoxElement Box(int y, int height) => new()
    {
        X = 10, Y = y, WidthDots = 200, HeightDots = height, ThicknessDots = 2,
    };

    [Fact]
    public void Length_IsTheContentPlusTheGap()
    {
        // Bottom-most ink at 100 + 160 = 260 dots, plus 3 mm of gap at 8 dpmm.
        LabelDocument document = Roll(Box(20, 40), Box(100, 160));

        Assert.Equal(260 / 8.0 + 3, document.HeightMm, 3);
        Assert.Equal(260 + 24, document.HeightDots);
    }

    /// <summary>The stored height is not thrown away, so unticking continuous puts back
    /// a real die-cut size instead of freezing whatever the content last measured.</summary>
    [Fact]
    public void TheDieCutHeight_SurvivesUnderneath()
    {
        LabelDocument document = Roll(Box(20, 40));
        Assert.NotEqual(200, document.HeightMm, 3);

        document.IsContinuous = false;

        Assert.Equal(200, document.HeightMm, 3);
    }

    [Fact]
    public void AnEmptyRoll_StillHasASurfaceToDropTheFirstElementOnto() =>
        Assert.Equal(ContinuousLength.MinimumLengthMm, Roll().HeightMm, 3);

    /// <summary>Only ink that prints advances the roll. Anything hidden, suppressed or
    /// parked off the label leaves no mark, so paying media for it would be wrong.</summary>
    [Fact]
    public void OnlyPrintingContent_MakesTheLabelLonger()
    {
        double baseline = Roll(Box(20, 40)).HeightMm;

        var hidden = Box(400, 40);
        hidden.IsVisible = false;
        var suppressed = Box(500, 40);
        suppressed.DoNotPrint = true;
        var parked = Box(600, 40);
        parked.X = -50;

        Assert.Equal(baseline, Roll(Box(20, 40), hidden).HeightMm, 3);
        Assert.Equal(baseline, Roll(Box(20, 40), suppressed).HeightMm, 3);
        Assert.Equal(baseline, Roll(Box(20, 40), parked).HeightMm, 3);
    }

    /// <summary>There is no bottom edge on a roll, so an element far down the media is
    /// ordinary content rather than something parked off the label.</summary>
    [Fact]
    public void ThereIsNoBottomEdgeToFallOff()
    {
        LabelDocument document = Roll(Box(20, 40));
        var far = Box(4000, 40);
        document.Elements.Add(far);

        Assert.True(ElementPlacement.IsPrintable(far, document));
        Assert.Equal(
            PlacementStatus.Inside,
            ElementPlacement.Classify(far, new ElementBoundsCalculator().GetBounds(far), document));
        Assert.Contains("^FO10,4000", new ZplGenerator().Generate(document), StringComparison.Ordinal);
    }

    /// <summary>A die-cut label is unchanged in every respect, which is what makes this
    /// safe to add: the flag is off, so nothing about an existing label moves.</summary>
    [Fact]
    public void DieCutStock_IsUntouched()
    {
        var document = new LabelDocument { WidthMm = 80, HeightMm = 50, Dpmm = 8 };
        document.Elements.Add(Box(20, 40));

        string zpl = new ZplGenerator().Generate(document);

        Assert.Equal(50, document.HeightMm, 3);
        Assert.DoesNotContain("^MN", zpl, StringComparison.Ordinal);
        Assert.Contains("^LL400", zpl, StringComparison.Ordinal);
    }

    /// <summary>^MNN is emitted for continuous stock and for nothing else. A roll with no
    /// gaps must be declared or the printer feeds forward hunting for one; gap and mark
    /// sensing are the operator's to set, so they are left alone.</summary>
    [Fact]
    public void ContinuousStock_TellsThePrinterToStopSensingGaps()
    {
        string zpl = new ZplGenerator().Generate(Roll(Box(20, 40)));

        Assert.Contains("^MNN", zpl, StringComparison.Ordinal);
        Assert.Contains("^LL" + (60 + 24), zpl, StringComparison.Ordinal);
    }

    /// <summary>The preview is fed to the offline renderer, which would only report ^MNN
    /// as an unknown command, so it stays out for the same reason job settings do.</summary>
    [Fact]
    public void ThePreview_CarriesNoPrinterCommands() =>
        Assert.DoesNotContain(
            "^MN", new ZplGenerator().GeneratePreview(Roll(Box(20, 40)), 0), StringComparison.Ordinal);

    [Fact]
    public void ARollHasNoCornersToRound()
    {
        LabelDocument document = Roll(Box(20, 40));
        document.CornerRadiusMm = 5;

        Assert.Equal(0, document.EffectiveCornerRadiusMm, 3);
    }

    [Fact]
    public void RoundTrip_ThroughTheProjectFile()
    {
        LabelDocument document = Roll(Box(20, 40));
        document.ContinuousMarginMm = 6;

        LabelDocument restored = LabelDocumentJson.Deserialize(
            LabelDocumentJson.Serialize(document));

        Assert.True(restored.IsContinuous);
        Assert.Equal(6, restored.ContinuousMarginMm, 3);
        Assert.Equal(document.HeightMm, restored.HeightMm, 3);
    }

    /// <summary>An older .lfl has neither field, and has to keep its stored height.</summary>
    [Fact]
    public void AnOlderProjectFile_IsStillDieCut()
    {
        const string legacy = """
            {"SchemaVersion":1,"Document":{"WidthMm":100,"HeightMm":60,"Dpmm":8,"Elements":[]}}
            """;

        LabelDocument restored = LabelDocumentJson.Deserialize(legacy);

        Assert.False(restored.IsContinuous);
        Assert.Equal(60, restored.HeightMm, 3);
    }

    /// <summary>The load-bearing one: ZPL never states the gap, so it is recovered from
    /// the difference between the length the file asked for and the content that fills
    /// it. Without that the margin would reset to the default and the label would come
    /// back a different length than it left.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7.5)]
    public void RoundTrip_ThroughZpl_KeepsTheLength(double marginMm)
    {
        LabelDocument document = Roll(Box(20, 40), Box(100, 160));
        document.ContinuousMarginMm = marginMm;
        string first = new ZplGenerator().Generate(document);

        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(first, dpmm: 8);

        Assert.True(imported.Document.IsContinuous);
        Assert.Equal(marginMm, imported.Document.ContinuousMarginMm, 3);
        Assert.Equal(document.HeightMm, imported.Document.HeightMm, 3);
        Assert.Empty(imported.Warnings);
        Assert.Equal(first, new ZplGenerator().Generate(imported.Document));
    }

    /// <summary>Gap and mark sensing stay printer setup, reported as deliberately left
    /// behind rather than turned into a document flag.</summary>
    [Fact]
    public void OtherTrackingModes_AreStillThePrintersOwn()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^PW800^LL400^MNY^FO10,10^A0N,30^FDgap sensed^FS^XZ", dpmm: 8);

        Assert.False(result.Document.IsContinuous);
        Assert.Contains(result.Warnings, w => w.Contains("^MN", StringComparison.Ordinal));
    }
}
