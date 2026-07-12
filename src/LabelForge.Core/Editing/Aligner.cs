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
/// </summary>
public static class Aligner
{
    /// <summary>Returns true when at least one element moved.</summary>
    public static bool Align(
        IReadOnlyList<Element> elements, AlignEdge edge, int labelWidthDots, int labelHeightDots)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var bounds = new ElementBoundsCalculator();
        var items = elements.Where(e => !e.IsLocked).ToList();
        if (items.Count == 0)
        {
            return false;
        }

        bool horizontal = edge is AlignEdge.Left or AlignEdge.CenterHorizontal or AlignEdge.Right;

        int lo, hi;
        if (items.Count == 1)
        {
            lo = 0;
            hi = horizontal ? labelWidthDots : labelHeightDots;
        }
        else
        {
            var rects = items.Select(bounds.GetBounds).ToList();
            lo = rects.Min(r => horizontal ? r.X : r.Y);
            hi = rects.Max(r => horizontal ? r.X + r.Width : r.Y + r.Height);
        }

        bool moved = false;
        foreach (Element element in items)
        {
            DotRect b = bounds.GetBounds(element);
            int size = horizontal ? b.Width : b.Height;
            int current = horizontal ? b.X : b.Y;
            int target = edge switch
            {
                AlignEdge.Left or AlignEdge.Top => lo,
                AlignEdge.Right or AlignEdge.Bottom => hi - size,
                _ => lo + (hi - lo - size) / 2,
            };

            moved |= MoveBy(element, target - current, horizontal);
        }

        return moved;
    }

    /// <summary>Equalizes the gaps between three or more elements along one axis; the
    /// outermost elements stay put. Returns true when anything moved.</summary>
    public static bool Distribute(IReadOnlyList<Element> elements, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var bounds = new ElementBoundsCalculator();
        var items = elements.Where(e => !e.IsLocked).ToList();
        if (items.Count < 3)
        {
            return false;
        }

        var ordered = items
            .Select(e => (Element: e, Bounds: bounds.GetBounds(e)))
            .OrderBy(t => horizontal ? t.Bounds.X : t.Bounds.Y)
            .ToList();

        int first = horizontal ? ordered[0].Bounds.X : ordered[0].Bounds.Y;
        DotRect lastBounds = ordered[^1].Bounds;
        int last = horizontal ? lastBounds.X + lastBounds.Width : lastBounds.Y + lastBounds.Height;
        int totalSize = ordered.Sum(t => horizontal ? t.Bounds.Width : t.Bounds.Height);

        // Negative gaps are fine: overlapping elements still spread evenly.
        double gap = (last - first - totalSize) / (double)(ordered.Count - 1);

        bool moved = false;
        double cursor = first;
        foreach ((Element element, DotRect b) in ordered)
        {
            int current = horizontal ? b.X : b.Y;
            moved |= MoveBy(element, (int)Math.Round(cursor) - current, horizontal);
            cursor += (horizontal ? b.Width : b.Height) + gap;
        }

        return moved;
    }

    private static bool MoveBy(Element element, int delta, bool horizontal)
    {
        if (horizontal)
        {
            int next = Math.Max(element.X + delta, 0);
            if (next == element.X)
            {
                return false;
            }

            element.X = next;
        }
        else
        {
            int next = Math.Max(element.Y + delta, 0);
            if (next == element.Y)
            {
                return false;
            }

            element.Y = next;
        }

        return true;
    }
}
