using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

public static class RotationGesture
{
    /// <summary>Turns around the original drawn centre, including any ^FT or barcode offsets.</summary>
    public static void Apply(Element element, Orientation orientation, DotRect startBounds)
    {
        FieldRotation.Set(element, orientation);
        DotRect bounds = new ElementBoundsCalculator().GetBounds(element);
        double x = startBounds.X + startBounds.Width / 2.0 - bounds.Width / 2.0;
        double y = startBounds.Y + startBounds.Height / 2.0 - bounds.Height / 2.0;
        element.X += (int)Math.Round(x, MidpointRounding.AwayFromZero) - bounds.X;
        element.Y += (int)Math.Round(y, MidpointRounding.AwayFromZero) - bounds.Y;
    }
}
