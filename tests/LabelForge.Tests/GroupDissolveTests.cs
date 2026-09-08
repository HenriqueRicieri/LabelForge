using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class GroupDissolveTests
{
    [Fact]
    public void RemovingMembers_DissolvesOnlyTheSingleton()
    {
        var document = new LabelDocument();
        var removedGroup = Guid.NewGuid();
        var retainedGroup = Guid.NewGuid();
        var first = new BoxElement { GroupId = removedGroup };
        var second = new BoxElement { GroupId = removedGroup };
        var survivor = new BoxElement { GroupId = removedGroup };
        document.Elements.Add(first);
        document.Elements.Add(second);
        document.Elements.Add(survivor);
        document.Elements.Add(new BoxElement { GroupId = retainedGroup });
        document.Elements.Add(new BoxElement { GroupId = retainedGroup });
        document.Elements.Remove(first);
        document.Elements.Remove(second);
        Groups.DissolveSingles(document);
        Assert.Null(survivor.GroupId);
        Assert.Equal(2, document.Elements.Count(e => e.GroupId == retainedGroup));
    }
}
