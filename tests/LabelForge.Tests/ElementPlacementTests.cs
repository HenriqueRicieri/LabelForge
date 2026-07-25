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
}
