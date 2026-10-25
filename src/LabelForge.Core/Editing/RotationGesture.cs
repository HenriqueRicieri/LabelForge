using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

public static class RotationGesture
{
    /// <summary>Turns around the original drawn centre, including any ^FT or barcode offsets.
    /// With <paramref name="label"/>, a field that was wholly on it stays on it.</summary>
    public static void Apply(
        Element element, Orientation orientation, DotRect startBounds, LabelDocument? label = null)
    {
        FieldRotation.Set(element, orientation);
        RepositionToCenter(element, startBounds);
        KeepOnLabel(element, startBounds, label);
    }

    /// <summary>A quarter turn around the element's current drawn center.</summary>
    public static void Rotate90(Element element, LabelDocument? label = null)
    {
        if (!FieldRotation.CanRotate(element)) return;
        DotRect startBounds = new ElementBoundsCalculator().GetBounds(element);
        FieldRotation.Rotate90(element);
        RepositionToCenter(element, startBounds);
        KeepOnLabel(element, startBounds, label);
    }

    private static void RepositionToCenter(Element element, DotRect startBounds)
    {
        DotRect bounds = new ElementBoundsCalculator().GetBounds(element);
        // Truncating the half-dot difference makes an opposite turn cancel it.
        int x = startBounds.X + (startBounds.Width - bounds.Width) / 2;
        int y = startBounds.Y + (startBounds.Height - bounds.Height) / 2;
        element.X += x - bounds.X;
        element.Y += y - bounds.Y;
    }

    /// <summary>
    /// A turn about the centre near an edge can put the origin off the label, and an
    /// off-label origin leaves the field out of the printed ZPL entirely. A field that
    /// was wholly on the label before the turn is moved the least it takes to stay on it;
    /// one parked on the pasteboard is off the label on purpose and is left alone.
    /// </summary>
    private static void KeepOnLabel(Element element, DotRect startBounds, LabelDocument? label)
    {
        if (label is null) return;
        int width = label.WidthDots;
        int height = label.HeightDots;
        bool bottomBound = !label.IsContinuous;
        bool wasInside = startBounds.X >= 0 && startBounds.Y >= 0 &&
            startBounds.X + startBounds.Width <= width &&
            (!bottomBound || startBounds.Y + startBounds.Height <= height);
        if (!wasInside) return;

        DotRect b = new ElementBoundsCalculator().GetBounds(element);
        element.X += Shift(b.X, b.Width, width);
        element.Y += bottomBound ? Shift(b.Y, b.Height, height) : Math.Max(0, -b.Y);
    }

    /// <summary>The move along one axis that brings [start, start + size) inside [0, limit),
    /// favouring the near edge when the field is longer than the label.</summary>
    private static int Shift(int start, int size, int limit)
    {
        if (start + size > limit) return Math.Max(limit - (start + size), -start);
        return start < 0 ? -start : 0;
    }
}
