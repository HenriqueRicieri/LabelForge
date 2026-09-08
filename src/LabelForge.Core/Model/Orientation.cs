namespace LabelForge.Core.Model;

/// <summary>
/// Field orientation. ZPL only supports these four values (command letters N/R/I/B),
/// so a label element can never carry a free rotation angle.
/// </summary>
public enum Orientation
{
    /// <summary>No rotation (ZPL "N").</summary>
    Normal,

    /// <summary>Rotated 90 degrees clockwise (ZPL "R").</summary>
    Rotated90,

    /// <summary>Rotated 180 degrees (ZPL "I").</summary>
    Rotated180,

    /// <summary>Rotated 270 degrees, i.e. bottom-up (ZPL "B").</summary>
    Rotated270,
}

/// <summary>
/// Which fields ZPL will actually turn.
///
/// Not every command takes an orientation. Text and the barcodes carry the letter in the
/// command itself (`^A0R`, `^BCR`), but the graphic primitives have no such argument:
/// `^GB`, `^GE`, `^GD` and `^GF` state a width and a height and draw them, so setting an
/// orientation on one changes nothing a printer does. Measured, not assumed: rendering a
/// box, an ellipse, a diagonal, a line and an image at 0 and at 90 degrees produces
/// byte-identical output, and only text differs.
///
/// It lives here rather than inside the bounds calculator because two places need the same
/// answer and they must not drift: the footprint has to stop swapping width for height on a
/// field that does not turn, or a rotated box's outline claims a shape its ink never takes,
/// and the properties panel has to stop offering a control that cannot do anything.
/// </summary>
public static class FieldRotation
{
    /// <summary>True when the element's <see cref="Element.Orientation"/> reaches the ZPL
    /// at all.</summary>
    public static bool Applies(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element is TextElement or BarcodeElement or QrCodeElement
            or DataMatrixElement or Pdf417Element;
    }

    /// <summary>
    /// True when a quarter turn means anything for this element, which is a different
    /// question from <see cref="Applies"/> and the reason both exist.
    ///
    /// A line and a diagonal turn, they just do not say so in <see cref="Element.Orientation"/>:
    /// a vertical line is `^GB` with its sides swapped and a "/" is `^GD` with the other
    /// letter, so the quarter turn IS expressible where a box's or an ellipse's is not.
    /// `Applies` still answers whether `Orientation` reaches the ZPL, and everything that
    /// reads or writes that property keeps asking it. This one answers whether to OFFER the
    /// turn: the rotation handle, its hit test, its hover cursor, the Rotate 90 command and
    /// the panel's control.
    ///
    /// The trap, stated because it is the one a reader walks into: the bounds calculator's
    /// side swap and <see cref="FieldTypeset"/> must keep asking `Applies`, NOT this. A
    /// line's footprint is already measured from `IsVertical` and a diagonal's box is
    /// swapped by <see cref="Set"/> itself, so a swap keyed on this would turn them twice.
    /// </summary>
    public static bool CanRotate(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Applies(element) || element is LineElement or DiagonalLineElement;
    }

    /// <summary>
    /// How many quarter turns are distinguishable, for the handle's magnetic stops. Four
    /// where the orientation letter reaches the ZPL; two for a line and a diagonal, because
    /// a bar has no direction and neither does a line segment, so 180 degrees is 0 and 270
    /// is 90. Only meaningful where <see cref="CanRotate"/> is true.
    /// </summary>
    public static int Stops(Element element) => Applies(element) ? 4 : 2;

    /// <summary>
    /// The turn this element is currently in, whichever property happens to carry it.
    ///
    /// The handle and the panel set an ABSOLUTE orientation rather than stepping one, so
    /// they need to read the current one first, and reading <see cref="Element.Orientation"/>
    /// off a line answers a question the generator never asks. A line reads as 90 when it is
    /// vertical; a diagonal reads as 90 when it leans the way `^GD` does not default to.
    /// </summary>
    public static Orientation Get(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element switch
        {
            LineElement line => line.IsVertical ? Orientation.Rotated90 : Orientation.Normal,

            // "/" is `^GD`'s own default letter and the model's, so it is the zero here and
            // "\" is the quarter turn. Neither is more rotated than the other; one of them
            // had to be called nought.
            DiagonalLineElement diagonal =>
                diagonal.LeansRight ? Orientation.Normal : Orientation.Rotated90,

            _ => Applies(element) ? element.Orientation : Orientation.Normal,
        };
    }

    /// <summary>
    /// Puts the element in the turn asked for, writing whichever property carries it.
    ///
    /// Writing <see cref="Element.Orientation"/> on a line is exactly the bug this pair
    /// exists to prevent: the generator ignores it, so the handle would turn nothing while
    /// the document changed underneath.
    ///
    /// Where there are only two stops, 180 lands on 0 and 270 on 90 rather than being
    /// refused, so the handle can be dragged the whole way round and always arrive
    /// somewhere legal.
    /// </summary>
    public static void Set(Element element, Orientation orientation)
    {
        ArgumentNullException.ThrowIfNull(element);
        bool quarter = orientation is Orientation.Rotated90 or Orientation.Rotated270;
        switch (element)
        {
            case LineElement line:
                line.IsVertical = quarter;
                break;

            // A quarter turn of a diagonal is the other lean IN A BOX ON ITS SIDE: "/" from
            // bottom-left to top-right, turned clockwise, runs top-left to bottom-right in a
            // box whose sides have traded places. Flipping the letter alone would leave the
            // line crossing a box it no longer fits.
            case DiagonalLineElement diagonal when diagonal.LeansRight == quarter:
                diagonal.LeansRight = !quarter;
                (diagonal.WidthDots, diagonal.HeightDots) = (diagonal.HeightDots, diagonal.WidthDots);
                break;

            case DiagonalLineElement:
                break;

            default:
                if (Applies(element))
                {
                    element.Orientation = orientation;
                }

                break;
        }
    }

    /// <summary>
    /// One quarter turn clockwise from wherever it is now. Built on <see cref="Get"/> and
    /// <see cref="Set"/> so there is one answer to what a turn means per element type, and
    /// a no-op on anything <see cref="CanRotate"/> refuses.
    /// </summary>
    public static void Rotate90(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (Applies(element))
        {
            element.Orientation = (Orientation)(((int)element.Orientation + 1) % 4);
            return;
        }

        if (CanRotate(element))
        {
            Set(
                element,
                Get(element) == Orientation.Normal ? Orientation.Rotated90 : Orientation.Normal);
        }
    }
}
