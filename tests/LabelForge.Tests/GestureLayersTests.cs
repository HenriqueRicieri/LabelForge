using LabelForge.Core.Editing;
using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class GestureLayersTests
{
    private static LabelDocument Label(params Element[] elements) => new() { Elements = elements.ToList() };

    [Fact]
    public void PartitionRetainsStableStackingAndDoesNotEditTheDocument()
    {
        var first = new BoxElement { ZOrder = -10 };
        var moving = new BoxElement { ZOrder = 2, DoNotPrint = true };
        var tied = new BoxElement { ZOrder = 2 };
        var last = new BoxElement { ZOrder = 20 };
        var hidden = new BoxElement { ZOrder = 1, IsVisible = false };
        var document = Label(last, first, moving, hidden, tied);
        string before = LabelDocumentJson.Serialize(document);

        GestureLayerPlan plan = GestureLayers.Split(document, [moving, hidden]);

        Assert.True(plan.CanComposite);
        Assert.Equal([first], plan.Below);
        Assert.Equal([moving], plan.Moving);
        Assert.Equal([tied, last], plan.Above);
        Assert.Equal(before, LabelDocumentJson.Serialize(document));
    }

    [Fact]
    public void ALockedGroupStaysInTheStationaryLayer()
    {
        var group = Guid.NewGuid();
        var held = new BoxElement { ZOrder = 0, GroupId = group };
        var locked = new BoxElement { ZOrder = 1, GroupId = group, IsLocked = true };
        var movable = new BoxElement { ZOrder = 2 };
        GestureLayerPlan plan = GestureLayers.Split(Label(held, locked, movable), [held, movable]);

        Assert.True(plan.CanComposite);
        Assert.Equal([held, locked], plan.Below);
        Assert.Equal([movable], plan.Moving);
        Assert.Empty(plan.Above);
    }

    [Fact]
    public void AStationaryFieldInsideTheSelectionRequiresAFullRender()
    {
        var first = new BoxElement { ZOrder = 1 };
        var between = new BoxElement { ZOrder = 2 };
        var last = new BoxElement { ZOrder = 3 };
        GestureLayerPlan plan = GestureLayers.Split(Label(first, between, last), [first, last]);

        Assert.False(plan.CanComposite);
        Assert.Equal([first, last], plan.Moving);
        Assert.Equal([between], plan.Above);
    }

    [Fact]
    public void AHiddenFieldDoesNotBreakAContiguousSelection()
    {
        var first = new BoxElement { ZOrder = 1 };
        var hidden = new BoxElement { ZOrder = 2, IsVisible = false, IsReversed = true };
        var last = new BoxElement { ZOrder = 3 };
        GestureLayerPlan plan = GestureLayers.Split(Label(first, hidden, last), [first, last]);

        Assert.True(plan.CanComposite);
        Assert.Equal([first, last], plan.Moving);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void OnlyTheOpaqueBaseCanContainReverseFields(int reversedIndex, bool allowed)
    {
        Element[] elements = [new BoxElement(), new BoxElement { ZOrder = 1 }, new BoxElement { ZOrder = 2 }];
        elements[reversedIndex].IsReversed = true;

        Assert.Equal(allowed, GestureLayers.Split(Label(elements), [elements[1]]).CanComposite);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ContinuousOrReverseStockKeepsTheFullRender(bool continuous, bool reverse)
    {
        var moving = new BoxElement();
        var document = Label(moving);
        document.IsContinuous = continuous;
        document.Print.ReverseAll = reverse;

        Assert.False(GestureLayers.Split(document, [moving]).CanComposite);
    }

    [Theory]
    [InlineData('0', 0, true)]
    [InlineData('0', 100, false)]
    [InlineData('A', 0, false)]
    [InlineData('F', 0, false)]
    public void TextWithUnboundedPreviewMetricsKeepsTheFullRender(char font, int blockWidth, bool allowed)
    {
        var text = new TextElement { Font = font, BlockWidthDots = blockWidth };
        Assert.Equal(allowed, GestureLayers.Split(Label(text), [text]).CanComposite);
    }

    [Fact]
    public void NoMovableDocumentElementProducesNoGestureLayers()
    {
        var held = new BoxElement { IsLocked = true };
        var document = Label(held);
        GestureLayerPlan plan = GestureLayers.Split(document, [held, new BoxElement()]);

        Assert.False(plan.CanComposite);
        Assert.Empty(plan.Moving);
        Assert.Equal([held], plan.Below);
    }

    [Fact]
    public void AViewportFollowsTheSelectionOutsideTheLabel()
    {
        var first = new BoxElement { X = -700, Y = -300, WidthDots = 40, HeightDots = 80 };
        var second = new TextElement
        {
            X = -100, Y = -80, Text = "Baseline", FontHeightDots = 60,
            Anchor = FieldAnchor.Baseline, Orientation = Orientation.Rotated180,
        };
        DotRect viewport = GestureLayers.GetMovingViewport([first, second]);

        Assert.True(viewport.Contains(first.X, first.Y));
        Assert.True(viewport.Contains(second.X, second.Y));
        Assert.True(viewport.X < first.X);
        Assert.True(viewport.Y < first.Y);
        first.X += 80;
        first.Y -= 30;
        second.X += 80;
        second.Y -= 30;
        Assert.Equal(viewport with { X = viewport.X + 80, Y = viewport.Y - 30 },
            GestureLayers.GetMovingViewport([first, second]));
    }
}
