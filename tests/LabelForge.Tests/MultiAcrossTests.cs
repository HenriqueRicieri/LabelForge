using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Printers;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// Multi-across stock: two or more of the same label side by side on the web.
///
/// The load-bearing idea is that this is a property of the RUN and not of the design.
/// One label is drawn, edited and measured; a print repeats it across the roll. So the
/// tests here come in two halves: everything about a single label has to be unchanged,
/// byte for byte, and everything about the job has to lay the columns out where the
/// stock actually holds them.
/// </summary>
public sealed class MultiAcrossTests
{
    private const int Dpmm = 8;

    private static LabelDocument Stock(int across, double gapMm = 3, double widthMm = 25)
    {
        var document = new LabelDocument
        {
            WidthMm = widthMm,
            HeightMm = 20,
            Dpmm = Dpmm,
            LabelsAcross = across,
            AcrossGapMm = gapMm,
        };
        document.Elements.Add(new BoxElement
        {
            X = 10, Y = 10, WidthDots = 60, HeightDots = 40, ThicknessDots = 4,
        });
        return document;
    }

    // ---- the arithmetic, in one place so nothing can disagree with it ----

    [Theory]
    [InlineData(1, 25, 3, 25)]
    [InlineData(2, 25, 3, 53)]
    [InlineData(3, 25, 3, 81)]
    [InlineData(4, 12.7, 3.18, 60.34)]
    [InlineData(2, 40, 2, 82)]
    public void WebWidth_IsEveryColumnAndTheGapsBetweenThem(
        int across, double widthMm, double gapMm, double expectedMm)
    {
        LabelDocument document = Stock(across, gapMm, widthMm);

        Assert.Equal(expectedMm, AcrossLayout.WebWidthMm(document), 3);
        Assert.Equal(expectedMm, document.WebWidthMm, 3);
    }

    /// <summary>A row is one pull of the media and cannot be part printed, so a quantity
    /// that does not divide by the column count rounds up and prints a few extra. Saying
    /// so is the whole point of computing it here rather than discovering it on the roll.</summary>
    [Theory]
    [InlineData(10, 1, 10, 10)]
    [InlineData(10, 3, 4, 12)]
    [InlineData(12, 3, 4, 12)]
    [InlineData(1, 4, 1, 4)]
    [InlineData(0, 3, 1, 3)]
    public void Rows_RoundUpAndSayHowManyLabelsThatIs(
        int copies, int columns, int expectedRows, int expectedLabels)
    {
        int rows = AcrossLayout.Rows(copies, columns);

        Assert.Equal(expectedRows, rows);
        Assert.Equal(expectedLabels, AcrossLayout.LabelsInRows(rows, columns));
    }

    // ---- the single label, which must not move ----

    /// <summary>
    /// The design is one label whatever the stock carries, so the ZPL pane, the file
    /// export and the round trip see exactly the bytes they saw before the web existed.
    /// This is what keeps generate -> parse -> generate byte-identical: the importer has
    /// no way to tell three columns of one design from three designs.
    /// </summary>
    [Fact]
    public void Generate_IsByteIdenticalWhateverTheStockCarries()
    {
        string ordinary = new ZplGenerator().Generate(Stock(across: 1));
        string web = new ZplGenerator().Generate(Stock(across: 4));

        Assert.Equal(ordinary, web);
    }

    [Fact]
    public void Generate_RoundTripsThroughTheImporterUnchanged()
    {
        LabelDocument document = Stock(across: 3);
        string first = new ZplGenerator().Generate(document);

        LabelDocument reopened = ZplDocumentImport.FromZpl(first, Dpmm).Document;

        Assert.Equal(first, new ZplGenerator().Generate(reopened));
    }

    /// <summary>The canvas draws the label being designed, not the roll it is cut from,
    /// so the preview keeps its one column even when the run would print four.</summary>
    [Fact]
    public void GeneratePreview_StaysOneLabel()
    {
        string preview = new ZplGenerator().GeneratePreview(Stock(across: 4), offsetDots: 40);

        Assert.Equal(1, Occurrences(preview, "^GB"));
    }

    // ---- the run ----

    [Fact]
    public void PrintJob_RepeatsTheDesignAtTheStockPitch()
    {
        LabelDocument document = Stock(across: 3);
        document.Print.Copies = 12;

        PrintJobResult job = PrintJob.Build(document, DateTime.Now);

        // 25 mm plus a 3 mm gap is 224 dots at 8 dpmm, so the columns start at 10, 234, 458.
        Assert.Equal(224, AcrossLayout.PitchDots(document));
        Assert.Contains("^FO10,10^GB", job.Zpl, StringComparison.Ordinal);
        Assert.Contains("^FO234,10^GB", job.Zpl, StringComparison.Ordinal);
        Assert.Contains("^FO458,10^GB", job.Zpl, StringComparison.Ordinal);
        Assert.Equal(3, Occurrences(job.Zpl, "^GB"));

        // The web is what the printhead is asked to cover: 200 + 224 + 224.
        Assert.Contains("^PW648", job.Zpl, StringComparison.Ordinal);

        // And ^PQ counts pulls of the media, not labels: 12 across three columns is four.
        Assert.Contains("^PQ4", job.Zpl, StringComparison.Ordinal);
        Assert.Equal(12, job.Labels);
        Assert.Equal(1, Blocks(job.Zpl));
    }

    /// <summary>
    /// The columns are baked X offsets rather than a ^LH shift per column, and the manual
    /// is what decided it: ^LH "must come before the first ^FS to be compatible with
    /// existing printers", and once issued it is "retained until you turn off the printer
    /// or send a new ^LH". A per-column ^LH would be both out of position and left behind
    /// for whatever prints next.
    /// </summary>
    [Fact]
    public void PrintJob_StatesLabelHomeOnceAndBeforeAnyField()
    {
        LabelDocument document = Stock(across: 4);

        string zpl = PrintJob.Build(document, DateTime.Now).Zpl;

        Assert.Equal(1, Occurrences(zpl, "^LH"));
        Assert.True(zpl.IndexOf("^LH", StringComparison.Ordinal)
                    < zpl.IndexOf("^FS", StringComparison.Ordinal));
    }

    /// <summary>A quantity that does not divide evenly is reported rather than silently
    /// overshot, because the extra labels are real stock coming off a real roll.</summary>
    [Fact]
    public void PrintJob_SaysWhenTheWebProducesMoreThanWasAskedFor()
    {
        LabelDocument document = Stock(across: 3);
        document.Print.Copies = 10;

        PrintJobResult job = PrintJob.Build(document, DateTime.Now);

        Assert.Equal(12, job.Labels);
        Assert.Contains("^PQ4", job.Zpl, StringComparison.Ordinal);
        Assert.Contains(job.Warnings, w => w.Contains("prints 12 labels", StringComparison.Ordinal));
    }

    [Fact]
    public void PrintJob_OnOrdinaryStockIsExactlyWhatItAlwaysWas()
    {
        LabelDocument document = Stock(across: 1);
        document.Print.Copies = 7;

        PrintJobResult job = PrintJob.Build(document, DateTime.Now);

        Assert.Equal(7, job.Labels);
        Assert.Empty(job.Warnings);
        Assert.Contains("^PQ7", job.Zpl, StringComparison.Ordinal);
        Assert.Equal(new ZplGenerator().Generate(document), job.Zpl);
    }

    // ---- counters, which are the part that could go silently wrong ----

    private static LabelDocument Counting(int across, int copies, bool printerCounts)
    {
        LabelDocument document = Stock(across);
        document.Elements.Add(new TextElement
        {
            X = 10, Y = 60, Text = "##SERIE##", FontHeightDots = 20,
        });
        document.Variables["SERIE"] = new VariableDefinition
        {
            Kind = VariableKind.Counter,
            CounterStart = 1,
            CounterStep = 1,
            CounterPadding = 4,
            UsePrinterCounter = printerCounts,
        };
        document.Print.Copies = copies;
        return document;
    }

    /// <summary>
    /// Every column of a row is a different label, so they have to number consecutively.
    /// Three columns sharing one number, then all three advancing together, is the failure
    /// this pins: the labels would print 1,1,1 then 2,2,2.
    /// </summary>
    [Fact]
    public void APrinterCounter_NumbersEachColumnOnItsOwn()
    {
        LabelDocument document = Counting(across: 3, copies: 9, printerCounts: true);

        PrintJobResult job = PrintJob.Build(document, DateTime.Now);

        // Row one prints 1, 2, 3; each field then steps three, so row two prints 4, 5, 6.
        Assert.Contains("^SN0001,3,Y", job.Zpl, StringComparison.Ordinal);
        Assert.Contains("^SN0002,3,Y", job.Zpl, StringComparison.Ordinal);
        Assert.Contains("^SN0003,3,Y", job.Zpl, StringComparison.Ordinal);
        Assert.Equal(3, Occurrences(job.Zpl, "^SN"));
        Assert.True(job.CountedByPrinter);

        // Still one block and one quantity: the fast path survives the web.
        Assert.Equal(1, Blocks(job.Zpl));
        Assert.Contains("^PQ3", job.Zpl, StringComparison.Ordinal);
        Assert.Equal(9, job.Labels);
    }

    /// <summary>The manual allows many serialized fields in one format, each indexing on
    /// its own, which is what makes the trick above legal. A single column has to keep
    /// emitting the plain step, or every existing counter label changes.</summary>
    [Fact]
    public void APrinterCounter_OnOrdinaryStockKeepsItsOwnStep()
    {
        LabelDocument document = Counting(across: 1, copies: 9, printerCounts: true);
        document.Variables["SERIE"].CounterStep = 5;

        string zpl = PrintJob.Build(document, DateTime.Now).Zpl;

        Assert.Contains("^SN0001,5,Y", zpl, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(zpl, "^SN"));
    }

    /// <summary>A counter numbered here bakes the value into the field, so a row of three
    /// carries three different blocks' worth of data in one block: the copy index walks
    /// across the columns and then down the rows.</summary>
    [Fact]
    public void ACounterThisMachineNumbers_WalksAcrossThenDown()
    {
        LabelDocument document = Counting(across: 3, copies: 6, printerCounts: false);

        PrintJobResult job = PrintJob.Build(document, DateTime.Now);

        Assert.Equal(2, Blocks(job.Zpl));
        Assert.Equal(6, job.Labels);
        foreach (string value in new[] { "0001", "0002", "0003", "0004", "0005", "0006" })
        {
            Assert.Contains(value, job.Zpl, StringComparison.Ordinal);
        }

        // One block per row, each already a specific set of labels, so no quantity.
        Assert.DoesNotContain("^PQ", job.Zpl, StringComparison.Ordinal);
    }

    // ---- the printhead, which is the thing a web can outgrow ----

    /// <summary>Three 40 mm labels are 126 mm of print on a 104 mm head, and each one of
    /// them looks perfectly printable on its own. The check has to measure the web.</summary>
    [Fact]
    public void APrinterProfile_MeasuresTheWebAndNotTheLabel()
    {
        PrinterProfile printer = PrinterCatalog.All.First(p => p.Id == "zd421-203");
        LabelDocument narrow = Stock(across: 1, gapMm: 3, widthMm: 40);
        LabelDocument web = Stock(across: 3, gapMm: 3, widthMm: 40);

        Assert.Empty(printer.Validate(narrow));
        Assert.Contains(
            printer.Validate(web),
            w => w.Contains("Web width", StringComparison.Ordinal)
                 && w.Contains("3 across", StringComparison.Ordinal));
    }

    // ---- and where the ink actually lands ----

    /// <summary>
    /// The measurement that matters, taken the way every other footprint here is: render
    /// the job and find the columns of ink. Arithmetic that agrees with itself proves
    /// nothing about where the printer puts the dots.
    /// </summary>
    [Fact]
    public void TheRenderedJob_PutsEachColumnAtTheStockPitch()
    {
        LabelDocument document = Stock(across: 3);
        double webMm = document.WebWidthMm;

        RenderResult result = new BinaryKitsRenderer()
            .Render(PrintJob.Build(document, DateTime.Now).Zpl, webMm, document.HeightMm, Dpmm);
        Assert.Empty(result.Errors);

        int[] columns = InkColumns(result);

        Assert.Equal(3, columns.Length);
        Assert.Equal(10, columns[0]);
        Assert.Equal(234, columns[1]);
        Assert.Equal(458, columns[2]);

        // The last column ends inside the web the ZPL declared, which is the whole reason
        // ^PW is widened rather than left at the label's own width.
        Assert.True(columns[2] + 60 <= Units.MmToDots(webMm, Dpmm));
    }

    /// <summary>Left edge of each run of inked columns in a rendered label.</summary>
    private static int[] InkColumns(RenderResult result)
    {
        using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
        Assert.NotNull(bitmap);

        var starts = new List<int>();
        bool inside = false;
        for (int x = 0; x < bitmap.Width; x++)
        {
            bool inked = false;
            for (int y = 0; y < bitmap.Height && !inked; y++)
            {
                inked = bitmap.GetPixel(x, y).Red < 128;
            }

            if (inked && !inside)
            {
                starts.Add(x);
            }

            inside = inked;
        }

        return [.. starts];
    }

    private static int Occurrences(string text, string token)
    {
        int count = 0;
        int at = text.IndexOf(token, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = text.IndexOf(token, at + token.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static int Blocks(string zpl) => Occurrences(zpl, "^XA");
}
