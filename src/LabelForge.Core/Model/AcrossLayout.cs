namespace LabelForge.Core.Model;

/// <summary>
/// How a design is laid out across the web when the stock carries more than one label
/// side by side.
///
/// The label itself never changes: a 3-across design is one label, drawn once, edited
/// once and measured once. What changes is the run, which prints three of it per pull
/// of the media. So every number here is about the job rather than about the document,
/// and it lives in one place because the generator, the print job, the printer warning
/// and the designer all have to agree about how wide the web is and how many pulls a
/// quantity costs.
/// </summary>
public static class AcrossLayout
{
    /// <summary>Ceiling on the columns a design is laid out in. The widest stock the
    /// Zebra catalog carries is 8 across, and a printhead is 104 mm; this only exists so
    /// a mistyped number cannot ask for a web thousands of labels wide.</summary>
    public const int MaxAcross = 20;

    /// <summary>Columns actually printed, whatever the document happens to store. One is
    /// the ordinary label and the only value that changes nothing.</summary>
    public static int Columns(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Math.Clamp(document.LabelsAcross, 1, MaxAcross);
    }

    /// <summary>Distance from one column's left edge to the next: the label plus the gap
    /// the die cut leaves between them.</summary>
    public static double PitchMm(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.WidthMm + Math.Max(document.AcrossGapMm, 0);
    }

    public static int PitchDots(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Units.MmToDots(PitchMm(document), document.Dpmm);
    }

    /// <summary>Width of the printed web: every column plus the gaps between them, and
    /// nothing outside the last one. The liner's own edge margins are not printed on, so
    /// they are not part of what ^PW has to cover.</summary>
    public static double WebWidthMm(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int columns = Columns(document);
        return columns <= 1
            ? document.WidthMm
            : document.WidthMm + ((columns - 1) * PitchMm(document));
    }

    /// <summary>The web in dots. Deliberately built from the same per-column pitch the
    /// generator offsets by, so the last column cannot end up outside the stated width by
    /// a dot of rounding.</summary>
    public static int WebWidthDots(LabelDocument document) =>
        WebWidthDots(document, Columns(document));

    /// <summary>The web for a stated number of columns, which is what the generator asks:
    /// how wide a run is laid out is the run's decision, and only the pitch is the
    /// stock's.</summary>
    public static int WebWidthDots(LabelDocument document, int columns)
    {
        ArgumentNullException.ThrowIfNull(document);
        return columns <= 1
            ? document.WidthDots
            : document.WidthDots + ((columns - 1) * PitchDots(document));
    }

    /// <summary>
    /// How many pulls of the media a quantity costs. The printer feeds the whole web at
    /// once, so a row is the unit it counts in and ^PQ states rows, not labels.
    /// </summary>
    public static int Rows(int copies, int columns) =>
        Math.Max(1, (Math.Max(copies, 1) + Math.Max(columns, 1) - 1) / Math.Max(columns, 1));

    /// <summary>How many labels those rows actually produce. A row cannot be part
    /// printed, so asking for 10 on 3-across stock produces 12.</summary>
    public static int LabelsInRows(int rows, int columns) =>
        Math.Max(rows, 1) * Math.Max(columns, 1);
}
