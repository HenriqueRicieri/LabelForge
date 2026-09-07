using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

public enum AlignEdge
{
    Left,
    CenterHorizontal,
    Right,
    Top,
    Middle,
    Bottom,
}

/// <summary>
/// Alignment and distribution for the designer selection, operating on the visual
/// footprints (ElementBoundsCalculator), so what lines up is the ink, not the origins.
/// A single element aligns against the label; two or more align within the selection's
/// own bounding box. Locked elements never move. Origins are clamped at 0 so an
/// alignment can never push an element into the unprintable negative range (the QR
/// footprint sits below its origin, so flush-to-edge would otherwise need a negative
/// ^FO, which ZPL cannot express).
///
/// Everything works in UNITS: a unit is one or more elements that line up as a single box
/// and move by a single delta, which is what a group is. The element-taking methods are
/// the same thing with every element its own unit, and they exist because most callers and
/// every alignment test have no groups in them.
/// </summary>
public static class Aligner
{
    /// <summary>Returns true when at least one element moved.</summary>
    public static bool Align(
        IReadOnlyList<Element> elements, AlignEdge edge, int labelWidthDots, int labelHeightDots)
    {
        ArgumentNullException.ThrowIfNull(elements);
        return AlignUnits(Alone(elements), edge, labelWidthDots, labelHeightDots);
    }

    /// <summary>Equalizes the gaps between three or more elements along one axis; the
    /// outermost elements stay put. Returns true when anything moved.</summary>
    public static bool Distribute(IReadOnlyList<Element> elements, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(elements);
        return DistributeUnits(Alone(elements), horizontal);
    }

    /// <summary>
    /// Aligns units rather than elements: a group lines up by the box around all of it and
    /// keeps its internal layout, instead of every member lining up on its own and the
    /// group coming apart.
    ///
    /// A unit holding any locked element sits the whole move out, because a group moves as
    /// one thing and half of one moving is not that.
    /// </summary>
    public static bool AlignUnits(
        IReadOnlyList<IReadOnlyList<Element>> units,
        AlignEdge edge,
        int labelWidthDots,
        int labelHeightDots)
    {
        ArgumentNullException.ThrowIfNull(units);

        var bounds = new ElementBoundsCalculator();
        List<IReadOnlyList<Element>> items = Movable(units);
        if (items.Count == 0)
        {
            return false;
        }

        bool horizontal = edge is AlignEdge.Left or AlignEdge.CenterHorizontal or AlignEdge.Right;
        List<DotRect> boxes = [.. items.Select(u => Box(u, bounds))];

        int lo, hi;
        if (items.Count == 1)
        {
            lo = 0;
            hi = horizontal ? labelWidthDots : labelHeightDots;
        }
        else
        {
            lo = boxes.Min(r => horizontal ? r.X : r.Y);
            hi = boxes.Max(r => horizontal ? r.X + r.Width : r.Y + r.Height);
        }

        bool moved = false;
        for (int i = 0; i < items.Count; i++)
        {
            DotRect b = boxes[i];
            int size = horizontal ? b.Width : b.Height;
            int current = horizontal ? b.X : b.Y;
            int target = edge switch
            {
                AlignEdge.Left or AlignEdge.Top => lo,
                AlignEdge.Right or AlignEdge.Bottom => hi - size,
                _ => lo + (hi - lo - size) / 2,
            };

            moved |= MoveUnit(items[i], target - current, horizontal);
        }

        return moved;
    }

    /// <summary>Distribution over units; see <see cref="AlignUnits"/> for what a unit is.
    /// Three units, not three elements: a group counts once however many members it
    /// has.</summary>
    public static bool DistributeUnits(
        IReadOnlyList<IReadOnlyList<Element>> units, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(units);

        var bounds = new ElementBoundsCalculator();
        List<IReadOnlyList<Element>> items = Movable(units);
        if (items.Count < 3)
        {
            return false;
        }

        List<(IReadOnlyList<Element> Unit, DotRect Bounds)> ordered =
        [
            .. items
                .Select(u => (Unit: u, Bounds: Box(u, bounds)))
                .OrderBy(t => horizontal ? t.Bounds.X : t.Bounds.Y),
        ];

        int first = horizontal ? ordered[0].Bounds.X : ordered[0].Bounds.Y;
        DotRect lastBounds = ordered[^1].Bounds;
        int last = horizontal ? lastBounds.X + lastBounds.Width : lastBounds.Y + lastBounds.Height;
        int totalSize = ordered.Sum(t => horizontal ? t.Bounds.Width : t.Bounds.Height);

        // Negative gaps are fine: overlapping elements still spread evenly.
        double gap = (last - first - totalSize) / (double)(ordered.Count - 1);

        bool moved = false;
        double cursor = first;
        foreach ((IReadOnlyList<Element> unit, DotRect b) in ordered)
        {
            int current = horizontal ? b.X : b.Y;
            moved |= MoveUnit(unit, (int)Math.Round(cursor) - current, horizontal);
            cursor += (horizontal ? b.Width : b.Height) + gap;
        }

        return moved;
    }

    private static IReadOnlyList<IReadOnlyList<Element>> Alone(IReadOnlyList<Element> elements) =>
        [.. elements.Select(e => (IReadOnlyList<Element>)new[] { e })];

    private static List<IReadOnlyList<Element>> Movable(
        IReadOnlyList<IReadOnlyList<Element>> units) =>
        [.. units.Where(u => u.Count > 0 && !u.Any(e => e.IsLocked))];

    /// <summary>The box around a whole unit, which for a single element is its own.</summary>
    private static DotRect Box(IReadOnlyList<Element> unit, ElementBoundsCalculator bounds)
    {
        DotRect box = bounds.GetBounds(unit[0]);
        for (int i = 1; i < unit.Count; i++)
        {
            DotRect b = bounds.GetBounds(unit[i]);
            int x = Math.Min(box.X, b.X);
            int y = Math.Min(box.Y, b.Y);
            box = new DotRect(
                x,
                y,
                Math.Max(box.X + box.Width, b.X + b.Width) - x,
                Math.Max(box.Y + box.Height, b.Y + b.Height) - y);
        }

        return box;
    }

    /// <summary>
    /// Moves every element of a unit by the same delta, so the unit keeps its shape. The
    /// clamp at 0 is applied ONCE to the unit rather than per element: clamping each member
    /// on its own is what would pull a group out of shape against the label edge.
    /// </summary>
    private static bool MoveUnit(IReadOnlyList<Element> unit, int delta, bool horizontal)
    {
        int min = unit.Min(e => horizontal ? e.X : e.Y);
        delta = Math.Max(delta, -min);
        if (delta == 0)
        {
            return false;
        }

        foreach (Element element in unit)
        {
            if (horizontal)
            {
                element.X += delta;
            }
            else
            {
                element.Y += delta;
            }
        }

        return true;
    }
}
