namespace LabelForge.Core.Model;

/// <param name="Columns">Data columns the symbol is laid out in: the element's own
/// count, or the estimate standing in for the printer's automatic choice.</param>
/// <param name="Rows">Rows of codewords, which is what the payload and the column
/// count decide between them.</param>
/// <param name="WidthDots">Symbol width before orientation is applied.</param>
/// <param name="HeightDots">Symbol height before orientation is applied.</param>
/// <param name="ColumnsAreAutomatic">True when the column count above is our estimate
/// rather than a number the element states. The printer picks its own, so anything
/// derived from this shape is an approximation and should say so.</param>
/// <param name="OverCapacity">True when the payload cannot fit a PDF417 at this
/// security level and column count.</param>
public readonly record struct Pdf417Shape(
    int Columns,
    int Rows,
    int WidthDots,
    int HeightDots,
    bool ColumnsAreAutomatic,
    bool OverCapacity);

/// <summary>
/// Symbol geometry for a PDF417, in one place so the selection outline, the resize
/// gesture and the properties panel's capacity warning cannot come to different
/// conclusions about how big a symbol is.
///
/// The module arithmetic is exact and was confirmed against the offline renderer: a
/// row is 17 modules per data column plus the start pattern, both row indicators and
/// the stop pattern. The codeword count from the data is an estimate, because the real
/// number depends on the compaction modes the encoder switches between as it walks the
/// string. Measured against the renderer it lands within one row on text and overstates
/// pure digits, which is the harmless direction for a selection box.
/// </summary>
public static class Pdf417Metrics
{
    /// <summary>A PDF417 holds no more codewords than this, whatever its shape.</summary>
    public const int MaxCodewords = 928;

    public const int MinRows = 3;
    public const int MaxRows = 90;
    public const int MinColumns = 1;
    public const int MaxColumns = 30;

    /// <summary>Modules across one row. Truncating drops the right row indicator and
    /// shortens the stop pattern to a single bar, which is the whole point of it.</summary>
    public static int WidthModules(int columns, bool truncate) =>
        truncate ? 17 * (columns + 2) + 1 : 17 * (columns + 4) + 1;

    /// <summary>Correction codewords carried at a security level: 2^(level+1).</summary>
    public static int ErrorCorrectionCodewords(int securityLevel) =>
        1 << (Math.Clamp(securityLevel, 0, 8) + 1);

    /// <summary>
    /// Codewords the data itself occupies. Numeric compaction packs about 2.9 digits
    /// into a codeword and text compaction two characters, plus one for the mode latch
    /// any real string needs.
    ///
    /// It cannot be exact, and the reason is worth stating: the true count depends on
    /// how often the encoder switches sub-mode as it walks the string, so two strings of
    /// the same length differ. "PDF417 PAYLOAD 12345" really does cost two codewords
    /// more than "LABEL PAYLOAD 12345" because the letters and digits interleave. This
    /// lands within one row either side of both.
    /// </summary>
    public static int EstimateDataCodewords(string data)
    {
        ArgumentNullException.ThrowIfNull(data);

        double perCodeword = data.All(char.IsAsciiDigit) ? 2.9 : 2.0;
        return (int)Math.Ceiling(data.Length / perCodeword) + 1;
    }

    public static Pdf417Shape Measure(Pdf417Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        int moduleWidth = Math.Max(element.ModuleWidthDots, 1);
        int rowHeight = Math.Max(element.RowHeightDots, 1);

        // One descriptor codeword states the symbol length, then the data, then the
        // correction block.
        int payload = 1
                      + EstimateDataCodewords(element.Data)
                      + ErrorCorrectionCodewords(element.SecurityLevel);

        bool automatic = element.DataColumns <= 0;
        int columns = automatic
            ? AutomaticColumns(payload)
            : Math.Clamp(element.DataColumns, MinColumns, MaxColumns);

        int rows = Math.Clamp(Rows(payload, columns), MinRows, MaxRows);

        return new Pdf417Shape(
            columns,
            rows,
            WidthModules(columns, element.Truncate) * moduleWidth,
            rows * rowHeight,
            automatic,
            payload > Math.Min(MaxCodewords, columns * MaxRows));
    }

    private static int Rows(int payload, int columns) =>
        (int)Math.Ceiling((double)payload / Math.Max(columns, 1));

    /// <summary>
    /// What the offline renderer does when the column count is left off: the narrowest
    /// symbol that still stays inside <see cref="AutomaticMaxRows"/>.
    ///
    /// This follows the renderer rather than the aspect ratio the ZPL reference states,
    /// and deliberately so. The footprint's job is the selection outline and the
    /// hit-test, and what the canvas draws underneath them is the render, so the two
    /// have to agree or the handles sit off the symbol. Measured across security levels
    /// and payload lengths it gets the width exactly right and the row count within one.
    ///
    /// It says nothing about the printer, which runs its own heuristic. That is the
    /// whole reason a fixed column count is the default for a symbol created here, and
    /// the reason <see cref="Pdf417Shape.ColumnsAreAutomatic"/> exists to be reported.
    /// </summary>
    private const int AutomaticMaxRows = 30;

    private static int AutomaticColumns(int payload)
    {
        for (int columns = MinColumns; columns < MaxColumns; columns++)
        {
            if (Rows(payload, columns) <= AutomaticMaxRows)
            {
                return columns;
            }
        }

        return MaxColumns;
    }
}
