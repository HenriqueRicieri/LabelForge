using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// The print job as an exportable artefact: exactly the bytes a print would send, for a
/// support ticket, a diff against what a printer received, or a fixture to test against.
/// A file that merely resembles the job is worse than no file, because it gets trusted.
/// </summary>
public sealed class PrintJobExportTests
{
    private static LabelDocument Counting(int copies, bool printerCounts)
    {
        var document = new LabelDocument { WidthMm = 60, HeightMm = 40, Dpmm = 8 };
        document.Elements.Add(new TextElement
        {
            X = 10, Y = 10, Text = "##SERIE##", FontHeightDots = 30,
        });
        document.Variables["SERIE"] = new VariableDefinition
        {
            Kind = VariableKind.Counter,
            CounterStart = 41,
            CounterPadding = 4,
            UsePrinterCounter = printerCounts,
        };
        document.Print.Copies = copies;
        return document;
    }

    /// <summary>
    /// The case the export exists for. A counter this machine expands cannot be a
    /// quantity, so the run is one block per copy, and the label the ZPL pane shows is
    /// only the first of them.
    /// </summary>
    [Fact]
    public void ARunThisMachineNumbers_ExportsEveryCopy()
    {
        LabelDocument document = Counting(copies: 3, printerCounts: false);

        PrintJobResult job = PrintJob.Build(document, DateTime.Now);
        string singleLabel = new ZplGenerator().Generate(document);

        Assert.Equal(3, job.Labels);
        Assert.False(job.CountedByPrinter);
        Assert.Equal(3, Blocks(job.Zpl));
        Assert.Equal(1, Blocks(singleLabel));

        // Each copy carries its own number, which is the thing a single label cannot show.
        Assert.Contains("0041", job.Zpl, StringComparison.Ordinal);
        Assert.Contains("0042", job.Zpl, StringComparison.Ordinal);
        Assert.Contains("0043", job.Zpl, StringComparison.Ordinal);

        // And no quantity, because repeating blocks that already differ would print nine.
        Assert.DoesNotContain("^PQ", job.Zpl, StringComparison.Ordinal);
    }

    /// <summary>When the printer serializes the run, the job is one block and a quantity,
    /// so the export matches the label plus ^PQ rather than repeating anything.</summary>
    [Fact]
    public void ARunThePrinterNumbers_ExportsOneBlockAndAQuantity()
    {
        PrintJobResult job = PrintJob.Build(Counting(copies: 3, printerCounts: true), DateTime.Now);

        Assert.True(job.CountedByPrinter);
        Assert.Equal(1, Blocks(job.Zpl));
        Assert.Contains("^SN", job.Zpl, StringComparison.Ordinal);
        Assert.Contains("^PQ3", job.Zpl, StringComparison.Ordinal);
    }

    /// <summary>An ordinary label has nothing to expand, so the two exports agree and the
    /// distinction costs nobody anything.</summary>
    [Fact]
    public void AnOrdinaryLabel_ExportsWhatTheZplPaneShows()
    {
        var document = new LabelDocument { WidthMm = 60, HeightMm = 40, Dpmm = 8 };
        document.Elements.Add(new TextElement { X = 10, Y = 10, Text = "Plain", FontHeightDots = 30 });

        PrintJobResult job = PrintJob.Build(document, DateTime.Now);

        Assert.Equal(new ZplGenerator().Generate(document), job.Zpl);
        Assert.Equal(1, job.Labels);
    }

    /// <summary>A run capped below the requested copies says so, and the export carries
    /// that message: the file is what someone reads later, when nobody is at the screen
    /// the warning first appeared on.</summary>
    [Fact]
    public void ACappedRun_ReportsWhatItLeftOut()
    {
        LabelDocument document = Counting(copies: PrintJob.MaxSoftwareCopies + 5, printerCounts: false);

        PrintJobResult job = PrintJob.Build(document, DateTime.Now);

        Assert.Equal(PrintJob.MaxSoftwareCopies, job.Labels);
        Assert.Contains(job.Warnings, w => w.Contains("capped", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A clock stamped by this machine takes its value when the job is built,
    /// which is why the export builds one rather than reusing what the canvas last drew.</summary>
    [Fact]
    public void AClock_IsStampedWhenTheJobIsBuilt()
    {
        var document = new LabelDocument { WidthMm = 60, HeightMm = 40, Dpmm = 8 };
        document.Elements.Add(new TextElement
        {
            X = 10, Y = 10, Text = "##EMISSAO##", FontHeightDots = 30,
        });
        document.Variables["EMISSAO"] = new VariableDefinition
        {
            Kind = VariableKind.Clock,
            ClockFormat = "yyyy-MM-dd HH:mm:ss",
            UsePrinterClock = false,
        };

        var early = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
        var later = new DateTime(2026, 7, 8, 9, 10, 11, DateTimeKind.Local);

        Assert.Contains(
            "2026-01-02 03:04:05", PrintJob.Build(document, early).Zpl, StringComparison.Ordinal);
        Assert.Contains(
            "2026-07-08 09:10:11", PrintJob.Build(document, later).Zpl, StringComparison.Ordinal);
    }

    private static int Blocks(string zpl)
    {
        int count = 0;
        int at = zpl.IndexOf("^XA", StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = zpl.IndexOf("^XA", at + 3, StringComparison.Ordinal);
        }

        return count;
    }
}
