using System.Text;
using LabelForge.Core.Model;

namespace LabelForge.Core.Zpl;

/// <summary>The bytes a print run sends, and what they will produce.</summary>
/// <param name="Zpl">The complete job: one label block, or one per copy.</param>
/// <param name="Labels">How many labels the printer will produce.</param>
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
        var context = new GenerationContext { Now = now };
        string single = generator.Generate(document, context);
        GenerationInfo info = generator.LastRun;
        int copies = Math.Max(1, document.Print.Copies);

        // Every label identical, or the printer doing the counting: one block, ^PQ and
        // ^SN carry the run.
        if (!info.UsesSoftwareCounter || copies == 1)
        {
            return new PrintJobResult(single, copies, info.UsesPrinterCounter, info.Warnings);
        }

        var warnings = new List<string>(info.Warnings);
        int labels = Math.Min(copies, MaxSoftwareCopies);
        if (labels < copies)
        {
            warnings.Add(
                $"Built the first {labels} of {copies} labels: a run numbered by the PC is capped at {MaxSoftwareCopies}.");
        }

        // One block per copy, each a specific label, so ^PQ must not repeat them.
        var sb = new StringBuilder(single.Length * labels);
        for (int copy = 0; copy < labels; copy++)
        {
            sb.Append(generator.Generate(
                document, context with { CopyIndex = copy, EmitCopies = false }));
            sb.Append('\n');
        }

        return new PrintJobResult(sb.ToString(), labels, CountedByPrinter: false, warnings);
    }
}
