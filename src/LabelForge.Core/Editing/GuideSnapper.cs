namespace LabelForge.Core.Editing;

/// <summary>
/// Axis-independent snapping for canvas drags. Given the dragged selection's box on
/// one axis, finds the smallest shift that aligns its low edge, center, or high edge
/// with one of the targets (user guides, label edges, label center). Both axes are
/// evaluated separately by the caller.
/// </summary>
public static class GuideSnapper
{
    /// <summary>Shift to apply to the box (0 when nothing is within
    /// <paramref name="threshold"/>) and the target that matched, for highlighting.</summary>
    public static (int Shift, int? Target) Snap(
        int lo, int hi, IEnumerable<int> targets, int threshold)
    {
        ArgumentNullException.ThrowIfNull(targets);

        int bestShift = 0;
        int? bestTarget = null;
        int bestDistance = threshold + 1;
        ReadOnlySpan<int> edges = [lo, (lo + hi) / 2, hi];

        foreach (int target in targets)
        {
            foreach (int edge in edges)
            {
                int distance = Math.Abs(target - edge);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestShift = target - edge;
                    bestTarget = target;
                }
            }
        }

        return (bestShift, bestTarget);
    }
}
