using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class SpacingGuidesTests
{
    [Fact]
    public void MeasuresTheNearestGapOnEachSideInTheSharedLane()
    {
        IReadOnlyList<SpacingGap> gaps = SpacingGuides.Measure(new(100, 100, 40, 40),
            [new(20, 110, 40, 20), new(190, 100, 20, 60), new(110, 20, 20, 50), new(100, 180, 60, 20)]);
        Assert.Equal(new SpacingGap(true, 60, 100, 120), gaps[0]);
        Assert.Equal(new SpacingGap(true, 140, 190, 120), gaps[1]);
        Assert.Equal(new SpacingGap(false, 70, 100, 120), gaps[2]);
        Assert.Equal(new SpacingGap(false, 140, 180, 120), gaps[3]);
    }

    [Theory]
    [InlineData(true, 246, 60, 4)]
    [InlineData(false, 246, 60, 4)]
    [InlineData(true, 254, 60, -4)]
    [InlineData(false, 254, 60, -4)]
    [InlineData(true, 250, 60, 0)]
    [InlineData(true, 500, 60, null)]
    [InlineData(true, 241, 60, null)]
    [InlineData(true, 246, 61, null)]
    public void EqualGapsSnapOnEitherAxisWithinTheThreshold(bool horizontal, int position, int size, int? expected)
    {
        DotRect moving = Axis(position, size, horizontal);
        Assert.Equal(expected, SpacingGuides.Snap(moving, [Axis(100, 60, horizontal), Axis(400, 60, horizontal)], horizontal, 8));
    }

    [Theory]
    [InlineData(true, 696, 4)]
    [InlineData(false, 696, 4)]
    [InlineData(true, -204, 4)]
    [InlineData(false, -204, 4)]
    public void RepeatsTheGapOnEitherEnd(bool horizontal, int position, int expected)
    {
        Assert.Equal(expected, SpacingGuides.Snap(Axis(position, 60, horizontal),
            [Axis(100, 60, horizontal), Axis(400, 60, horizontal)], horizontal, 8));
    }

    [Fact]
    public void RepeatedSpacingIncludesTheReferenceGap()
    {
        var gaps = SpacingGuides.Measure(new(700, 180, 60, 60), [new(100, 180, 60, 60), new(400, 180, 60, 60)]);
        Assert.Equal(2, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(240, g.Dots));
        Assert.Contains(new SpacingGap(true, 160, 400, 210), gaps);
        Assert.Contains(new SpacingGap(true, 460, 700, 210), gaps);
    }

    [Fact]
    public void DifferentRowsAndOverlappingFieldsDoNotBecomeSideNeighbours()
    {
        var moving = new DotRect(100, 100, 40, 40);
        Assert.Empty(SpacingGuides.Measure(moving, [new(50, 50, 30, 30), new(110, 110, 20, 20)]));
        Assert.Null(SpacingGuides.Snap(moving, [new(0, 0, 40, 40), new(200, 200, 40, 40)], true, 10));
    }

    [Fact]
    public void AllThreeFieldsMustShareTheSameLane()
    {
        var moving = new DotRect(246, 100, 60, 100);
        Assert.Null(SpacingGuides.Snap(moving, [new(100, 100, 60, 40), new(400, 160, 60, 40)], true, 8));
        Assert.Null(SpacingGuides.Snap(new(696, 100, 60, 40),
            [new(100, 160, 60, 40), new(400, 100, 60, 100)], true, 8));
    }

    [Fact]
    public void ContactIsZeroButCornerContactHasNoSharedLane()
    {
        Assert.Equal(0, Assert.Single(SpacingGuides.Measure(new(100, 100, 40, 40), [new(60, 110, 40, 20)])).Dots);
        Assert.Empty(SpacingGuides.Measure(new(100, 100, 40, 40), [new(60, 60, 40, 40)]));
    }

    [Fact]
    public void ACloserNeighbourBlocksAHiddenEqualSpacingTarget()
    {
        Assert.Null(SpacingGuides.Snap(new(246, 180, 60, 60),
            [new(100, 180, 60, 60), new(200, 180, 30, 60), new(400, 180, 60, 60)], true, 8));
    }

    private static DotRect Axis(int position, int size, bool horizontal) => horizontal
        ? new(position, 180, size, 60) : new(180, position, 60, size);
}
