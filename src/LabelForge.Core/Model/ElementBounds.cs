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
        DotRect local = GetLocalBounds(element);
        (int x, int y) = FieldTypeset.DrawnTopLeft(element, local);
        return local with { X = local.X + x, Y = local.Y + y };
    }

    /// <summary>
    /// The footprint measured from the field's own origin rather than from the label's,
    /// so it is the element's size and nothing about where it sits.
    ///
    /// Separate from <see cref="GetUnrotatedBounds"/> because placing a field can need
    /// its size first: a `^FT` origin is the field's bottom-left, so working out which
    /// dot its top-left lands on means knowing how tall and wide it is.
    /// <see cref="FieldTypeset"/> asks for this one, and asking for the placed bounds
    /// there would be circular.
    /// </summary>
    public DotRect GetLocalBounds(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Accept(this);
        return _result;
    }

    public void Visit(TextElement element)
    {
        // Font 0 is proportional, so its width comes from the characters rather than from
        // a count of them; TextMetrics holds the measured advances. The bitmapped fonts
        // are the opposite: fixed pitch, so their width is arithmetic on the count, and
        // ZplFont holds the manual's cells (confirmed against Labelary to the dot).
        //
        // The height stays the font size rather than the ink. The ink is 0.74 of it for
        // capitals and 0.94 once a descender appears, so a box that hugged one string
        // would jump the moment someone typed a "g". The font size is the line the field
        // occupies, and it is the stable answer.
        int naturalWidth = Math.Max(TextMetrics.WidthDots(element), 1);

        if (!element.IsBlock)
        {
            _result = new DotRect(0, 0, naturalWidth, element.FontHeightDots);
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
            0, 0, element.BlockWidthDots, Math.Max(height, element.FontHeightDots));
    }

    public void Visit(BarcodeElement element)
    {
        // Measured against the rendered ink, exactly, for every case tried. The quiet
        // zone is deliberately not in here: it is blank stock rather than symbol, it
        // belongs on both sides rather than only after the last bar, and QuietZone is
        // where it lives now.
        int modules = element.Symbology switch
        {
            // EAN-13 and UPC-A are fixed-width: 95 modules whatever the data.
            BarcodeSymbology.Ean13 or BarcodeSymbology.UpcA => 95,

            // Code 39: 3 wide and 6 narrow elements plus an inter-character gap, for the
            // data plus the start and stop characters. The last gap is not printed.
            BarcodeSymbology.Code39 => (int)Math.Ceiling(
                (3 * element.WideBarRatio + 7) * (element.Data.Length + 2)) - 1,

            // Code 128: start + one symbol per character + checksum + stop(13). Digits
            // are NOT paired on their own: that needs subset C, and neither the offline
            // renderer nor a printer in ^BC's default mode switches to it unasked. The
            // data can ask, though, and a GS1 payload always does, so the symbol count
            // is worked out by Code128Encoding rather than assumed from the length.
            _ => Zpl.Code128Encoding.WidthModules(element.Data),
        };

        int height = element.HeightDots + (element.PrintInterpretationLine ? 30 : 0);
        _result = new DotRect(0, 0, modules * element.ModuleWidthDots, height);
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

        // BinaryKits' QR drawer paints 10 dots below the field origin. A fixed number of
        // dots rather than a number of modules: measured across magnifications 2, 4 and 8
        // and across symbol versions it never moved, so it is scaled by nothing.
        _result = new DotRect(0, 10, side, side);
    }

    public void Visit(DataMatrixElement element)
    {
        // Square ECC 200 symbols by the codewords they hold, 10x10 through 52x52.
        ReadOnlySpan<(int Modules, int Codewords)> sizes =
        [
            (10, 3), (12, 5), (14, 8), (16, 12), (18, 18), (20, 22), (22, 30),
            (24, 36), (26, 44), (32, 62), (36, 86), (40, 114), (44, 144), (48, 174), (52, 204),
        ];

        // Codewords, not characters. ECC 200's ASCII mode packs a pair of digits into one
        // codeword, so sixteen digits cost eight and fit a symbol that holds eight; taking
        // the character count instead picked a symbol two sizes too big and drew the
        // outline four modules wider than the ink on every numeric code.
        int codewords = 0;
        for (int i = 0; i < element.Data.Length; i++)
        {
            bool pair = i + 1 < element.Data.Length &&
                        char.IsAsciiDigit(element.Data[i]) &&
                        char.IsAsciiDigit(element.Data[i + 1]);
            if (pair)
            {
                i++;
            }

            codewords++;
        }

        int modules = 52;
        foreach ((int m, int capacity) in sizes)
        {
            if (codewords <= capacity)
            {
                modules = m;
                break;
            }
        }

        int side = modules * Math.Max(element.ModuleSizeDots, 1);
        _result = new DotRect(0, 0, side, side);
    }

    public void Visit(Pdf417Element element)
    {
        // The one place the symbol's shape is worked out; see Pdf417Metrics for how
        // exact each half of it is.
        Pdf417Shape shape = Pdf417Metrics.Measure(element);
        _result = new DotRect(0, 0, shape.WidthDots, shape.HeightDots);
    }

    public void Visit(ImageElement element) =>
        _result = new DotRect(0, 0, element.WidthDots, element.HeightDots);

    public void Visit(LineElement element)
    {
        (int w, int h) = element.IsVertical
            ? (element.ThicknessDots, element.LengthDots)
            : (element.LengthDots, element.ThicknessDots);
        _result = new DotRect(0, 0, w, h);
    }

    public void Visit(BoxElement element) =>
        _result = new DotRect(0, 0, element.WidthDots, element.HeightDots);
}
