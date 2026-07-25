namespace LabelForge.Core.Model;

/// <summary>Where an element sits relative to the printable label area.</summary>
public enum PlacementStatus
{
    /// <summary>Fully inside the label; prints normally.</summary>
    Inside,

    /// <summary>Origin is on the label but the footprint crosses the right or bottom
    /// edge; the printer cuts the overflow off at the edge.</summary>
    Clipped,

    /// <summary>Cannot be expressed in ZPL (negative origin) or the origin is past the
    /// label edge; the generator skips it entirely.</summary>
    NotPrintable,

    /// <summary>The user marked it "do not print". Skipped for the same reason as
    /// <see cref="NotPrintable"/> but by choice rather than by accident, which is why it
    /// is a separate answer: the canvas and the warning line must not call a deliberate
    /// decision a mistake.</summary>
    Suppressed,
}

/// <summary>
/// The single place the "does this element print?" rule lives, so the ZPL generator,
/// the designer warnings, and the canvas outlines can never disagree. Printability is
/// decided from the origin alone (exact); the clipped classification uses the heuristic
/// footprint and only feeds warnings and outlines, never generation.
/// </summary>
public static class ElementPlacement
{
    /// <summary>Working area kept around the label on the design surface, where
    /// elements can be parked without printing.</summary>
    public const double PasteboardMarginMm = 20;

    /// <summary>True when the element is meant to print and its origin can be emitted as
    /// a ^FO that lands on the label. ZPL has no negative origins, and an origin past the
    /// edge prints nothing. A "do not print" element fails here too, so the generator
    /// needs no second rule and cannot disagree with the canvas about which is which.</summary>
    public static bool IsPrintable(Element element, int widthDots, int heightDots) =>
        !element.DoNotPrint &&
        element.X >= 0 && element.Y >= 0 && element.X < widthDots && element.Y < heightDots;

    public static PlacementStatus Classify(Element element, DotRect bounds, int widthDots, int heightDots)
    {
        // Asked for, so it is reported as a choice even when the element also happens to
        // sit off the label.
        if (element.DoNotPrint)
        {
            return PlacementStatus.Suppressed;
        }

        if (!IsPrintable(element, widthDots, heightDots))
        {
            return PlacementStatus.NotPrintable;
        }

        return bounds.X + bounds.Width > widthDots || bounds.Y + bounds.Height > heightDots
            ? PlacementStatus.Clipped
            : PlacementStatus.Inside;
    }
}
