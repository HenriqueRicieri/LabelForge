using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

public readonly record struct SpacingGap(bool Horizontal, int Start, int End, double Cross)
{
    public int Dots => End - Start;
}

public static class SpacingGuides
{
    public static int? Snap(DotRect moving, IReadOnlyList<DotRect> neighbours, bool horizontal, int threshold)
    {
        DotRect? before = Nearest(moving, neighbours, horizontal, true);
        DotRect? after = Nearest(moving, neighbours, horizontal, false);
        int? shift = null;
        int best = threshold + 1;
        int size = End(moving, horizontal) - Start(moving, horizontal);
        if (before is { } left && after is { } right && CommonLane(moving, left, horizontal, right))
        {
            int span = Start(right, horizontal) - End(left, horizontal) - size;
            // An odd span cannot be split into equal gaps on the printer-dot grid.
            if (span >= 0 && span % 2 == 0) Consider(End(left, horizontal) + span / 2);
        }
        if (before is { } near && Nearest(near, neighbours, horizontal, true, moving) is { } previous)
            Consider(End(near, horizontal) + Start(near, horizontal) - End(previous, horizontal));
        if (after is { } far && Nearest(far, neighbours, horizontal, false, moving) is { } next)
            Consider(Start(far, horizontal) - (Start(next, horizontal) - End(far, horizontal)) - size);
        return shift;

        void Consider(int position)
        {
            int delta = position - Start(moving, horizontal);
            if (Math.Abs(delta) >= best) return;
            best = Math.Abs(delta);
            shift = delta;
        }
    }

    public static IReadOnlyList<SpacingGap> Measure(DotRect moving, IReadOnlyList<DotRect> neighbours)
    {
        List<SpacingGap> gaps = [];
        MeasureAxis(true);
        MeasureAxis(false);
        return gaps;

        void MeasureAxis(bool horizontal)
        {
            if (Nearest(moving, neighbours, horizontal, true) is { } before)
            {
                SpacingGap gap = Gap(before, moving, horizontal);
                gaps.Add(gap);
                if (Nearest(before, neighbours, horizontal, true, moving) is { } previous &&
                    Start(before, horizontal) - End(previous, horizontal) == gap.Dots)
                    gaps.Add(Gap(previous, before, horizontal, moving));
            }
            if (Nearest(moving, neighbours, horizontal, false) is { } after)
            {
                SpacingGap gap = Gap(moving, after, horizontal);
                gaps.Add(gap);
                if (Nearest(after, neighbours, horizontal, false, moving) is { } next &&
                    Start(next, horizontal) - End(after, horizontal) == gap.Dots)
                    gaps.Add(Gap(after, next, horizontal, moving));
            }
        }
    }

    private static DotRect? Nearest(DotRect moving, IReadOnlyList<DotRect> neighbours,
        bool horizontal, bool before, DotRect? lane = null)
    {
        DotRect? nearest = null;
        int best = int.MaxValue;
        foreach (DotRect candidate in neighbours)
        {
            if (!CommonLane(moving, candidate, horizontal, lane)) continue;
            int gap = before ? Start(moving, horizontal) - End(candidate, horizontal)
                : Start(candidate, horizontal) - End(moving, horizontal);
            if (gap < 0 || gap >= best) continue;
            best = gap;
            nearest = candidate;
        }
        return nearest;
    }

    private static bool CommonLane(DotRect a, DotRect b, bool horizontal, DotRect? lane) =>
        Math.Max(CrossStart(a, horizontal), Math.Max(CrossStart(b, horizontal),
            lane is { } l ? CrossStart(l, horizontal) : int.MinValue)) <
        Math.Min(CrossEnd(a, horizontal), Math.Min(CrossEnd(b, horizontal),
            lane is { } r ? CrossEnd(r, horizontal) : int.MaxValue));

    private static SpacingGap Gap(DotRect before, DotRect after, bool horizontal, DotRect? lane = null)
    {
        int lo = Math.Max(CrossStart(before, horizontal), CrossStart(after, horizontal));
        int hi = Math.Min(CrossEnd(before, horizontal), CrossEnd(after, horizontal));
        if (lane is { } l)
        {
            lo = Math.Max(lo, CrossStart(l, horizontal));
            hi = Math.Min(hi, CrossEnd(l, horizontal));
        }
        return new SpacingGap(horizontal, End(before, horizontal), Start(after, horizontal), (lo + hi) / 2d);
    }

    private static int Start(DotRect r, bool horizontal) => horizontal ? r.X : r.Y;
    private static int End(DotRect r, bool horizontal) => horizontal ? r.X + r.Width : r.Y + r.Height;
    private static int CrossStart(DotRect r, bool horizontal) => horizontal ? r.Y : r.X;
    private static int CrossEnd(DotRect r, bool horizontal) => horizontal ? r.Y + r.Height : r.X + r.Width;
}
