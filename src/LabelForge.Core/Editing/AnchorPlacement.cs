using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

public static class AnchorPlacement
{
    /// <summary>Changes which point X/Y names without moving the drawn field.</summary>
    public static void Set(Element element, FieldAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.Anchor == anchor) return;

        var calculator = new ElementBoundsCalculator();
        DotRect before = calculator.GetBounds(element);
        element.Anchor = anchor;
        DotRect after = calculator.GetBounds(element);
        element.X += before.X - after.X;
        element.Y += before.Y - after.Y;
    }
}
