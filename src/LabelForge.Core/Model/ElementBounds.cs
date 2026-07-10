namespace LabelForge.Core.Model;

/// <summary>An axis-aligned rectangle in printer dots.</summary>
public readonly record struct DotRect(int X, int Y, int Width, int Height)
{
    public bool Contains(int x, int y) =>
        x >= X && x < X + Width && y >= Y && y < Y + Height;

    public bool Intersects(DotRect other) =>
        X < other.X + other.Width && other.X < X + Width &&
        Y < other.Y + other.Height && other.Y < Y + Height;
}

/// <summary>
/// Computes the approximate footprint of an element in dot space. Used by the
/// designer canvas for hit-testing and selection outlines only; the rendered
/// bitmap remains the visual truth (WYSIWYG rule). Text and barcode widths are
/// heuristics; they are refined as the designer matures.
/// </summary>
public sealed class ElementBoundsCalculator : IElementVisitor
{
    private DotRect _result;

    public DotRect GetBounds(Element element)
    {
        DotRect bounds = GetUnrotatedBounds(element);

        // ZPL rotates fields around the origin; approximating the rotated footprint
        // as a width/height swap at the same origin is close enough for selection.
        return element.Orientation is Orientation.Rotated90 or Orientation.Rotated270
            ? bounds with { Width = bounds.Height, Height = bounds.Width }
            : bounds;
    }

    /// <summary>The footprint before orientation is applied. Used by resize logic,
    /// which reasons about the element's intrinsic width (e.g. barcode modules).</summary>
    public DotRect GetUnrotatedBounds(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Accept(this);
        return _result;
    }

    public void Visit(TextElement element)
    {
        // Font 0 average advance is roughly 0.55 of the character height.
        int advance = element.FontWidthDots > 0
            ? element.FontWidthDots
            : (int)Math.Round(element.FontHeightDots * 0.55);
        int width = Math.Max(element.Text.Length, 1) * Math.Max(advance, 1);
        _result = new DotRect(element.X, element.Y, width, element.FontHeightDots);
    }

    public void Visit(BarcodeElement element)
    {
        int modules = element.Symbology switch
        {
            // EAN-13 and UPC-A are fixed-width symbologies (95 modules plus quiet zones).
            BarcodeSymbology.Ean13 or BarcodeSymbology.UpcA => 113,

            // Code 39: 3 wide + 6 narrow bars plus a gap per character, start/stop included.
            BarcodeSymbology.Code39 => (int)Math.Ceiling(
                (3 * element.WideBarRatio + 7) * (element.Data.Length + 2)),

            // Code 128: ~11 modules per symbol; digit pairs share a symbol in subset C.
            _ => 11 * (element.Data.All(char.IsAsciiDigit)
                ? (element.Data.Length + 1) / 2 + 2
                : element.Data.Length + 2) + 35,
        };

        int height = element.HeightDots + (element.PrintInterpretationLine ? 30 : 0);
        _result = new DotRect(element.X, element.Y, modules * element.ModuleWidthDots, height);
    }

    public void Visit(QrCodeElement element)
    {
        // Approximate byte-mode capacity at medium error correction for versions 1-10.
        ReadOnlySpan<int> capacity = [14, 26, 42, 62, 84, 106, 122, 152, 180, 213];
        int version = 10;
        for (int i = 0; i < capacity.Length; i++)
        {
            if (element.Data.Length <= capacity[i])
            {
                version = i + 1;
                break;
            }
        }

        int modules = 17 + 4 * version;
        int side = modules * Math.Max(element.Magnification, 1);
        _result = new DotRect(element.X, element.Y, side, side);
    }

    public void Visit(LineElement element)
    {
        (int w, int h) = element.IsVertical
            ? (element.ThicknessDots, element.LengthDots)
            : (element.LengthDots, element.ThicknessDots);
        _result = new DotRect(element.X, element.Y, w, h);
    }

    public void Visit(BoxElement element) =>
        _result = new DotRect(element.X, element.Y, element.WidthDots, element.HeightDots);
}
