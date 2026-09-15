using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

public enum GestureKind
{
    Move,
    Resize,
    Rotate,
}

public sealed record GestureLayerPlan(
    IReadOnlyList<Element> Below,
    IReadOnlyList<Element> Moving,
    IReadOnlyList<Element> Above,
    bool CanComposite);

public static class GestureLayers
{
    public static GestureLayerPlan Split(LabelDocument document, IReadOnlyList<Element> moving)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(moving);

        var requested = moving.ToHashSet();
        var ordered = document.Elements.Where(e => e.IsVisible).OrderBy(e => e.ZOrder).ToArray();
        var selected = ordered.Where(e => requested.Contains(e) && !Groups.IsHeld(document, e)).ToArray();
        if (selected.Length == 0) return new GestureLayerPlan(ordered, [], [], false);

        var selectedSet = selected.ToHashSet();
        int first = Array.IndexOf(ordered, selected[0]);
        int last = Array.IndexOf(ordered, selected[^1]);
        var below = ordered.Take(first).ToArray();
        var above = ordered.Skip(first).Where(e => !selectedSet.Contains(e)).ToArray();

        // Three layers cannot retain an unselected field between two moving fields.
        bool contiguous = last - first + 1 == selected.Length;
        bool supported = contiguous && !document.IsContinuous && !document.Print.ReverseAll
            && !selected.Any(e => e.IsReversed || e is TextElement { IsBlock: true }
                || e is TextElement { Font: not ZplFont.Scalable })
            && !above.Any(e => e.IsReversed);
        return new GestureLayerPlan(below, selected, above, supported);
    }

    /// <summary>Bounds for a standalone moving render. Samples must already be resolved
    /// on the supplied elements. Include the field origins as well as their drawn bounds
    /// so a cropped ^FT field still has a nonnegative origin in the rendering surface.</summary>
    public static DotRect GetMovingViewport(IReadOnlyList<Element> moving)
    {
        ArgumentNullException.ThrowIfNull(moving);
        if (moving.Count == 0) return new DotRect(0, 0, 1, 1);

        var calculator = new ElementBoundsCalculator();
        int left = int.MaxValue, top = int.MaxValue;
        int right = int.MinValue, bottom = int.MinValue;
        foreach (Element element in moving)
        {
            DotRect bounds = PreviewBounds(element, calculator);
            int padding = element switch
            {
                TextElement text => Math.Max(16, Math.Max(text.FontHeightDots, text.FontWidthDots)),
                DiagonalLineElement diagonal => Math.Max(16, diagonal.ThicknessDots + 2),
                _ when element.Anchor == FieldAnchor.Baseline => Math.Max(bounds.Width, bounds.Height) + 16,
                _ => 16,
            };
            left = Math.Min(left, Math.Min(bounds.X, element.X) - padding);
            top = Math.Min(top, Math.Min(bounds.Y, element.Y) - padding);
            right = Math.Max(right, Math.Max(bounds.X + bounds.Width, element.X + 1) + padding);
            bottom = Math.Max(bottom, Math.Max(bounds.Y + bounds.Height, element.Y + 1) + padding);
        }
        return new DotRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static DotRect PreviewBounds(Element element, ElementBoundsCalculator calculator)
    {
        DotRect local = calculator.GetLocalBounds(element);
        // Selection metrics estimate symbol capacity. Cropping must also contain a
        // larger encoding caused by correction level, compaction or multibyte data.
        switch (element)
        {
            case QrCodeElement qr:
                int qrSide = 177 * Math.Max(1, qr.Magnification); // Model 2, version 40.
                local = local with { Width = qrSide, Height = qrSide };
                break;
            case DataMatrixElement matrix:
                int matrixSide = 144 * Math.Max(1, matrix.ModuleSizeDots); // Largest square ECC 200 symbol.
                local = local with { Width = matrixSide, Height = matrixSide };
                break;
            case Pdf417Element pdf:
                int columns = pdf.DataColumns > 0
                    ? Math.Clamp(pdf.DataColumns, Pdf417Metrics.MinColumns, Pdf417Metrics.MaxColumns)
                    : Pdf417Metrics.MaxColumns;
                local = local with
                {
                    Width = Pdf417Metrics.WidthModules(columns, pdf.Truncate) * Math.Max(1, pdf.ModuleWidthDots),
                    Height = Pdf417Metrics.MaxRows * Math.Max(1, pdf.RowHeightDots),
                };
                break;
            case BoxElement box:
                local = local with
                {
                    Width = Math.Max(local.Width, box.ThicknessDots),
                    Height = Math.Max(local.Height, box.ThicknessDots),
                };
                break;
            case LineElement line:
                local = local with
                {
                    Width = Math.Max(local.Width, line.ThicknessDots),
                    Height = Math.Max(local.Height, line.ThicknessDots),
                };
                break;
            default:
                return calculator.GetBounds(element);
        }

        (int x, int y) = FieldTypeset.DrawnTopLeft(element, local);
        if (FieldRotation.Applies(element) && element.Orientation is Orientation.Rotated90 or Orientation.Rotated270)
            local = local with { Width = local.Height, Height = local.Width };
        return local with { X = x + local.X, Y = y + local.Y };
    }
}
