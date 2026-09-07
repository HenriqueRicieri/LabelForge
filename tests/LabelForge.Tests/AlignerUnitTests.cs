using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// Alignment over UNITS rather than elements, which is what makes a group line up by the
/// box around all of it and arrive with its internal layout intact. The element-taking
/// methods are these with every element its own unit, and AlignerTests covers that shape.
/// </summary>
public sealed class AlignerUnitTests
{
    private static BoxElement Box(int x, int y, int w = 100, int h = 50, bool locked = false) =>
        new() { X = x, Y = y, WidthDots = w, HeightDots = h, IsLocked = locked };

    [Fact]
    public void AUnit_LinesUpByItsOwnBox_AndKeepsItsShape()
    {
        var left = Box(300, 100);
        var right = Box(500, 100);
        var other = Box(50, 400);

        Assert.True(Aligner.AlignUnits([[left, right], [other]], AlignEdge.Left, 800, 1200));

        // The unit moved as one: its left edge is where the other element's is, and the
        // gap inside it is untouched.
        Assert.Equal(50, left.X);
        Assert.Equal(250, right.X);
        Assert.Equal(50, other.X);
    }

    [Fact]
    public void AUnitAlone_CentersOnTheLabelByItsWholeBox()
    {
        var left = Box(0, 100);
        var right = Box(200, 100);

        // The unit spans 0 to 300, so centering it on an 800 dot label puts it at 250.
        Assert.True(Aligner.AlignUnits([[left, right]], AlignEdge.CenterHorizontal, 800, 1200));

        Assert.Equal(250, left.X);
        Assert.Equal(450, right.X);
    }

    [Fact]
    public void AUnitWithALockedMember_SitsTheMoveOut()
    {
        var free = Box(300, 100);
        var pinned = Box(500, 100, locked: true);
        var other = Box(50, 400);

        // The locked unit is out of the move entirely, which leaves one unit, and one unit
        // aligns against the label. So something moved; it just was not the locked group.
        Assert.True(Aligner.AlignUnits([[free, pinned], [other]], AlignEdge.Left, 800, 1200));
        Assert.Equal(300, free.X);
        Assert.Equal(500, pinned.X);
        Assert.Equal(0, other.X);

        // And with nothing else to move, there is nothing to do at all.
        Assert.False(Aligner.AlignUnits([[free, pinned]], AlignEdge.Left, 800, 1200));
        Assert.Equal(300, free.X);
    }

    [Fact]
    public void TheClampAtZero_AppliesToTheUnit_NotToEachMember()
    {
        var leading = Box(20, 100);
        var trailing = Box(400, 100);
        var target = Box(0, 400);

        Assert.True(Aligner.AlignUnits([[leading, trailing], [target]], AlignEdge.Left, 800, 1200));

        // Clamping each member on its own would have pinned the first at 0 and pulled the
        // unit apart. The whole unit moves by the same delta or not at all.
        Assert.Equal(0, leading.X);
        Assert.Equal(380, trailing.X);
    }

    [Fact]
    public void DistributionCountsAUnitOnce()
    {
        var a = Box(0, 100);
        var groupLeft = Box(300, 100);
        var groupRight = Box(360, 100, w: 40);
        var c = Box(700, 100);

        // Three units, not four elements: the pair in the middle is one thing to space.
        Assert.True(Aligner.DistributeUnits([[a], [groupLeft, groupRight], [c]], horizontal: true));

        Assert.Equal(0, a.X);
        Assert.Equal(700, c.X);
        Assert.Equal(60, groupRight.X - groupLeft.X);
    }

    [Fact]
    public void TwoUnitsAreNotEnoughToDistribute()
    {
        var a = Box(0, 100);
        var b = Box(300, 100);
        var c = Box(360, 100);

        // Three elements but only two units, and distribution needs a middle to move.
        Assert.False(Aligner.DistributeUnits([[a], [b, c]], horizontal: true));
    }
}
