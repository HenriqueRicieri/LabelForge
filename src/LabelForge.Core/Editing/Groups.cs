using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

/// <summary>
/// What a group is and what belongs to one. Groups are flat and one level deep
/// (<see cref="Element.GroupId"/>), and none of this reaches the ZPL: a printer has no
/// notion of a group, so a document generates byte-identical output grouped or not.
///
/// Every question about membership goes through here. Reading GroupId at the call site is
/// how the canvas, the clipboard and the outline panel would end up disagreeing about what
/// a click selects.
/// </summary>
public static class Groups
{
    /// <summary>
    /// Everything in the same group as this element, in z-order, or just the element itself
    /// when it is not in one. Never empty.
    /// </summary>
    public static IReadOnlyList<Element> Members(LabelDocument document, Element element)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        if (element.GroupId is not { } id)
        {
            return [element];
        }

        List<Element> members =
            [.. document.Elements.Where(e => e.GroupId == id).OrderBy(e => e.ZOrder)];
        return members.Count > 0 ? members : [element];
    }

    /// <summary>
    /// A selection widened to whole groups: picking any member picks all of them. A plain
    /// click, a marquee and Tab all pass their answer through this, so the three cannot
    /// disagree about what got selected. Ordered by z-order, nothing twice.
    /// </summary>
    public static IReadOnlyList<Element> Expand(
        LabelDocument document, IEnumerable<Element> selection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selection);

        List<Element> expanded = [];
        foreach (Element element in selection)
        {
            foreach (Element member in Members(document, element))
            {
                if (!expanded.Contains(member))
                {
                    expanded.Add(member);
                }
            }
        }

        return [.. expanded.OrderBy(e => e.ZOrder)];
    }

    /// <summary>
    /// Puts everything selected into one new group and stacks the members together.
    /// Anything already grouped brings the rest of its group with it, because the model is
    /// flat: there is no nesting for it to become.
    ///
    /// The stacking permutes z-order VALUES within the span the members already occupy, so
    /// the set of values in the document is unchanged and nothing outside that span moves.
    /// The members end up at the top of the span, which is where every editor puts them: at
    /// the level of whichever one was already in front.
    /// </summary>
    /// <returns>False when there was nothing to group: fewer than two elements, or one
    /// whole group being grouped with itself.</returns>
    public static bool Group(LabelDocument document, IEnumerable<Element> selection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selection);

        List<Element> members = [.. Expand(document, selection)];
        if (members.Count < 2)
        {
            return false;
        }

        // Already exactly one whole group with nothing joining it: grouping again would
        // swap the id for another one and record an undo step for no visible change.
        if (members[0].GroupId is { } existing &&
            members.TrueForAll(e => e.GroupId == existing) &&
            members.Count == Members(document, members[0]).Count)
        {
            return false;
        }

        var id = Guid.NewGuid();
        foreach (Element member in members)
        {
            member.GroupId = id;
        }

        Restack(document, members);
        return true;
    }

    /// <summary>Takes the selected elements out of whatever group they were in. Z-order is
    /// left exactly as it is: the stack the grouping produced is what the user has been
    /// looking at, and shuffling it on the way out would be a second surprise.</summary>
    /// <returns>False when nothing was grouped in the first place.</returns>
    public static bool Ungroup(IEnumerable<Element> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        bool changed = false;
        foreach (Element element in selection)
        {
            if (element.GroupId is null)
            {
                continue;
            }

            element.GroupId = null;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Gives copies their own group ids, so a pasted or duplicated group is a group of its
    /// own instead of joining the original. Called by BOTH ways copies are made, the
    /// clipboard and a Ctrl-drag, or the two would drift.
    ///
    /// A copy of only PART of a group is not a group: taking one member out with Alt-click
    /// and duplicating it should leave a loose element, not a group of one.
    /// </summary>
    public static void Remap(IEnumerable<Element> copies)
    {
        ArgumentNullException.ThrowIfNull(copies);

        List<Element> items = [.. copies];
        Dictionary<Guid, int> counts = [];
        foreach (Element item in items)
        {
            if (item.GroupId is { } id)
            {
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
        }

        Dictionary<Guid, Guid?> replacement = [];
        foreach ((Guid id, int count) in counts)
        {
            replacement[id] = count > 1 ? Guid.NewGuid() : null;
        }

        foreach (Element item in items)
        {
            if (item.GroupId is { } id)
            {
                item.GroupId = replacement[id];
            }
        }
    }

    /// <summary>
    /// Whether this element is held in place. A group moves as one thing, so one locked
    /// member holds the whole group: the alternative is a group that half moves, which is
    /// not a group at all. For an ungrouped element this is just its own lock.
    /// </summary>
    public static bool IsHeld(LabelDocument document, Element element)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        return element.IsLocked ||
               (element.GroupId is not null && Members(document, element).Any(e => e.IsLocked));
    }

    /// <summary>Packs the members together at the top of the span they already occupy,
    /// permuting the z-order values within that span and leaving every other element's
    /// value untouched.</summary>
    private static void Restack(LabelDocument document, List<Element> members)
    {
        List<Element> stack = [.. document.Elements.OrderBy(e => e.ZOrder)];
        List<int> at = [.. members.Select(m => stack.IndexOf(m)).Where(i => i >= 0)];
        if (at.Count < 2)
        {
            return;
        }

        int lo = at.Min();
        int hi = at.Max();
        List<Element> span = stack.GetRange(lo, hi - lo + 1);
        List<int> values = [.. span.Select(e => e.ZOrder)];
        List<Element> reordered =
            [.. span.Where(e => !members.Contains(e)), .. span.Where(members.Contains)];

        for (int i = 0; i < reordered.Count; i++)
        {
            reordered[i].ZOrder = values[i];
        }
    }
}
