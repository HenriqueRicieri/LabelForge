namespace LabelForge.Core.Zpl;

/// <summary>
/// What one call of the generator is producing: which copy of a serialized run, the
/// timestamp to stamp into clock fields, and whether the job's copy count belongs on
/// this block.
///
/// The defaults describe the first copy at the current time, which is what the
/// designer's ZPL pane and the file export show. A print run that numbers its labels
/// here rather than on the printer walks the copy index and turns the copy count off,
/// because each block is then one specific label.
/// </summary>
public sealed record GenerationContext
{
    /// <summary>Zero-based position in the run of this block's FIRST label; drives counter
    /// values. A block laid out several columns wide holds that many consecutive labels,
    /// so column n carries copy index CopyIndex + n.</summary>
    public int CopyIndex { get; init; }

    /// <summary>
    /// How many columns of the design this block carries across the web. One is an
    /// ordinary label and emits exactly the bytes it always did.
    ///
    /// It is the run's decision rather than the document's, which is the whole reason it
    /// lives here: <see cref="ZplGenerator.Generate(LabelDocument)"/> is the single label
    /// the ZPL pane shows and the file export writes, and it has to keep parsing back into
    /// the document it came from. Only <see cref="PrintJob"/> asks for the web.
    /// </summary>
    public int Columns { get; init; } = 1;

    /// <summary>The moment clock variables are formatted against. Captured once per run
    /// so every label in a job carries the same timestamp.</summary>
    public DateTime Now { get; init; } = DateTime.Now;

    /// <summary>Whether ^PQ may be emitted. False when the caller is already producing
    /// one block per copy.</summary>
    public bool EmitCopies { get; init; } = true;

    /// <summary>Whether the ~DG downloads a repeated graphic needs belong on this block.
    /// A block always recalls them with ^XG; only the download itself is optional, and
    /// it is skipped exactly once: on the later blocks of a multi-copy run whose first
    /// block already put the graphic in the printer's memory. Any block generated on its
    /// own keeps the download, so what the ZPL pane shows and what a file export writes
    /// are always self-contained.</summary>
    public bool IncludeGraphicDownloads { get; init; } = true;
}

/// <summary>
/// What the generator actually used for the last document it produced. The designer
/// reads this to tell the user whether the printer or the PC is doing the counting,
/// and the print pipeline reads it to decide how many blocks a run needs.
/// </summary>
/// <param name="Warnings">Deduplicated, in encounter order; empty when everything the
/// document asked for was expressible in ZPL.</param>
public sealed record GenerationInfo(
    bool UsesPrinterCounter,
    bool UsesPrinterClock,
    bool UsesSoftwareCounter,
    IReadOnlyList<string> Warnings)
{
    public static GenerationInfo Empty { get; } = new(false, false, false, []);
}
