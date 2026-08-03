namespace LabelForge.Core.Model;

/// <summary>
/// How much room a barcode's human-readable interpretation line takes under the bars.
///
/// It is NOT a fixed number of dots, which is what this replaces: the printer draws the
/// line in font A magnified by the narrow-bar width from `^BY`, so a barcode drawn at a
/// wider module gets a proportionally taller line. Measured on both engines at modules 1
/// to 8 and across all five symbologies, the ink below the bars is 12, 19, 27, 36, 45,
/// 53, 62 and 71 dots - so the flat 30 this replaces was 9 dots too generous at the
/// default module of 2 and 6 dots too short at module 4, which is the direction that
/// matters: a footprint shorter than the ink says a label fits when it clips.
///
/// The bar height is deliberately not in it, measured at 50, 100 and 200 dots: the line
/// sits under the bars wherever they end, it does not scale with them. Nor does the print
/// density, measured at 6, 8, 12 and 24 dpmm: this is a count of dots, and a dot is a dot.
///
/// So there is nothing here for a resize gesture to keep in proportion, which is the
/// backlog item this answers (B4): dragging a barcode wider raises the module width and
/// the printer scales the line to match, by itself, in the ZPL rather than in the model.
/// </summary>
public static class BarcodeInterpretation
{
    /// <summary>
    /// Font A's own cell height, which is what the printer magnifies. The manual publishes
    /// it as 5 by 9 and the driver confirmed it (B14), and it is what makes the measured
    /// numbers a rule rather than a curve fit: at module 1 the line takes exactly the
    /// 9-dot cell plus the gap.
    /// </summary>
    private const int CellHeightDots = 9;

    /// <summary>The blank the printer leaves between the last bar and the cell, measured.
    /// The ink's own gap grows from 5 dots to 8 across the module range as the glyphs get
    /// taller inside their cell; 3 is what makes the whole box cover the ink at every
    /// module without standing more than 4 dots clear of it.</summary>
    private const int GapDots = 3;

    /// <summary>What the line adds to a barcode's footprint, or nothing when it is
    /// turned off.</summary>
    public static int HeightDots(BarcodeElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.PrintInterpretationLine ? HeightDots(element.ModuleWidthDots) : 0;
    }

    /// <summary>What the line takes at a given narrow-bar width.</summary>
    public static int HeightDots(int moduleWidthDots) =>
        (CellHeightDots * Math.Max(moduleWidthDots, 1)) + GapDots;
}
