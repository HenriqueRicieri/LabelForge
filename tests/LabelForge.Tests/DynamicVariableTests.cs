using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// Counter and date/time variables: what reaches the printer and, just as importantly,
/// when we hand the work to the printer versus doing it ourselves. The golden strings
/// pin the exact ^SN and ^FC syntax, and every fallback asserts its stated reason,
/// because a silent fallback turns one fast job into thousands of slow ones.
/// </summary>
public sealed class DynamicVariableTests
{
    /// <summary>A fixed instant so clock output is a golden value, not the wall clock.</summary>
    private static readonly DateTime Now = new(2026, 7, 24, 15, 4, 5, DateTimeKind.Unspecified);

    private const string Head = "^XA\n^CI28\n^PW800\n^LL1200\n^LH0,0\n";

    private static LabelDocument Doc(params Element[] elements)
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 150, Dpmm = 8 };
        foreach (Element element in elements)
        {
            doc.Elements.Add(element);
        }

        return doc;
    }

    private static LabelDocument TextDoc(string text, string name, VariableDefinition definition)
    {
        LabelDocument doc = Doc(new TextElement { X = 10, Y = 20, FontHeightDots = 30, Text = text });
        doc.Variables[name] = definition;
        return doc;
    }

    private static VariableDefinition Counter(
        long start = 1, long step = 1, int padding = 3, bool printer = true) => new()
        {
            Kind = VariableKind.Counter,
            CounterStart = start,
            CounterStep = step,
            CounterPadding = padding,
            UsePrinterCounter = printer,
        };

    private static VariableDefinition Clock(string format = "dd/MM/yyyy", bool printer = false) => new()
        {
            Kind = VariableKind.Clock,
            ClockFormat = format,
            UsePrinterClock = printer,
        };

    private static (string Zpl, GenerationInfo Run) Generate(LabelDocument document)
    {
        var generator = new ZplGenerator();
        string zpl = generator.Generate(document, new GenerationContext { Now = Now });
        return (zpl, generator.LastRun);
    }

    // ---- Printer-side serialization (^SN) ----------------------------------------

    [Fact]
    public void Counter_AloneInTheField_BecomesPrinterSerialization()
    {
        (string zpl, GenerationInfo run) = Generate(TextDoc("##SERIE##", "SERIE", Counter()));

        Assert.Equal(Head + "^FO10,20^A0N,30^SN001,1,Y^FS\n^XZ", zpl);
        Assert.True(run.UsesPrinterCounter);
        Assert.False(run.UsesSoftwareCounter);
        Assert.Empty(run.Warnings);
    }

    [Fact]
    public void Counter_WithStaticPrefix_KeepsThePrefixInsideTheSerialValue()
    {
        (string zpl, GenerationInfo run) = Generate(TextDoc("LOTE-##SERIE##", "SERIE", Counter()));

        Assert.Equal(Head + "^FO10,20^A0N,30^SNLOTE-001,1,Y^FS\n^XZ", zpl);
        Assert.True(run.UsesPrinterCounter);
    }

    [Fact]
    public void Counter_WithoutPadding_TellsThePrinterNotToPad()
    {
        (string zpl, _) = Generate(TextDoc("##SERIE##", "SERIE", Counter(padding: 0)));

        Assert.Equal(Head + "^FO10,20^A0N,30^SN1,1,N^FS\n^XZ", zpl);
    }

    [Fact]
    public void Counter_CountingDown_EmitsANegativeStep()
    {
        (string zpl, _) = Generate(TextDoc("##SERIE##", "SERIE", Counter(start: 500, step: -5)));

        Assert.Equal(Head + "^FO10,20^A0N,30^SN500,-5,Y^FS\n^XZ", zpl);
    }

    [Fact]
    public void Counter_OnABarcode_SerializesToo()
    {
        LabelDocument doc = Doc(new BarcodeElement
        {
            X = 30, Y = 40, Data = "##SERIE##", HeightDots = 80, ModuleWidthDots = 2,
        });
        doc.Variables["SERIE"] = Counter();

        (string zpl, GenerationInfo run) = Generate(doc);

        Assert.Equal(Head + "^BY2^FO30,40^BCN,80,Y,N,N^SN001,1,Y^FS\n^XZ", zpl);
        Assert.True(run.UsesPrinterCounter);
    }

    // ---- Falling back to counting here -------------------------------------------

    [Fact]
    public void Counter_WithTextAfterIt_CountsHereAndSaysWhy()
    {
        (string zpl, GenerationInfo run) = Generate(TextDoc("##SERIE## un", "SERIE", Counter()));

        Assert.Equal(Head + "^FO10,20^A0N,30^FD001 un^FS\n^XZ", zpl);
        Assert.False(run.UsesPrinterCounter);
        Assert.True(run.UsesSoftwareCounter);
        Assert.Contains("nothing may follow the marker", Assert.Single(run.Warnings));
    }

    [Fact]
    public void Counter_InAFieldHoldingAComma_CountsHereAndSaysWhy()
    {
        (string zpl, GenerationInfo run) = Generate(TextDoc("A,##SERIE##", "SERIE", Counter()));

        Assert.Equal(Head + "^FO10,20^A0N,30^FDA,001^FS\n^XZ", zpl);
        Assert.Contains("comma", Assert.Single(run.Warnings));
    }

    [Fact]
    public void Counter_SharingAFieldWithAnotherMarker_CountsHereAndSaysWhy()
    {
        LabelDocument doc = TextDoc("##LOTE##-##SERIE##", "SERIE", Counter());

        (string zpl, GenerationInfo run) = Generate(doc);

        // The external marker survives verbatim; only the counter resolves.
        Assert.Equal(Head + "^FO10,20^A0N,30^FD##LOTE##-001^FS\n^XZ", zpl);
        Assert.Contains("more than one marker", Assert.Single(run.Warnings));
    }

    [Fact]
    public void Counter_WithThePrinterOptionOff_CountsHereWithoutComplaining()
    {
        (string zpl, GenerationInfo run) = Generate(
            TextDoc("##SERIE##", "SERIE", Counter(printer: false)));

        Assert.Equal(Head + "^FO10,20^A0N,30^FD001^FS\n^XZ", zpl);
        Assert.True(run.UsesSoftwareCounter);
        Assert.Empty(run.Warnings);
    }

    [Fact]
    public void Counter_OnAQrCode_CountsHere_BecauseTheModePrefixHoldsAComma()
    {
        LabelDocument doc = Doc(new QrCodeElement { X = 5, Y = 6, Data = "##SERIE##", Magnification = 4 });
        doc.Variables["SERIE"] = Counter();

        (string zpl, GenerationInfo run) = Generate(doc);

        Assert.Equal(Head + "^FO5,6^BQN,2,4^FDMA,001^FS\n^XZ", zpl);
        Assert.True(run.UsesSoftwareCounter);
    }

    [Fact]
    public void Counter_AtCopyIndex_AdvancesByTheStep()
    {
        LabelDocument doc = TextDoc("##SERIE##", "SERIE", Counter(start: 10, step: 5, printer: false));
        var generator = new ZplGenerator();

        string zpl = generator.Generate(doc, new GenerationContext { Now = Now, CopyIndex = 3 });

        Assert.Contains("^FD025^FS", zpl);
    }

    // ---- Clock variables ----------------------------------------------------------

    [Fact]
    public void Clock_ByDefault_StampsThisMachinesTime()
    {
        (string zpl, GenerationInfo run) = Generate(TextDoc("##DATA##", "DATA", Clock()));

        Assert.Equal(Head + "^FO10,20^A0N,30^FD24/07/2026^FS\n^XZ", zpl);
        Assert.False(run.UsesPrinterClock);
    }

    [Fact]
    public void Clock_WithThePrinterClock_EmitsFieldClockPlaceholders()
    {
        (string zpl, GenerationInfo run) = Generate(
            TextDoc("##DATA##", "DATA", Clock("dd/MM/yyyy HH:mm", printer: true)));

        Assert.Equal(Head + "^FO10,20^A0N,30^FC%^FD%d/%m/%Y %H:%M^FS\n^XZ", zpl);
        Assert.True(run.UsesPrinterClock);
        Assert.Empty(run.Warnings);
    }

    [Fact]
    public void Clock_WithAFormatThePrinterCannotExpress_FallsBackAndSaysWhy()
    {
        (string zpl, GenerationInfo run) = Generate(
            TextDoc("##DATA##", "DATA", Clock("dd MMM yyyy", printer: true)));

        Assert.Equal(Head + "^FO10,20^A0N,30^FD24 Jul 2026^FS\n^XZ", zpl);
        Assert.False(run.UsesPrinterClock);
        Assert.Contains("cannot express", Assert.Single(run.Warnings));
    }

    [Fact]
    public void Clock_InAFieldHoldingAPercentSign_FallsBackAndSaysWhy()
    {
        (string zpl, GenerationInfo run) = Generate(
            TextDoc("100% ##DATA##", "DATA", Clock(printer: true)));

        Assert.Equal(Head + "^FO10,20^A0N,30^FD100% 24/07/2026^FS\n^XZ", zpl);
        Assert.False(run.UsesPrinterClock);
        Assert.Contains("read as a clock code", Assert.Single(run.Warnings));
    }

    [Fact]
    public void Clock_KeepsSurroundingTextAndCombinesWithAnExternalMarker()
    {
        LabelDocument doc = TextDoc("Val: ##DATA## / ##LOTE##", "DATA", Clock());

        (string zpl, _) = Generate(doc);

        Assert.Equal(Head + "^FO10,20^A0N,30^FDVal: 24/07/2026 / ##LOTE##^FS\n^XZ", zpl);
    }

    // ---- What must not change -----------------------------------------------------

    [Fact]
    public void ExternalMarkers_StillReachTheOutputVerbatim()
    {
        LabelDocument doc = TextDoc(
            "##CODIGO## ##@REGION(1)##", "CODIGO",
            new VariableDefinition { Kind = VariableKind.External });

        (string zpl, GenerationInfo run) = Generate(doc);

        Assert.Equal(Head + "^FO10,20^A0N,30^FD##CODIGO## ##@REGION(1)##^FS\n^XZ", zpl);
        Assert.False(run.UsesSoftwareCounter);
        Assert.False(run.UsesPrinterCounter);
    }

    [Fact]
    public void MarkersWithUnderscores_SurviveVerbatim()
    {
        // Field names are full of underscores and ZPL reserves '_' for its own hex
        // escape, so this used to emit ##FILIAL_5FDOCUMENTO##. That was wrong, and the
        // real files say so: they write "^FH^FD MA,##CODIGO_BARRAS##" with the marker
        // untouched. A marker is not data for the printer, it is a placeholder the
        // filling system replaces first, and escaping it hands that system a name it
        // cannot match. With nothing else in the field needing an escape, no ^FH is
        // emitted at all.
        LabelDocument doc = TextDoc(
            "##FILIAL_DOCUMENTO##", "FILIAL_DOCUMENTO",
            new VariableDefinition { Kind = VariableKind.External });

        (string zpl, _) = Generate(doc);

        Assert.Equal(Head + "^FO10,20^A0N,30^FD##FILIAL_DOCUMENTO##^FS\n^XZ", zpl);
    }

    /// <summary>Literal text around a marker is still escaped; only the marker is
    /// exempt, so the two rules do not blur into each other.</summary>
    [Fact]
    public void LiteralTextAroundAMarker_IsStillEscaped()
    {
        LabelDocument doc = TextDoc(
            "N_ ##FILIAL_DOCUMENTO## ^x", "FILIAL_DOCUMENTO",
            new VariableDefinition { Kind = VariableKind.External });

        (string zpl, _) = Generate(doc);

        Assert.Contains("^FH_^FDN_5F ##FILIAL_DOCUMENTO## _5Ex^FS", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_KeepsMarkersLiteral_SoTheDesignerCanSubstituteSamples()
    {
        LabelDocument doc = TextDoc("##SERIE##", "SERIE", Counter());

        string zpl = new ZplGenerator().GeneratePreview(doc, offsetDots: 0);

        Assert.Contains("^FD##SERIE##^FS", zpl);
        Assert.DoesNotContain("^SN", zpl);
    }

    [Fact]
    public void Preview_ResolvesCountersAndClocksToRenderableValues()
    {
        LabelDocument doc = TextDoc("##SERIE##", "SERIE", Counter(start: 7, padding: 4));
        doc.Variables["DATA"] = Clock("yyyy-MM-dd");
        doc.SampleValues["LOTE"] = "AB-12";

        Assert.Equal("0007", VariableValues.ForPreview(doc, "SERIE", Now));
        Assert.Equal("2026-07-24", VariableValues.ForPreview(doc, "DATA", Now));
        Assert.Equal("AB-12", VariableValues.ForPreview(doc, "LOTE", Now));
        Assert.Null(VariableValues.ForPreview(doc, "SEM_AMOSTRA", Now));
    }

    // ---- Print jobs ----------------------------------------------------------------

    [Fact]
    public void PrintJob_WithAPrinterCounter_StaysOneBlockWithACopyCount()
    {
        LabelDocument doc = TextDoc("##SERIE##", "SERIE", Counter());
        doc.Print.Copies = 250;

        PrintJobResult job = PrintJob.Build(doc, Now);

        Assert.Equal(1, CountBlocks(job.Zpl));
        Assert.Contains("^PQ250", job.Zpl);
        Assert.Contains("^SN001,1,Y", job.Zpl);
        Assert.Equal(250, job.Labels);
        Assert.True(job.CountedByPrinter);
    }

    [Fact]
    public void PrintJob_CountingHere_EmitsOneBlockPerCopyAndNoCopyCount()
    {
        LabelDocument doc = TextDoc("##SERIE##", "SERIE", Counter(printer: false));
        doc.Print.Copies = 3;

        PrintJobResult job = PrintJob.Build(doc, Now);

        Assert.Equal(3, CountBlocks(job.Zpl));
        Assert.DoesNotContain("^PQ", job.Zpl);
        Assert.Contains("^FD001^FS", job.Zpl);
        Assert.Contains("^FD002^FS", job.Zpl);
        Assert.Contains("^FD003^FS", job.Zpl);
        Assert.Equal(3, job.Labels);
        Assert.False(job.CountedByPrinter);
    }

    [Fact]
    public void PrintJob_CountingHereForASingleCopy_IsStillOneBlock()
    {
        LabelDocument doc = TextDoc("##SERIE##", "SERIE", Counter(printer: false));
        doc.Print.Copies = 1;

        PrintJobResult job = PrintJob.Build(doc, Now);

        Assert.Equal(1, CountBlocks(job.Zpl));
        Assert.DoesNotContain("^PQ", job.Zpl);
        Assert.Equal(1, job.Labels);
    }

    [Fact]
    public void PrintJob_WithoutVariables_IsUnchangedFromASimpleGenerate()
    {
        LabelDocument doc = Doc(new TextElement { X = 1, Y = 2, Text = "Fixed" });
        doc.Print.Copies = 4;

        PrintJobResult job = PrintJob.Build(doc, Now);

        Assert.Equal(new ZplGenerator().Generate(doc), job.Zpl);
        Assert.Equal(4, job.Labels);
        Assert.Empty(job.Warnings);
    }

    [Fact]
    public void PrintJob_CarriesTheGeneratorsFallbackWarnings()
    {
        LabelDocument doc = TextDoc("##SERIE## un", "SERIE", Counter());
        doc.Print.Copies = 2;

        PrintJobResult job = PrintJob.Build(doc, Now);

        Assert.Equal(2, CountBlocks(job.Zpl));
        Assert.Contains("nothing may follow the marker", Assert.Single(job.Warnings));
    }

    private static int CountBlocks(string zpl) =>
        zpl.Split("^XA", StringSplitOptions.RemoveEmptyEntries).Length;

    // ---- Persistence ----------------------------------------------------------------

    [Fact]
    public void Definitions_RoundTripThroughTheDocumentFormat()
    {
        LabelDocument doc = TextDoc("##SERIE##", "SERIE", Counter(start: 42, step: 2, padding: 5));
        doc.Variables["DATA"] = Clock("yyyy-MM-dd", printer: true);

        LabelDocument reloaded = LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(doc));

        VariableDefinition counter = reloaded.Variables["SERIE"];
        Assert.Equal(VariableKind.Counter, counter.Kind);
        Assert.Equal(42, counter.CounterStart);
        Assert.Equal(2, counter.CounterStep);
        Assert.Equal(5, counter.CounterPadding);
        Assert.True(counter.UsePrinterCounter);

        VariableDefinition clock = reloaded.Variables["DATA"];
        Assert.Equal(VariableKind.Clock, clock.Kind);
        Assert.Equal("yyyy-MM-dd", clock.ClockFormat);
        Assert.True(clock.UsePrinterClock);
    }

    [Fact]
    public void DocumentsSavedBeforeVariablesExisted_LoadWithNoDefinitions()
    {
        const string legacy = """
        {
          "SchemaVersion": 1,
          "Document": {
            "WidthMm": 100,
            "HeightMm": 150,
            "Dpmm": 8,
            "Elements": [
              { "$type": "text", "X": 10, "Y": 20, "FontHeightDots": 30, "Text": "##SERIE##" }
            ],
            "SampleValues": { "SERIE": "0001" }
          }
        }
        """;

        LabelDocument doc = LabelDocumentJson.Deserialize(legacy);

        Assert.Empty(doc.Variables);
        Assert.Equal("0001", doc.SampleValues["SERIE"]);
        // With no definition the marker is still the template's, so it prints as typed.
        Assert.Contains("^FD##SERIE##^FS", new ZplGenerator().Generate(doc));
    }
}
