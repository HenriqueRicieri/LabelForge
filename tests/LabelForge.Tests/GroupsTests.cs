using LabelForge.Core.Editing;
using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// Groups are a designer convenience and nothing else: flat, one level, invisible to the
/// printer. The tests that matter here are the three that stop it leaking, that a grouped
/// document generates the same ZPL as an ungrouped one, that the id survives a save, and
/// that an ungrouped document does not gain a key it never had.
/// </summary>
public sealed class GroupsTests
{
    private static BoxElement Box(int x, int y, int z, bool locked = false) =>
        new() { X = x, Y = y, WidthDots = 100, HeightDots = 50, ZOrder = z, IsLocked = locked };

    private static LabelDocument Document(params Element[] elements)
    {
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        foreach (Element element in elements)
        {
            document.Elements.Add(element);
        }

        return document;
    }

    [Fact]
    public void AnUngroupedDocument_WritesNoGroupIdAtAll()
    {
        LabelDocument document = Document(Box(10, 10, 0), Box(200, 10, 1));

        string json = LabelDocumentJson.Serialize(document);

        // Every .lfl already saved would otherwise gain a null entry per element the next
        // time it was opened and saved, and every undo snapshot would carry it.
        Assert.DoesNotContain("GroupId", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AGrouping_SurvivesTheRoundTrip()
    {
        var first = Box(10, 10, 0);
        var second = Box(200, 10, 1);
        LabelDocument document = Document(first, second);
        Assert.True(Groups.Group(document, [first, second]));

        LabelDocument restored = LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(document));

        Assert.All(restored.Elements, e => Assert.NotNull(e.GroupId));
        Assert.Single(restored.Elements.Select(e => e.GroupId).Distinct());
        Assert.Equal(first.GroupId, restored.Elements[0].GroupId);
    }

    [Fact]
    public void Grouping_ChangesNotOneByteOfTheZpl()
    {
        var first = Box(10, 10, 0);
        var second = Box(200, 10, 1);
        LabelDocument document = Document(first, second);

        string before = new ZplGenerator().Generate(document);
        Assert.True(Groups.Group(document, [first, second]));
        string after = new ZplGenerator().Generate(document);

        Assert.Equal(before, after);
    }

    [Fact]
    public void Members_IsTheWholeGroup_OrJustTheElement()
    {
        var first = Box(10, 10, 0);
        var second = Box(200, 10, 1);
        var loose = Box(400, 10, 2);
        LabelDocument document = Document(first, second, loose);
        Groups.Group(document, [first, second]);

        Assert.Equal([first, second], Groups.Members(document, first));
        Assert.Equal([loose], Groups.Members(document, loose));
    }

    [Fact]
    public void Expand_WidensToWholeGroups_WithoutRepeating()
    {
        var first = Box(10, 10, 0);
        var second = Box(200, 10, 1);
        var loose = Box(400, 10, 2);
        LabelDocument document = Document(first, second, loose);
        Groups.Group(document, [first, second]);

        Assert.Equal([first, second], Groups.Expand(document, [second]));
        Assert.Equal([first, second, loose], Groups.Expand(document, [first, second, loose]));
    }

    [Fact]
    public void Grouping_NeedsTwoThings()
    {
        var only = Box(10, 10, 0);
        LabelDocument document = Document(only);

        Assert.False(Groups.Group(document, [only]));
        Assert.Null(only.GroupId);
    }

    [Fact]
    public void RegroupingTheSameGroup_DoesNothing()
    {
        var first = Box(10, 10, 0);
        var second = Box(200, 10, 1);
        LabelDocument document = Document(first, second);
        Groups.Group(document, [first, second]);
        Guid? id = first.GroupId;

        Assert.False(Groups.Group(document, [first, second]));
        Assert.Equal(id, first.GroupId);
    }

    [Fact]
    public void CanGroup_SaysWhenGroupingWouldDoNothing()
    {
        var first = Box(10, 10, 0);
        var second = Box(200, 10, 1);
        var loose = Box(400, 10, 2);
        LabelDocument document = Document(first, second, loose);

        Assert.False(Groups.CanGroup(document, [first]));
        Assert.True(Groups.CanGroup(document, [first, second]));

        Groups.Group(document, [first, second]);

        // One whole group is more than one element and still nothing to do, which is what
        // stops the menu offering a key that presses to no effect.
        Assert.False(Groups.CanGroup(document, [first, second]));
        Assert.True(Groups.CanGroup(document, [first, loose]));
    }

    [Fact]
    public void GroupingAbsorbsAnExistingGroup()
    {
        var first = Box(10, 10, 0);
        var second = Box(200, 10, 1);
        var third = Box(400, 10, 2);
        LabelDocument document = Document(first, second, third);
        Groups.Group(document, [first, second]);

        // Flat model: joining a group means everything lands in one new group, not a
        // group inside a group.
        Assert.True(Groups.Group(document, [first, third]));
        Assert.Single(document.Elements.Select(e => e.GroupId).Distinct());
    }

    [Fact]
    public void Grouping_StacksTheMembersTogether_AndKeepsTheZOrderSet()
    {
        var bottom = Box(10, 10, 0);
        var between = Box(200, 10, 1);
        var top = Box(400, 10, 2);
        LabelDocument document = Document(bottom, between, top);

        Assert.True(Groups.Group(document, [bottom, top]));

        // The values in the document are the same three; only who holds which changed, and
        // the members ended up together at the level of whichever was already in front.
        Assert.Equal([0, 1, 2], document.Elements.Select(e => e.ZOrder).OrderBy(z => z));
        Assert.Equal(0, between.ZOrder);
        Assert.True(bottom.ZOrder > between.ZOrder);
        Assert.True(top.ZOrder > between.ZOrder);
    }

    [Fact]
    public void Ungrouping_ClearsTheId_AndLeavesTheStackAlone()
    {
        var first = Box(10, 10, 0);
        var second = Box(200, 10, 1);
        LabelDocument document = Document(first, second);
        Groups.Group(document, [first, second]);
        int[] stack = [.. document.Elements.Select(e => e.ZOrder)];

        Assert.True(Groups.Ungroup([first, second]));
        Assert.All(document.Elements, e => Assert.Null(e.GroupId));
        Assert.Equal(stack, document.Elements.Select(e => e.ZOrder));
        Assert.False(Groups.Ungroup([first, second]));
    }

    [Fact]
    public void Copies_GetTheirOwnGroup()
    {
        var first = Box(10, 10, 0);
        var second = Box(200, 10, 1);
        LabelDocument document = Document(first, second);
        Groups.Group(document, [first, second]);

        List<Element> copies = LabelDocumentJson.DeserializeElements(
            LabelDocumentJson.SerializeElements([first, second]));
        Groups.Remap(copies);

        Assert.NotNull(copies[0].GroupId);
        Assert.Equal(copies[0].GroupId, copies[1].GroupId);
        Assert.NotEqual(first.GroupId, copies[0].GroupId);
    }

    [Fact]
    public void ACopyOfOneMember_IsNotAGroupOfOne()
    {
        var first = Box(10, 10, 0);
        var second = Box(200, 10, 1);
        LabelDocument document = Document(first, second);
        Groups.Group(document, [first, second]);

        List<Element> copies = LabelDocumentJson.DeserializeElements(
            LabelDocumentJson.SerializeElements([first]));
        Groups.Remap(copies);

        Assert.Null(copies[0].GroupId);
    }

    [Fact]
    public void OneLockedMember_HoldsTheWholeGroup()
    {
        var free = Box(10, 10, 0);
        var pinned = Box(200, 10, 1, locked: true);
        var loose = Box(400, 10, 2);
        LabelDocument document = Document(free, pinned, loose);
        Groups.Group(document, [free, pinned]);

        // A group moves as one thing, so half of it moving is not an option.
        Assert.True(Groups.IsHeld(document, free));
        Assert.True(Groups.IsHeld(document, pinned));
        Assert.False(Groups.IsHeld(document, loose));
    }
}
