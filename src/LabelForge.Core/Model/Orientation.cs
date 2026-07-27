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
}
