using LabelForge.Core.Editing;

namespace LabelForge.Tests;

/// <summary>
/// One-axis snapping used by the canvas drag: a box [lo, hi] snaps its low edge,
/// center, or high edge to the nearest target within the threshold.
/// </summary>
public sealed class GuideSnapperTests
{
    [Fact]
    public void LowEdge_SnapsToNearestTarget()
    {
        (int shift, int? target) = GuideSnapper.Snap(103, 203, [100], threshold: 6);
        Assert.Equal(-3, shift);
        Assert.Equal(100, target);
    }

    [Fact]
    public void HighEdge_SnapsToNearestTarget()
    {
        (int shift, int? target) = GuideSnapper.Snap(96, 196, [200], threshold: 6);
        Assert.Equal(4, shift);
        Assert.Equal(200, target);
    }

    [Fact]
    public void Center_SnapsToNearestTarget()
    {
        // Box center is 150; target 152 is within range of the center only.
        (int shift, int? target) = GuideSnapper.Snap(100, 200, [152], threshold: 6);
        Assert.Equal(2, shift);
        Assert.Equal(152, target);
    }

    [Fact]
    public void NothingWithinThreshold_ReturnsZeroAndNoTarget()
    {
        (int shift, int? target) = GuideSnapper.Snap(100, 200, [50, 260], threshold: 6);
        Assert.Equal(0, shift);
        Assert.Null(target);
    }

    [Fact]
    public void ClosestOfSeveralTargetsWins()
    {
        (int shift, int? target) = GuideSnapper.Snap(103, 203, [100, 205], threshold: 6);
        Assert.Equal(2, shift);
        Assert.Equal(205, target);
    }

    [Fact]
    public void ExactAlignment_KeepsTargetForHighlight()
    {
        (int shift, int? target) = GuideSnapper.Snap(100, 200, [100], threshold: 6);
        Assert.Equal(0, shift);
        Assert.Equal(100, target);
    }

    [Fact]
    public void EmptyTargets_NeverSnap()
    {
        (int shift, int? target) = GuideSnapper.Snap(100, 200, [], threshold: 6);
        Assert.Equal(0, shift);
        Assert.Null(target);
    }
}
