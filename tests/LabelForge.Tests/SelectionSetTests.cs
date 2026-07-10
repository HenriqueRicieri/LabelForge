using LabelForge.Core.Editing;
using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class SelectionSetTests
{
    private readonly TextElement _a = new() { Text = "a" };
    private readonly TextElement _b = new() { Text = "b" };

    [Fact]
    public void Set_ReplacesSelection_AndPrimaryIsLast()
    {
        var sut = new SelectionSet();
        sut.Set(_a);
        Assert.Equal(1, sut.Count);
        Assert.Same(_a, sut.Primary);

        sut.SetMany([_a, _b]);
        Assert.Equal(2, sut.Count);
        Assert.Same(_b, sut.Primary);

        sut.Set(null);
        Assert.Equal(0, sut.Count);
        Assert.Null(sut.Primary);
    }

    [Fact]
    public void Toggle_AddsAndRemoves()
    {
        var sut = new SelectionSet();
        sut.Toggle(_a);
        Assert.True(sut.Contains(_a));

        sut.Toggle(_b);
        Assert.Equal(2, sut.Count);

        sut.Toggle(_a);
        Assert.False(sut.Contains(_a));
        Assert.Same(_b, sut.Primary);
    }

    [Fact]
    public void Mutations_RaiseChanged()
    {
        var sut = new SelectionSet();
        int raised = 0;
        sut.Changed += (_, _) => raised++;

        sut.Set(_a);
        sut.Toggle(_b);
        sut.SetMany([_a]);
        sut.Clear();
        sut.Clear(); // empty clear is a no-op

        Assert.Equal(4, raised);
    }

    [Fact]
    public void ElementList_RoundTrips_ForGroupClipboard()
    {
        var json = LabelDocumentJson.SerializeElements([_a, new BoxElement { WidthDots = 50 }]);
        var restored = LabelDocumentJson.DeserializeElements(json);

        Assert.Equal(2, restored.Count);
        Assert.IsType<TextElement>(restored[0]);
        Assert.Equal(50, Assert.IsType<BoxElement>(restored[1]).WidthDots);
    }

    [Fact]
    public void DotRect_Intersects_DetectsOverlapAndSeparation()
    {
        var r = new DotRect(10, 10, 100, 50);
        Assert.True(r.Intersects(new DotRect(50, 30, 100, 100)));
        Assert.False(r.Intersects(new DotRect(110, 10, 10, 10)));
        Assert.False(r.Intersects(new DotRect(10, 60, 10, 10)));
    }
}
