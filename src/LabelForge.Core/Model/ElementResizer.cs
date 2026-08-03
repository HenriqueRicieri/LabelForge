namespace LabelForge.Core.Model;

/// <summary>
/// Maps a resize gesture (target footprint in dots) onto each element type's real
/// degrees of freedom. Barcodes and QR codes are quantized: their width only changes
/// in module steps, so the gesture snaps to the nearest valid module width or
/// magnification instead of resizing freely (what you get is what prints).
/// Rotated elements are resized using their unrotated footprint (approximation).
/// </summary>
public static class ElementResizer
{
    /// <summary>Smallest side ^GE and ^GD accept.</summary>
    public const int MinShapeSideDots = 3;

    /// <summary>Largest side ^GE accepts; a printer replaces anything above it. ^GD runs
    /// much further, so this one is the ellipse's alone.</summary>
    public const int MaxEllipseSideDots = 4095;

    private static readonly ElementBoundsCalculator Bounds = new();

    public static void Resize(Element element, int targetWidth, int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(element);

        switch (element)
        {
            case BoxElement box:
                box.WidthDots = Math.Max(targetWidth, 4);
                box.HeightDots = Math.Max(targetHeight, 4);
                break;

            case LineElement line:
                line.LengthDots = Math.Max(line.IsVertical ? targetHeight : targetWidth, 1);
                break;

            case EllipseElement ellipse:
                // ZPL's own range for ^GE: below 3 there is no shape left, and anything
                // above 4095 the printer replaces with 4095, so a handle dragged past
                // that would keep moving while the ink stopped.
                ellipse.WidthDots = Math.Clamp(targetWidth, MinShapeSideDots, MaxEllipseSideDots);
                ellipse.HeightDots = Math.Clamp(targetHeight, MinShapeSideDots, MaxEllipseSideDots);
                break;

            case DiagonalLineElement diagonal:
                diagonal.WidthDots = Math.Max(targetWidth, MinShapeSideDots);
                diagonal.HeightDots = Math.Max(targetHeight, MinShapeSideDots);
                break;

            case TextElement text:
                // Font 0 scales freely by height; width stays derived (0) unless the
                // user had set an explicit width, which then scales proportionally.
                int newHeight = Math.Max(targetHeight, 6);
                if (text.FontWidthDots > 0 && text.FontHeightDots > 0)
                {
                    text.FontWidthDots = Math.Max(
                        (int)Math.Round((double)text.FontWidthDots * newHeight / text.FontHeightDots), 1);
                }

                text.FontHeightDots = newHeight;
                break;

            case BarcodeElement barcode:
            {
                // The interpretation line comes off the target before the bars are sized,
                // and it is worked out at the module the drag started from, since the new
                // one is not known until the width below is resolved. Off by a dot or two
                // during a drag that changes both; the bars are what the pointer follows.
                barcode.HeightDots = Math.Max(
                    targetHeight - BarcodeInterpretation.HeightDots(barcode), 10);

                int modules = Bounds.GetUnrotatedBounds(barcode).Width / Math.Max(barcode.ModuleWidthDots, 1);
                if (modules > 0)
                {
                    barcode.ModuleWidthDots = Math.Clamp(
                        (int)Math.Round((double)targetWidth / modules), 1, 10);
                }

                break;
            }

            case QrCodeElement qr:
            {
                int modules = Bounds.GetUnrotatedBounds(qr).Width / Math.Max(qr.Magnification, 1);
                if (modules > 0)
                {
                    int target = Math.Max(targetWidth, targetHeight);
                    qr.Magnification = Math.Clamp(
                        (int)Math.Round((double)target / modules), 1, 10);
                }

                break;
            }

            case DataMatrixElement dm:
            {
                int modules = Bounds.GetUnrotatedBounds(dm).Width / Math.Max(dm.ModuleSizeDots, 1);
                if (modules > 0)
                {
                    int target = Math.Max(targetWidth, targetHeight);
                    dm.ModuleSizeDots = Math.Clamp(
                        (int)Math.Round((double)target / modules), 1, 20);
                }

                break;
            }

            case Pdf417Element pdf:
            {
                // A stacked symbol quantizes on both axes and they are independent:
                // width in whole modules across the row, height in whole rows.
                Pdf417Shape shape = Pdf417Metrics.Measure(pdf);
                int modules = Pdf417Metrics.WidthModules(shape.Columns, pdf.Truncate);
                pdf.ModuleWidthDots = Math.Clamp(
                    (int)Math.Round((double)targetWidth / modules), 1, 10);
                pdf.RowHeightDots = Math.Max(
                    (int)Math.Round((double)targetHeight / shape.Rows), 1);
                break;
            }

            case ImageElement image:
                image.WidthDots = Math.Max(targetWidth, 8);
                image.HeightDots = Math.Max(targetHeight, 8);
                break;
        }
    }
}
