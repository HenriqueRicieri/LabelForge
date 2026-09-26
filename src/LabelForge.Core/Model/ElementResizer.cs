namespace LabelForge.Core.Model;

/// <summary>Which text dimensions a resize handle controls.</summary>
public enum TextResizeMode
{
    Height,
    Width,
    Proportional,
    Free,
}

public readonly record struct TextResizeStart(
    int HeightDots, int WidthDots, int BlockWidthDots, int BoundsWidthDots, int BoundsHeightDots);

/// <summary>Maps a target footprint in dots onto each element's printable dimensions.</summary>
public static class ElementResizer
{
    /// <summary>Smallest side ^GE and ^GD accept.</summary>
    public const int MinShapeSideDots = 3;

    /// <summary>Largest side ^GE accepts; a printer replaces anything above it. ^GD runs
    /// much further, so this one is the ellipse's alone.</summary>
    public const int MaxEllipseSideDots = 4095;

    private static readonly ElementBoundsCalculator Bounds = new();

    public static TextResizeStart CaptureText(TextElement text)
    {
        ArgumentNullException.ThrowIfNull(text);
        DotRect bounds = Bounds.GetLocalBounds(text);
        return new TextResizeStart(text.FontHeightDots, text.FontWidthDots,
            text.BlockWidthDots, bounds.Width, bounds.Height);
    }

    public static void ResizeText(TextElement text, TextResizeStart start,
        int targetWidth, int targetHeight, TextResizeMode mode, int dpmm = 8)
    {
        ArgumentNullException.ThrowIfNull(text);
        int initialHeight = Math.Max(start.HeightDots, 1);
        int initialWidth = start.WidthDots > 0 ? start.WidthDots :
            ZplFont.Cell(text.Font, dpmm) is { } cell
                ? cell.WidthDots * ZplFont.Magnification(text.Font, initialHeight, vertical: true, dpmm)
                : initialHeight;

        if (targetWidth == start.BoundsWidthDots && targetHeight == start.BoundsHeightDots)
        {
            text.FontHeightDots = start.HeightDots;
            text.FontWidthDots = start.WidthDots;
            text.BlockWidthDots = start.BlockWidthDots;
            return;
        }

        int height = mode == TextResizeMode.Width
            ? start.HeightDots
            : Round(initialHeight * (double)targetHeight / Math.Max(start.BoundsHeightDots, 1));
        int width = mode switch
        {
            TextResizeMode.Height => initialWidth,
            TextResizeMode.Proportional => Round(initialWidth * (double)height / initialHeight),
            _ => Round(initialWidth * (double)targetWidth / Math.Max(start.BoundsWidthDots, 1)),
        };

        if (ZplFont.Cell(text.Font, dpmm) is { } bitmap)
        {
            if (mode != TextResizeMode.Width)
                height = Math.Clamp(Round((double)height / bitmap.HeightDots), 1,
                    ZplFont.MaxMagnification) * bitmap.HeightDots;
            if (mode != TextResizeMode.Height)
                width = Math.Clamp(Round((double)width / bitmap.WidthDots), 1,
                    ZplFont.MaxMagnification) * bitmap.WidthDots;
        }
        else
        {
            if (mode != TextResizeMode.Width)
                height = Math.Clamp(height, 10, 32_000);
            if (mode != TextResizeMode.Height)
                width = Math.Clamp(width, 10, 32_000);
        }

        bool keepAutomaticWidth = start.WidthDots == 0 &&
            (mode == TextResizeMode.Proportional ||
             mode == TextResizeMode.Width && width == initialWidth ||
             mode == TextResizeMode.Height && height == start.HeightDots ||
             mode == TextResizeMode.Free && height == start.HeightDots && width == initialWidth);
        text.FontHeightDots = height;
        text.FontWidthDots = keepAutomaticWidth ? 0 : width;
        if (start.BlockWidthDots > 0)
        {
            int blockWidth = mode switch
            {
                TextResizeMode.Height => start.BlockWidthDots,
                TextResizeMode.Proportional => Round(
                    start.BlockWidthDots * (double)height / initialHeight),
                _ => Round(start.BlockWidthDots * (double)targetWidth /
                    Math.Max(start.BoundsWidthDots, 1)),
            };
            text.BlockWidthDots = mode == TextResizeMode.Height
                ? start.BlockWidthDots : Math.Clamp(blockWidth, 1, 9999);
        }
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

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
                ResizeText(text, CaptureText(text), targetWidth, targetHeight,
                    TextResizeMode.Proportional);
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
