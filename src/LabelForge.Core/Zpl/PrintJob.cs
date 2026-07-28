using System.Text;
using LabelForge.Core.Model;

namespace LabelForge.Core.Zpl;

/// <summary>The bytes a print run sends, and what they will produce.</summary>
/// <param name="Zpl">The complete job: one label block, or one per row of the web.</param>
/// <param name="Labels">How many labels the printer will produce, which on multi-across
/// stock can be more than the copies asked for: a row cannot be part printed.</param>
/// <param name="CountedByPrinter">True when the printer serializes the run itself (^SN).</param>
/// <param name="Warnings">Reasons a printer-side feature was not used, plus any cap
/// applied to the run. Empty on an ordinary job.</param>
public sealed record PrintJobResult(
    string Zpl,
    int Labels,
    bool CountedByPrinter,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Builds the exact ZPL a print run sends, which is not always a single label block.
///
/// A counter the printer can serialize stays one block plus ^PQ: the printer produces
/// every copy at full speed and we send one small job. A counter numbered here cannot,
/// because the number is baked into the field data, so the run becomes one ^XA block
/// per copy. That difference is why ^SN is preferred and why a fallback is worth
/// telling the user about rather than silently sending ten thousand blocks.
///
/// It is also where multi-across stock is applied. The design is one label and the ZPL
/// pane shows one label; how many of it sit side by side on the web is a fact about the
/// run, so the layout happens here and <see cref="ZplGenerator.Generate(LabelDocument)"/>
/// keeps producing the single label that parses back into the document it came from.
/// </summary>
public static class PrintJob
{
    /// <summary>Ceiling on a run expanded here, matching the copy count the designer
    /// accepts. A document that somehow carries more is built up to this and says so.</summary>
    public const int MaxSoftwareCopies = 9999;

    public static PrintJobResult Build(LabelDocument document, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(document);

        var generator = new ZplGenerator();
        int columns = AcrossLayout.Columns(document);
        var context = new GenerationContext { Now = now, Columns = columns };
        string single = generator.Generate(document, context);
        GenerationInfo info = generator.LastRun;
        int copies = Math.Max(1, document.Print.Copies);
        int rows = AcrossLayout.Rows(copies, columns);

        var warnings = new List<string>(info.Warnings);

        // A row is one pull of the media and cannot be part printed, so asking for 10 on
        // 3-across stock produces 12. Better said than discovered on the roll.
        int produced = AcrossLayout.LabelsInRows(rows, columns);
        if (produced > copies)
        {
            warnings.Add(
                $"{columns} across: {copies} copies takes {rows} rows and prints {produced} labels.");
        }

        // Every label identical, or the printer doing the counting: one block, ^PQ and
        // ^SN carry the run. The block already holds every column.
        if (!info.UsesSoftwareCounter || (copies == 1 && columns == 1))
        {
            return new PrintJobResult(single, produced, info.UsesPrinterCounter, warnings);
        }

        int builtRows = Math.Min(rows, MaxSoftwareCopies);
        if (builtRows < rows)
        {
            warnings.Add(
                $"Built the first {AcrossLayout.LabelsInRows(builtRows, columns)} of {produced} labels: a run numbered by the PC is capped at {MaxSoftwareCopies} blocks.");
        }

        // One block per row, each a specific set of labels, so ^PQ must not repeat them.
        // A graphic shared across placements is downloaded by the first block only: the
        // whole run is one stream to one printer, and a ~DG stays in memory for the rest
        // of it, so repeating a logo per copy would be paying for it thousands of times.
        var sb = new StringBuilder(single.Length * builtRows);
        for (int row = 0; row < builtRows; row++)
        {
            sb.Append(generator.Generate(
                document,
                context with
                {
                    CopyIndex = row * columns,
                    EmitCopies = false,
                    IncludeGraphicDownloads = row == 0,
                }));
            sb.Append('\n');
        }

        return new PrintJobResult(
            sb.ToString(),
            AcrossLayout.LabelsInRows(builtRows, columns),
            CountedByPrinter: false,
            warnings);
    }
}
