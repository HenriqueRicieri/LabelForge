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
    /// <summary>The least working area kept around the label on each side.</summary>
    public const double MinimumPasteboardMarginMm = 20;

    /// <summary>Preview margins are rounded up to this step, so the underlay keeps its
    /// size while an off-label element moves a little.</summary>
    public const double PreviewMarginStepMm = 5;

    /// <summary>
    /// The working area around the label on the design surface, per axis, in dots: half
    /// the label's size on each side, and never less than
    /// <see cref="MinimumPasteboardMarginMm"/>. Elements can be parked there without
    /// printing, and the view scrolls no further than its edge.
    ///
    /// Proportional rather than fixed because a fixed margin is a sliver beside a large
    /// label and most of the screen beside a small one.
    /// </summary>
    public static (int X, int Y) PasteboardMarginDots(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int minimum = Units.MmToDots(MinimumPasteboardMarginMm, document.Dpmm);
        return (Math.Max(minimum, (document.WidthDots + 1) / 2),
                Math.Max(minimum, (document.HeightDots + 1) / 2));
    }

    /// <summary>
    /// The margin a preview needs to show the given footprints: how far the furthest one
    /// reaches past any label edge, rounded up to <see cref="PreviewMarginStepMm"/> and
    /// capped at the pasteboard. Zero when every footprint is on the label.
    ///
    /// Measured from the footprints rather than taken as the whole pasteboard, because the
    /// render grows with the square of the margin and the pasteboard is large around a
    /// large label, while a parked element is usually just past the edge.
    /// </summary>
    public static int PreviewMarginDots(LabelDocument document, IEnumerable<DotRect> footprints)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(footprints);
        int reach = 0;
        foreach (DotRect b in footprints)
        {
            reach = Math.Max(reach, Math.Max(
                Math.Max(-b.X, b.X + b.Width - document.WidthDots),
                Math.Max(-b.Y, b.Y + b.Height - document.HeightDots)));
        }

        if (reach <= 0)
        {
            return 0;
        }

        int step = Math.Max(Units.MmToDots(PreviewMarginStepMm, document.Dpmm), 1);
        (int x, int y) = PasteboardMarginDots(document);
        return Math.Min((reach + step - 1) / step * step, Math.Max(x, y));
    }

    /// <summary>
    /// True when the element is meant to print and its origin lands on the label. ZPL has
    /// no negative origins under either placing command, and an origin past the edge
    /// prints nothing. A "do not print" element fails here too, so the generator needs no
    /// second rule and cannot disagree with the canvas about which is which.
    ///
    /// It is the origin that is tested, not the drawn box. Those are the same corner for
    /// a `^FO` field and are not for a `^FT` one, whose box can extend up and left of the
    /// origin it is placed by; that is a legal label and the printer draws what fits, so
    /// it is <see cref="Classify"/>'s clipping that has something to say about it.
    ///
    /// Continuous stock has no bottom edge to fall off: the roll simply gets longer, so
    /// only the left, top and right bounds apply. That is also what keeps the rule
    /// answerable at all there, since the label's length is measured from the elements
    /// this decides about.
    /// </summary>
    public static bool IsPrintable(Element element, LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return !element.DoNotPrint &&
               element.X >= 0 && element.Y >= 0 && element.X < document.WidthDots &&
               (document.IsContinuous || element.Y < document.HeightDots);
    }

    public static PlacementStatus Classify(Element element, DotRect bounds, LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Asked for, so it is reported as a choice even when the element also happens to
        // sit off the label.
        if (element.DoNotPrint)
        {
            return PlacementStatus.Suppressed;
        }

        if (!IsPrintable(element, document))
        {
            return PlacementStatus.NotPrintable;
        }

        // On continuous stock the bottom cannot clip, because the label was measured to
        // reach the last element in the first place. The near edges can only be crossed
        // by a field placed from an anchor other than its top-left corner, since a
        // printable origin is already on the label.
        return bounds.X < 0 || bounds.Y < 0 ||
               bounds.X + bounds.Width > document.WidthDots ||
               bounds.Y + bounds.Height > document.HeightDots
            ? PlacementStatus.Clipped
            : PlacementStatus.Inside;
    }
}
