using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// Alignment and distribution semantics: one element aligns against the label
/// (800 x 1200 dots here), several align within their own bounding box, locked
/// elements never move, and distribution equalizes the gaps keeping the ends fixed.
/// Boxes are used because their footprint is exact (no heuristics).
/// </summary>
public sealed class AlignerTests
{
    private static BoxElement Box(int x, int y, int w, int h, bool locked = false) =>
        new() { X = x, Y = y, WidthDots = w, HeightDots = h, IsLocked = locked };

    [Fact]
    public void SingleElement_CentersOnTheLabel()
    {
        var box = Box(10, 10, 200, 100);

        Assert.True(Aligner.Align([box], AlignEdge.CenterHorizontal, 800, 1200));
        Assert.True(Aligner.Align([box], AlignEdge.Middle, 800, 1200));

        Assert.Equal(300, box.X);
        Assert.Equal(550, box.Y);
    }

    [Fact]
    public void SingleElement_AlignsToLabelEdges()
    {
        var box = Box(100, 100, 200, 100);

        Assert.True(Aligner.Align([box], AlignEdge.Right, 800, 1200));
        Assert.Equal(600, box.X);

        Assert.True(Aligner.Align([box], AlignEdge.Bottom, 800, 1200));
        Assert.Equal(1100, box.Y);

        Assert.True(Aligner.Align([box], AlignEdge.Left, 800, 1200));
        Assert.Equal(0, box.X);

        Assert.True(Aligner.Align([box], AlignEdge.Top, 800, 1200));
        Assert.Equal(0, box.Y);
    }

    [Fact]
    public void MultipleElements_AlignWithinTheirOwnBoundingBox()
    {
        var a = Box(100, 50, 100, 40);
        var b = Box(300, 200, 200, 40);

        Assert.True(Aligner.Align([a, b], AlignEdge.Left, 800, 1200));
        Assert.Equal(100, a.X);
        Assert.Equal(100, b.X);

        Assert.True(Aligner.Align([a, b], AlignEdge.Right, 800, 1200));
        Assert.Equal(200, a.X);
        Assert.Equal(100, b.X);
    }

    [Fact]
    public void MultipleElements_AlignCenters()
    {
        var a = Box(0, 0, 100, 40);
        var b = Box(200, 100, 300, 40);

        // Union spans X 0..500; both centers move to 250.
        Assert.True(Aligner.Align([a, b], AlignEdge.CenterHorizontal, 800, 1200));
        Assert.Equal(200, a.X);
        Assert.Equal(100, b.X);
    }

    [Fact]
    public void LockedElements_NeverMove()
    {
        var locked = Box(100, 100, 100, 40, locked: true);
        var free = Box(300, 100, 100, 40);

        // Only the free element counts, so it aligns against the label.
        Assert.True(Aligner.Align([locked, free], AlignEdge.Left, 800, 1200));
        Assert.Equal(100, locked.X);
        Assert.Equal(0, free.X);
    }

    [Fact]
    public void Align_ClampsOriginAtZero()
    {
        // The QR footprint sits 10 dots below its origin; flush-to-top would need
        // Y=-10, which ZPL cannot print, so the origin clamps at 0.
        var qr = new QrCodeElement { X = 100, Y = 100, Data = "A", Magnification = 2 };

        Assert.True(Aligner.Align([qr], AlignEdge.Top, 800, 1200));
        Assert.Equal(0, qr.Y);
    }

    [Fact]
    public void Distribute_EqualizesGaps_KeepingEndsFixed()
    {
        var a = Box(0, 0, 100, 40);
        var b = Box(120, 0, 100, 40);
        var c = Box(500, 0, 100, 40);

        // Span 0..600, total width 300, so two gaps of 150 each.
        Assert.True(Aligner.Distribute([a, b, c], horizontal: true));
        Assert.Equal(0, a.X);
        Assert.Equal(250, b.X);
        Assert.Equal(500, c.X);
    }

    [Fact]
    public void Distribute_Vertically()
    {
        var a = Box(0, 0, 40, 100);
        var b = Box(0, 900, 40, 100);
        var c = Box(0, 150, 40, 100);

        Assert.True(Aligner.Distribute([a, b, c], horizontal: false));
        Assert.Equal(0, a.Y);
        Assert.Equal(450, c.Y);
        Assert.Equal(900, b.Y);
    }

    [Fact]
    public void Distribute_NeedsAtLeastThree()
    {
        var a = Box(0, 0, 100, 40);
        var b = Box(500, 0, 100, 40);

        Assert.False(Aligner.Distribute([a, b], horizontal: true));
        Assert.Equal(0, a.X);
        Assert.Equal(500, b.X);
    }

    [Fact]
    public void AlreadyAligned_ReportsNoChange()
    {
        var a = Box(100, 50, 100, 40);
        var b = Box(100, 200, 200, 40);

        Assert.False(Aligner.Align([a, b], AlignEdge.Left, 800, 1200));
    }
}
