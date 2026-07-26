namespace LabelForge.Core.Model;

/// <summary>
/// Converts a `^FT` origin into the `^FO` origin that draws the same thing.
///
/// ZPL has two ways of placing a field and they do not mean the same thing. `^FO` is the
/// field's top-left corner; `^FT` is its bottom-left, and for text that "bottom" is the
/// baseline rather than the bottom of the line. The model stores one kind of origin and
/// emits `^FO`, so a file written the other way has to be converted on the way in, or
/// every field lands low by its own height.
///
/// This is not a nicety for an unusual file. Across the sample corpus `^FT` outnumbers
/// `^FO` by 1304 to 477 and appears in 28 of the 30 files: it is the ordinary way real
/// labels are written, and reading it as `^FO` misplaced almost everything on almost
/// everything.
///
/// The rule was measured against the renderer in all four orientations rather than
/// reasoned from the manual, and it comes out as one shape. `^FT` anchors the field's own
/// bottom-left corner, so rotating the field moves that corner to a different corner of
/// the box actually drawn:
///
/// <code>
///   0 degrees   shift by (0,     H - b)      bottom-left
///   90          shift by (b,     0)          top-left
///   180         shift by (W,     b)          top-right
///   270         shift by (H - b, W)          bottom-right
/// </code>
///
/// where W and H are the field's unrotated size and b is how far the anchor sits above
/// the bottom edge: the descender depth for text, and nothing at all for everything else,
/// whose anchor really is the bottom.
/// </summary>
public static class FieldTypeset
{
    /// <summary>
    /// How much of the font size sits above the baseline, measured against the renderer at
    /// heights of 30, 40, 50 and 80: each one lands on the measured shift exactly.
    /// </summary>
    public const double AscentRatio = 0.715;

    /// <summary>
    /// The renderer paints a QR ten dots below its field origin, and `^FT` puts the same
    /// ten dots the other way, so converting one to the other has to account for both.
    /// The constant itself is the one <see cref="ElementBoundsCalculator"/> already
    /// mirrors; measured across magnifications 2, 4 and 8, it never moves.
    /// </summary>
    private const int QrDrawOffsetDots = 10;

    /// <summary>The origin to store for a field the file placed with `^FT`.</summary>
    public static (int X, int Y) ToFieldOrigin(Element element, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(element);

        (int width, int height, int anchorAboveBottom) = Extent(element);
        int aboveAnchor = height - anchorAboveBottom;

        (int shiftX, int shiftY) = element.Orientation switch
        {
            Orientation.Rotated90 => (anchorAboveBottom, 0),
            Orientation.Rotated180 => (width, anchorAboveBottom),
            Orientation.Rotated270 => (aboveAnchor, width),
            _ => (0, aboveAnchor),
        };

        return (x - shiftX, y - shiftY);
    }

    /// <summary>
    /// The field's unrotated size and how far its anchor sits above the bottom edge.
    ///
    /// Mostly the drawn footprint, with three exceptions that were measured rather than
    /// assumed: a barcode anchors on the bottom of its bars and lets the interpretation
    /// line hang below, so the footprint's extra height is not part of it; text anchors on
    /// the baseline, which is the descender depth above the bottom; and a QR carries the
    /// renderer's ten-dot offset at both ends.
    /// </summary>
    private static (int Width, int Height, int AnchorAboveBottom) Extent(Element element)
    {
        var bounds = new ElementBoundsCalculator();
        DotRect box = bounds.GetUnrotatedBounds(element);

        return element switch
        {
            TextElement text => (
                box.Width,
                text.FontHeightDots,
                text.FontHeightDots - (int)Math.Round(AscentRatio * text.FontHeightDots)),

            BarcodeElement barcode => (box.Width, barcode.HeightDots, 0),

            QrCodeElement => (box.Width, box.Height + 2 * QrDrawOffsetDots, 0),

            _ => (box.Width, box.Height, 0),
        };
    }
}
