using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

/// <summary>Stacking operations on whole groups. Step and drag retain the existing values;
/// front and back retain their established behavior of assigning new extreme values.</summary>
public static class ZOrder
{
    public static bool BringToFront(LabelDocument document, IEnumerable<Element> selection) =>
        MoveToEnd(document, selection, front: true);

    public static bool SendToBack(LabelDocument document, IEnumerable<Element> selection) =>
        MoveToEnd(document, selection, front: false);

    private static bool MoveToEnd(LabelDocument document, IEnumerable<Element> selection, bool front)
    {
        var members = Groups.Expand(document, selection);
        if (members.Count == 0) return false;
        int next = front ? document.Elements.Max(e => e.ZOrder) + 1
            : document.Elements.Min(e => e.ZOrder) - members.Count;
        foreach (var element in members) element.ZOrder = next++;
        return true;
    }

    public static bool Step(LabelDocument document, IEnumerable<Element> selection, bool forward)
    {
        var selected = Groups.Expand(document, selection).ToHashSet();
        if (selected.Count == 0) return false;
        var order = document.Elements.OrderBy(e => e.ZOrder).ToList();
        var units = new List<IReadOnlyList<Element>>();
        var seen = new HashSet<Element>();
        foreach (var element in order)
        {
            if (seen.Contains(element)) continue;
            var members = Groups.Members(document, element);
            units.Add(members);
            seen.UnionWith(members);
        }
        IEnumerable<int> steps = forward
            ? Enumerable.Range(0, units.Count).Reverse() : Enumerable.Range(0, units.Count);
        bool moved = false;
        foreach (int i in steps)
        {
            int j = forward ? i + 1 : i - 1;
            if (!selected.Contains(units[i][0]) || j < 0 || j >= units.Count ||
                selected.Contains(units[j][0]) || units[i].Any(a => units[j].Any(b => a.ZOrder == b.ZOrder)))
                continue;
            (units[i], units[j]) = (units[j], units[i]);
            moved = true;
        }
        return moved && Apply(document, order, units.SelectMany(u => u).ToList(), apply: true);
    }

    public static bool CanMove(LabelDocument document, Element source, Element target, bool inFront) =>
        Move(document, source, target, inFront, apply: false);

    public static bool Move(LabelDocument document, Element source, Element target, bool inFront) =>
        Move(document, source, target, inFront, apply: true);

    private static bool Move(LabelDocument document, Element source, Element target, bool inFront, bool apply)
    {
        if (!document.Elements.Contains(source) || !document.Elements.Contains(target) || Groups.IsHeld(document, source))
            return false;
        var moving = Groups.Members(document, source).ToHashSet();
        if (moving.Contains(target)) return false;
        var targets = Groups.Members(document, target);
        var order = document.Elements.OrderBy(e => e.ZOrder).ToList();
        var next = order.Where(e => !moving.Contains(e)).ToList();
        int at = inFront ? next.FindLastIndex(e => targets.Contains(e)) + 1
            : next.FindIndex(e => targets.Contains(e));
        next.InsertRange(at, order.Where(moving.Contains));
        return Apply(document, order, next, apply);
    }

    private static bool Apply(LabelDocument document, List<Element> order, List<Element> next, bool apply)
    {
        if (order.SequenceEqual(next)) return false;
        var indices = next.Select((element, i) => (element, i)).ToDictionary(p => p.element, p => p.i);
        // Preserve the relative order of tied values, and reject permutations that stable
        // ZPL sorting could not reproduce without inventing new values.
        foreach (var tie in order.GroupBy(e => e.ZOrder).Where(g => g.Count() > 1))
        {
            var positions = tie.Select(e => indices[e]).ToList();
            if (!positions.SequenceEqual(positions.Order())) return false;
        }
        int[] values = order.Select(e => e.ZOrder).ToArray();
        if (!document.Elements.OrderBy(e => values[indices[e]]).SequenceEqual(next)) return false;
        if (apply)
            for (int i = 0; i < next.Count; i++) next[i].ZOrder = values[i];
        return true;
    }
}
