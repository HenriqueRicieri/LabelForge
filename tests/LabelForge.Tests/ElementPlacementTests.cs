using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// The placement rule shared by the generator (skip), the designer warnings, and the
/// canvas outlines. Label under test is 800 x 1200 dots.
/// </summary>
public sealed class ElementPlacementTests
{
    private static readonly ElementBoundsCalculator Bounds = new();

    /// <summary>800 x 1200 dots at 8 dpmm.</summary>
    private static LabelDocument Label() => new() { WidthMm = 100, HeightMm = 150, Dpmm = 8 };

    private static PlacementStatus Classify(Element element) =>
        ElementPlacement.Classify(element, Bounds.GetBounds(element), Label());

    [Fact]
    public void FullyInside_IsInside() =>
        Assert.Equal(PlacementStatus.Inside, Classify(
            new BoxElement { X = 10, Y = 10, WidthDots = 100, HeightDots = 100 }));

    [Fact]
    public void FootprintPastRightEdge_IsClipped() =>
        Assert.Equal(PlacementStatus.Clipped, Classify(
            new BoxElement { X = 750, Y = 10, WidthDots = 100, HeightDots = 100 }));

    [Fact]
    public void FootprintPastBottomEdge_IsClipped() =>
        Assert.Equal(PlacementStatus.Clipped, Classify(
            new BoxElement { X = 10, Y = 1150, WidthDots = 100, HeightDots = 100 }));

    [Fact]
    public void NegativeOrigin_IsNotPrintable() =>
        Assert.Equal(PlacementStatus.NotPrintable, Classify(
            new BoxElement { X = -1, Y = 10, WidthDots = 100, HeightDots = 100 }));

    [Fact]
    public void OriginPastTheEdge_IsNotPrintable()
    {
        Assert.Equal(PlacementStatus.NotPrintable, Classify(
            new BoxElement { X = 800, Y = 10, WidthDots = 100, HeightDots = 100 }));
        Assert.Equal(PlacementStatus.NotPrintable, Classify(
            new BoxElement { X = 10, Y = 1200, WidthDots = 100, HeightDots = 100 }));
    }

    /// <summary>The pasteboard is half the label on each side, per axis.</summary>
    [Fact]
    public void Pasteboard_IsHalfTheLabelPerAxis() =>
        Assert.Equal((400, 600), ElementPlacement.PasteboardMarginDots(Label()));

    /// <summary>A small label still gets 2 cm, on each axis separately: 10 x 6 cm is
    /// 5 cm across and the 2 cm minimum down.</summary>
    [Fact]
    public void Pasteboard_NeverDropsBelowTwoCentimeters()
    {
        Assert.Equal((400, 240), ElementPlacement.PasteboardMarginDots(
            new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 }));
        Assert.Equal((160, 160), ElementPlacement.PasteboardMarginDots(
            new LabelDocument { WidthMm = 20, HeightMm = 10, Dpmm = 8 }));
    }

    [Fact]
    public void PreviewMargin_IsZeroWhenEverythingIsOnTheLabel() =>
        Assert.Equal(0, ElementPlacement.PreviewMarginDots(Label(),
            [new DotRect(0, 0, 800, 1200), new DotRect(10, 10, 5, 5)]));

    /// <summary>The preview reaches as far as the furthest footprint, rounded up to
    /// whole 5 mm steps (40 dots at 8 dpmm), whichever edge it crosses.</summary>
    [Fact]
    public void PreviewMargin_FollowsTheFurthestReachInSteps()
    {
        Assert.Equal(40, ElementPlacement.PreviewMarginDots(Label(), [new DotRect(790, 10, 20, 20)]));
        Assert.Equal(80, ElementPlacement.PreviewMarginDots(Label(),
            [new DotRect(-41, 10, 20, 20), new DotRect(10, 1190, 20, 20)]));
        Assert.Equal(120, ElementPlacement.PreviewMarginDots(Label(), [new DotRect(10, -100, 20, 20)]));
    }

    /// <summary>Nothing reaches further than the pasteboard, so neither does the
    /// preview: the larger axis bounds it.</summary>
    [Fact]
    public void PreviewMargin_StopsAtThePasteboard() =>
        Assert.Equal(600, ElementPlacement.PreviewMarginDots(Label(), [new DotRect(10, 2000, 20, 20)]));
}
