namespace LabelForge.Core.Model;

/// <summary>
/// What the printer does with a label once it has printed it (^MM's first parameter).
///
/// <see cref="PrinterDefault"/> emits nothing at all, which is the same contract darkness
/// and speed already keep: a fresh document adds no ^MM, so a printer set up for its stock
/// keeps the mode its operator chose. Everything else is a deliberate instruction about
/// this run.
///
/// Which modes a given machine has is a hardware question the manual will not answer for
/// us ("refer to the User Guide for your printer"), and ZPL's own answer is to ignore an
/// ^MM it cannot honour rather than to fail. So the list is the manual's, unfiltered.
/// </summary>
public enum MediaHandling
{
    /// <summary>Emit no ^MM. The printer keeps whatever mode it is configured for.</summary>
    PrinterDefault = 0,

    /// <summary>^MMT: the label advances so the web is over the tear bar.</summary>
    TearOff,

    /// <summary>^MMP: the liner is peeled away and the label waits to be taken.</summary>
    PeelOff,

    /// <summary>^MMR: the label is wound onto a rewind spindle.</summary>
    Rewind,

    /// <summary>^MMA: an applicator applies the label.</summary>
    Applicator,

    /// <summary>^MMC: the cutter cuts after every label.</summary>
    Cutter,

    /// <summary>^MMD: the cutter waits for a ~JK rather than cutting as it goes.</summary>
    DelayedCutter,
}

/// <summary>
/// Job-level print settings stored on the document. Zero means "leave the printer's
/// configured default" for darkness and speed, so a fresh document adds nothing to
/// the ZPL; only deliberate choices are emitted.
/// </summary>
public sealed class PrintSettings
{
    /// <summary>Copies to print (^PQ). Emitted only when greater than 1.</summary>
    public int Copies { get; set; } = 1;

    /// <summary>Darkness adjustment (^MD), -30 to 30; 0 keeps the printer default.</summary>
    public int DarknessDelta { get; set; }

    /// <summary>Print speed in inches per second (^PR), 2 to 14; 0 keeps the printer
    /// default.</summary>
    public int SpeedIps { get; set; }

    /// <summary>What the printer does with each label (^MM). The default emits nothing.</summary>
    public MediaHandling MediaHandling { get; set; } = MediaHandling.PrinterDefault;

    /// <summary>^MM's second parameter, which presents the next label before it is
    /// asked for. Only <see cref="MediaHandling.PeelOff"/> has anything to pre-peel, so
    /// it is emitted with that mode and with no other.</summary>
    public bool Prepeel { get; set; }

    /// <summary>
    /// Cut after every this many labels (^PQ's pause-and-cut value); 0 is off.
    ///
    /// Counted in labels, the same unit <see cref="Copies"/> uses, and converted to rows
    /// on multi-across stock by the same rule, because ^PQ counts pulls of the web rather
    /// than labels. It rides with the cutter modes and nothing else: without a cutter the
    /// printer has nothing to do at the end of a group.
    /// </summary>
    public int CutAfterLabels { get; set; }

    /// <summary>
    /// ^LT: moves the whole printed format up or down relative to the top edge of the
    /// media, -120 to 120 dot rows. Zero emits nothing.
    ///
    /// This is a registration nudge for how the stock sits in the printer, not a design
    /// change, which is why it is here rather than in the elements' own coordinates. The
    /// label's own space cannot express it: every element moving 8 dots down is a
    /// different statement from the format sitting 8 dots further from the media's edge.
    /// </summary>
    public int LabelTopDots { get; set; }

    /// <summary>The ^MM letter for a mode, or null for the printer's own default.</summary>
    public static char? Letter(MediaHandling mode) => mode switch
    {
        MediaHandling.TearOff => 'T',
        MediaHandling.PeelOff => 'P',
        MediaHandling.Rewind => 'R',
        MediaHandling.Applicator => 'A',
        MediaHandling.Cutter => 'C',
        MediaHandling.DelayedCutter => 'D',
        _ => null,
    };

    /// <summary>The mode an ^MM letter names, or null when it names one not modelled
    /// (RFID, kiosk and the two reserved values, none of which describe what happens to
    /// an ordinary label).</summary>
    public static MediaHandling? FromLetter(string letter) =>
        letter.Trim().ToUpperInvariant() switch
        {
            "T" => MediaHandling.TearOff,
            "P" => MediaHandling.PeelOff,
            "R" => MediaHandling.Rewind,
            "A" => MediaHandling.Applicator,
            "C" => MediaHandling.Cutter,
            "D" => MediaHandling.DelayedCutter,
            _ => null,
        };

    /// <summary>True for the modes that end a group of labels with a cut, which is what
    /// makes <see cref="CutAfterLabels"/> mean anything.</summary>
    public static bool Cuts(MediaHandling mode) =>
        mode is MediaHandling.Cutter or MediaHandling.DelayedCutter;
}
