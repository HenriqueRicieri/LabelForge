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
                barcode.HeightDots = Math.Max(
                    targetHeight - (barcode.PrintInterpretationLine ? 30 : 0), 10);

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
        }
    }
}
