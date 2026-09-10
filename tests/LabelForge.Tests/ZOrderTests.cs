using LabelForge.Core.Editing;
using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class ZOrderTests
{
    private static LabelDocument Label(params int[] values)
    {
        var label = new LabelDocument();
        foreach (int value in values) label.Elements.Add(new BoxElement { ZOrder = value });
        return label;
    }

    [Theory]
    [InlineData(true, 90, -5, 10)]
    [InlineData(false, 10, -5, 90)]
    public void DragPermutesValues(bool inFront, int first, int second, int third)
    {
        var label = Label(-5, 10, 90);
        Assert.True(ZOrder.Move(label, label.Elements[0], label.Elements[2], inFront));
        Assert.Equal(new[] { first, second, third }, label.Elements.Select(e => e.ZOrder));
    }

    [Fact]
    public void DragBackwardPreservesTheDocumentCollection()
    {
        var label = Label(10, 20, 30);
        var elements = label.Elements.ToArray();
        Assert.True(ZOrder.Move(label, elements[2], elements[0], false));
        Assert.Equal(elements, label.Elements);
        Assert.Equal(new[] { 20, 30, 10 }, elements.Select(e => e.ZOrder));
    }

    [Fact]
    public void CanMoveDoesNotEditTheDocument()
    {
        var label = Label(1, 4, 20);
        string before = LabelDocumentJson.Serialize(label);
        Assert.True(ZOrder.CanMove(label, label.Elements[0], label.Elements[2], true));
        Assert.Equal(before, LabelDocumentJson.Serialize(label));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGroupMovesTogether(bool inFront)
    {
        var label = Label(1, 4, 20, 40);
        label.Elements[1].GroupId = label.Elements[2].GroupId = Guid.NewGuid();
        Assert.True(ZOrder.Move(label, label.Elements[1], label.Elements[inFront ? 3 : 0], inFront));
        var order = label.Elements.OrderBy(e => e.ZOrder).ToList();
        Assert.Equal(order.IndexOf(label.Elements[1]) + 1, order.IndexOf(label.Elements[2]));
        Assert.Equal(new[] { 1, 4, 20, 40 }, order.Select(e => e.ZOrder));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADropCannotSplitTheTargetGroup(bool inFront)
    {
        var label = Label(1, 4, 20, 40);
        label.Elements[1].GroupId = label.Elements[2].GroupId = Guid.NewGuid();
        var source = label.Elements[inFront ? 0 : 3];
        Assert.True(ZOrder.Move(label, source, label.Elements[1], inFront));
        var order = label.Elements.OrderBy(e => e.ZOrder).ToList();
        Assert.Equal(order.IndexOf(label.Elements[1]) + 1, order.IndexOf(label.Elements[2]));
        Assert.Equal(inFront ? 2 : 1, order.IndexOf(source));
    }

    [Fact]
    public void DraggingWithinAGroupDoesNothing()
    {
        var label = Label(1, 4);
        label.Elements[0].GroupId = label.Elements[1].GroupId = Guid.NewGuid();
        Assert.False(ZOrder.Move(label, label.Elements[0], label.Elements[1], true));
    }

    [Fact]
    public void ALockedMemberHoldsTheWholeDraggedGroup()
    {
        var label = Label(1, 4, 20);
        label.Elements[0].GroupId = label.Elements[1].GroupId = Guid.NewGuid();
        label.Elements[1].IsLocked = true;
        Assert.False(ZOrder.Move(label, label.Elements[0], label.Elements[2], true));
        Assert.Equal(new[] { 1, 4, 20 }, label.Elements.Select(e => e.ZOrder));
    }

    [Fact]
    public void HiddenElementsCanBeReordered()
    {
        var label = Label(1, 4);
        label.Elements[0].IsVisible = false;
        Assert.True(ZOrder.Move(label, label.Elements[0], label.Elements[1], true));
        Assert.False(label.Elements[0].IsVisible);
    }

    [Fact]
    public void TiesAreLeftUnchanged()
    {
        var label = Label(1, 1, 20);
        string before = LabelDocumentJson.Serialize(label);
        Assert.False(ZOrder.Move(label, label.Elements[0], label.Elements[1], true));
        Assert.False(ZOrder.Step(label, [label.Elements[0]], true));
        Assert.Equal(before, LabelDocumentJson.Serialize(label));
    }

    [Fact]
    public void DroppingAtTheExistingPositionDoesNothing()
    {
        var label = Label(1, 4, 20);
        Assert.False(ZOrder.Move(label, label.Elements[0], label.Elements[1], false));
        Assert.False(ZOrder.Move(label, label.Elements[0], label.Elements[0], true));
        Assert.False(ZOrder.Move(label, new BoxElement(), label.Elements[0], true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StepPassesAWholeNeighbourGroup(bool forward)
    {
        var label = Label(1, 4, 20, 40);
        label.Elements[1].GroupId = label.Elements[2].GroupId = Guid.NewGuid();
        var source = label.Elements[forward ? 0 : 3];
        Assert.True(ZOrder.Step(label, [source], forward));
        var order = label.Elements.OrderBy(e => e.ZOrder).ToList();
        Assert.Equal(order.IndexOf(label.Elements[1]) + 1, order.IndexOf(label.Elements[2]));
        Assert.Equal(forward ? 2 : 1, order.IndexOf(source));
    }

    [Fact]
    public void ASelectedBlockStepsWithoutReversingItsMembers()
    {
        var label = Label(1, 4, 20, 40);
        Assert.True(ZOrder.Step(label, [label.Elements[0], label.Elements[1]], true));
        Assert.Equal(new[] { 4, 20, 1, 40 }, label.Elements.Select(e => e.ZOrder));
        Assert.False(ZOrder.Step(label, [label.Elements[3]], true));
    }

    [Fact]
    public void AnEmptyStepDoesNotRestackAnImportedGroup()
    {
        var label = Label(1, 4, 20);
        label.Elements[0].GroupId = label.Elements[2].GroupId = Guid.NewGuid();
        string before = LabelDocumentJson.Serialize(label);
        Assert.False(ZOrder.Step(label, [], true));
        Assert.Equal(before, LabelDocumentJson.Serialize(label));
    }

    [Fact]
    public void ExtremeCommandsKeepTheirExistingValues()
    {
        var label = Label(1, 4, 20);
        Assert.True(ZOrder.BringToFront(label, [label.Elements[0]]));
        Assert.Equal(21, label.Elements[0].ZOrder);
        Assert.True(ZOrder.SendToBack(label, [label.Elements[2]]));
        Assert.Equal(3, label.Elements[2].ZOrder);
        Assert.False(ZOrder.BringToFront(label, []));
    }
}
