using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// Making things the same size. Sizes are DRAWN sizes, so what "the same width" means is
/// the ink; and anything whose size comes in steps lands on the nearest one and says so,
/// rather than the panel claiming a number the canvas is not drawing.
/// </summary>
public sealed class SizeMatcherTests
{
    private static BoxElement Box(int w, int h, bool locked = false) =>
        new() { X = 10, Y = 10, WidthDots = w, HeightDots = h, IsLocked = locked };

    [Fact]
    public void MatchesWidthAndLeavesHeightAlone()
    {
        var reference = Box(300, 90);
        var other = Box(120, 40);

        SizeMatch result = SizeMatcher.Match([reference, other], reference, width: true, height: false);

        Assert.Equal(1, result.Resized);
        Assert.Equal(0, result.Quantized);
        Assert.Equal(300, other.WidthDots);
        Assert.Equal(40, other.HeightDots);
    }

    /// <summary>A barcode turned a quarter runs its bars down the label, so its drawn
    /// height is what the module width sets and its drawn width is the bar height. Matching
    /// the drawn height has to change the module and leave the bars as tall as they were.</summary>
    [Theory]
    [InlineData(Orientation.Rotated90)]
    [InlineData(Orientation.Rotated270)]
    public void MatchesARotatedFieldOnItsDrawnAxes(Orientation orientation)
    {
        var bar = new BarcodeElement
        {
            X = 100, Y = 100, Data = "ABC12345", HeightDots = 80, ModuleWidthDots = 2,
            PrintInterpretationLine = false, Orientation = orientation,
        };
        var bounds = new ElementBoundsCalculator();
        DotRect before = bounds.GetBounds(bar);
        var reference = Box(50, before.Height * 2);

        SizeMatcher.Match([reference, bar], reference, width: false, height: true);

        DotRect after = bounds.GetBounds(bar);
        Assert.Equal(80, bar.HeightDots);
        Assert.Equal(4, bar.ModuleWidthDots);
        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.Height * 2, after.Height);
    }

    [Fact]
    public void MatchesBothSides()
    {
        var reference = Box(300, 90);
        var other = Box(120, 40);

        SizeMatch result = SizeMatcher.Match([reference, other], reference, width: true, height: true);

        Assert.Equal(1, result.Resized);
        Assert.Equal(300, other.WidthDots);
        Assert.Equal(90, other.HeightDots);
    }

    [Fact]
    public void TheReferenceAndAnythingLockedStayAsTheyAre()
    {
        var reference = Box(300, 90);
        var pinned = Box(120, 40, locked: true);

        SizeMatch result = SizeMatcher.Match([reference, pinned], reference, width: true, height: true);

        Assert.Equal(0, result.Resized);
        Assert.Equal(300, reference.WidthDots);
        Assert.Equal(120, pinned.WidthDots);
    }

    [Fact]
    public void AskingForNeitherAxisDoesNothing()
    {
        var reference = Box(300, 90);
        var other = Box(120, 40);

        Assert.Equal(default, SizeMatcher.Match([reference, other], reference, false, false));
        Assert.Equal(120, other.WidthDots);
    }

    [Fact]
    public void ABarcodeLandsOnAModuleStepAndSaysSo()
    {
        var reference = Box(300, 90);
        var code = new BarcodeElement
        {
            X = 10, Y = 200, Data = "12345678", ModuleWidthDots = 2, HeightDots = 60,
        };

        SizeMatch result = SizeMatcher.Match([reference, code], reference, width: true, height: true);

        // A barcode's width is a whole number of modules, so it cannot be exactly 300 dots
        // wide whatever module it lands on. It did resize, and the caller is told the size
        // it reached is not the size that was asked for.
        Assert.Equal(1, result.Resized);
        Assert.Equal(1, result.Quantized);
        Assert.NotEqual(300, new ElementBoundsCalculator().GetBounds(code).Width);
    }

    [Fact]
    public void SomethingAlreadyTheRightSizeIsNotCounted()
    {
        var reference = Box(300, 90);
        var same = Box(300, 90);

        SizeMatch result = SizeMatcher.Match([reference, same], reference, width: true, height: true);

        Assert.Equal(0, result.Resized);
        Assert.Equal(0, result.Quantized);
    }
}
