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
        advance = Math.Max(advance, 1);
        int naturalWidth = Math.Max(element.Text.Length, 1) * advance;

        if (!element.IsBlock)
        {
            _result = new DotRect(element.X, element.Y, naturalWidth, element.FontHeightDots);
            return;
        }

        // A block's width is declared rather than guessed, which is the one text
        // footprint here that is not a heuristic. The line count still is: it comes from
        // the same average advance, capped by what the block is allowed to print.
        int lines = Math.Clamp(
            (int)Math.Ceiling((double)naturalWidth / Math.Max(element.BlockWidthDots, 1)),
            1,
            Math.Max(element.BlockMaxLines, 1));
        int height = lines * element.FontHeightDots
                     + Math.Max(lines - 1, 0) * element.BlockLineSpacingDots;

        _result = new DotRect(
            element.X, element.Y, element.BlockWidthDots, Math.Max(height, element.FontHeightDots));
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

        // BinaryKits' QR drawer paints 10 dots below the field origin (a constant,
        // measured at 8 and 12 dpmm); mirror it so the selection box hugs the ink.
        _result = new DotRect(element.X, element.Y + 10, side, side);
    }

    public void Visit(DataMatrixElement element)
    {
        // Square ECC 200 symbol sizes by ASCII capacity (10x10 through 52x52); an
        // estimate in the same spirit as the QR table above.
        ReadOnlySpan<(int Modules, int Capacity)> sizes =
        [
            (10, 3), (12, 5), (14, 8), (16, 12), (18, 18), (20, 22), (22, 30),
            (24, 36), (26, 44), (32, 62), (36, 86), (40, 114), (44, 144), (48, 174), (52, 204),
        ];

        int modules = 52;
        foreach ((int m, int capacity) in sizes)
        {
            if (element.Data.Length <= capacity)
            {
                modules = m;
                break;
            }
        }

        int side = modules * Math.Max(element.ModuleSizeDots, 1);
        _result = new DotRect(element.X, element.Y, side, side);
    }

    public void Visit(Pdf417Element element)
    {
        // The one place the symbol's shape is worked out; see Pdf417Metrics for how
        // exact each half of it is.
        Pdf417Shape shape = Pdf417Metrics.Measure(element);
        _result = new DotRect(element.X, element.Y, shape.WidthDots, shape.HeightDots);
    }

    public void Visit(ImageElement element) =>
        _result = new DotRect(element.X, element.Y, element.WidthDots, element.HeightDots);

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
