using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

public static class RotationGesture
{
    /// <summary>Turns around the original drawn centre, including any ^FT or barcode offsets.</summary>
    public static void Apply(Element element, Orientation orientation, DotRect startBounds)
    {
        FieldRotation.Set(element, orientation);
        RepositionToCenter(element, startBounds);
    }

    /// <summary>A quarter turn around the element's current drawn center.</summary>
    public static void Rotate90(Element element)
    {
        if (!FieldRotation.CanRotate(element)) return;
        DotRect startBounds = new ElementBoundsCalculator().GetBounds(element);
        FieldRotation.Rotate90(element);
        RepositionToCenter(element, startBounds);
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
}
