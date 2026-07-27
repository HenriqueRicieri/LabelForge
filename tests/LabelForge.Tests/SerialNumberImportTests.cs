using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// Reading ^SN, the printer's own serialization, back into a counter variable.
///
/// ^SN stands where ^FD would and carries the field's data itself, so the case that
/// matters most is the one that looks like nothing: a serialized field read without it
/// produced no element at all, not merely an unnumbered one.
/// </summary>
public sealed class SerialNumberImportTests
{
    private static LabelDocument Counted(
        long start, long step, int padding, string text = "##LOTE##")
    {
        var document = new LabelDocument { WidthMm = 60, HeightMm = 40, Dpmm = 8 };
        document.Elements.Add(new TextElement
        {
            X = 40, Y = 50, Text = text, FontHeightDots = 40,
        });
        document.Variables["LOTE"] = new VariableDefinition
        {
            Kind = VariableKind.Counter,
            CounterStart = start,
            CounterStep = step,
            CounterPadding = padding,
            UsePrinterCounter = true,
        };

        return document;
    }

    /// <summary>
    /// The load-bearing one. A counter is the only thing the generator writes that is not
    /// a ^FD field, so without this the round trip had a hole in it exactly where C3 put
    /// its fastest print path.
    ///
    /// The variable's NAME is not expected to survive: it is never stated in the ZPL, only
    /// the value it produced, so the import gives it one of its own. The bytes are what
    /// have to match.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 3)]
    [InlineData(5, 2, 4)]
    [InlineData(7, 1, 0)]
    [InlineData(250, -5, 6)]
    [InlineData(0, 1, 2)]
    public void RoundTripOfAPrinterCounter_IsByteIdentical(long start, long step, int padding)
    {
        LabelDocument original = Counted(start, step, padding);
        string first = new ZplGenerator().Generate(original);
        Assert.Contains("^SN", first, StringComparison.Ordinal);

        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(first, dpmm: 8);

        Assert.Empty(imported.Warnings);
        Assert.Equal(first, new ZplGenerator().Generate(imported.Document));
    }

    /// <summary>Static text in front of the marker rides inside ^SN's own value, so the
    /// split has to put it back where it came from.</summary>
    [Fact]
    public void RoundTripOfACounterBehindStaticText_IsByteIdentical()
    {
        LabelDocument original = Counted(12, 1, 5, "LOTE ##LOTE##");
        string first = new ZplGenerator().Generate(original);
        Assert.Contains("^SNLOTE 00012,1,Y", first, StringComparison.Ordinal);

        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(first, dpmm: 8);

        Assert.Equal(first, new ZplGenerator().Generate(imported.Document));
        var text = Assert.IsType<TextElement>(Assert.Single(imported.Document.Elements));
        Assert.Equal("LOTE ##SERIAL##", text.Text);
    }

    /// <summary>The field data lives in ^SN, so an unread one takes the whole field with
    /// it. This is the regression that names the bug rather than the feature.</summary>
    [Fact]
    public void ASerializedField_ProducesTheElementItsDataBelongsTo()
    {
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(
            "^XA^PW480^LL320^FO40,50^A0N,40^SN001,1,Y^FS^XZ", dpmm: 8);

        var text = Assert.IsType<TextElement>(Assert.Single(imported.Document.Elements));
        Assert.Equal("##SERIAL##", text.Text);
        Assert.Empty(imported.Warnings);
    }

    /// <summary>^SN serializes barcode fields as well as text ones, and the element is
    /// whichever type the field already had in force.</summary>
    [Fact]
    public void ASerializedBarcode_KeepsItsSymbology()
    {
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(
            "^XA^PW480^LL320^FO40,50^BY3^BCN,80,Y,N,N^SNBOX0100,1,Y^FS^XZ", dpmm: 8);

        var barcode = Assert.IsType<BarcodeElement>(Assert.Single(imported.Document.Elements));
        Assert.Equal(BarcodeSymbology.Code128, barcode.Symbology);
        Assert.Equal("BOX##SERIAL##", barcode.Data);

        VariableDefinition counter = imported.Document.Variables["SERIAL"];
        Assert.Equal(100, counter.CounterStart);
        Assert.Equal(4, counter.CounterPadding);
    }

    /// <summary>Value, step and padding, taken from ZPL's own parameters. The manual's
    /// defaults apply per parameter, because an imported label has to keep printing what
    /// it printed.</summary>
    [Theory]
    [InlineData("^SN001,1,Y", "", "", 1, 1, 3)]
    [InlineData("^SN42,5,N", "", "", 42, 5, 0)]
    [InlineData("^SN0100,-1,Y", "", "", 100, -1, 4)]
    [InlineData("^SN", "", "", 1, 1, 0)]
    [InlineData("^SN,7,Y", "", "", 1, 7, 1)]
    [InlineData("^SNLOT-0007,1,Y", "LOT-", "", 7, 1, 4)]
    [InlineData("^SN0012AB,1,Y", "", "AB", 12, 1, 4)]
    [InlineData("^SNA1B0025CD,2,Y", "A1B", "CD", 25, 2, 4)]
    public void TheIndexedDigitsAreTheRightMostRun(
        string command, string prefix, string suffix, long start, long step, int padding)
    {
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(
            $"^XA^PW480^LL320^FO40,50^A0N,40{command}^FS^XZ", dpmm: 8);

        var text = Assert.IsType<TextElement>(Assert.Single(imported.Document.Elements));
        Assert.Equal($"{prefix}##SERIAL##{suffix}", text.Text);

        VariableDefinition counter = imported.Document.Variables["SERIAL"];
        Assert.Equal(VariableKind.Counter, counter.Kind);
        Assert.Equal(start, counter.CounterStart);
        Assert.Equal(step, counter.CounterStep);
        Assert.Equal(padding, counter.CounterPadding);
        Assert.True(counter.UsePrinterCounter);
    }

    /// <summary>
    /// A maximum of twelve right-most digits are subject to indexing, so a longer run is
    /// literal text in front of a twelve-digit counter. Reading it whole would carry digits
    /// the printer never touches.
    ///
    /// This one does not come back as ^SN, and the reason is worth stating rather than
    /// hiding: C3's eligibility rule measures the digits at the end of the whole FIELD, so
    /// sixteen of them puts it outside what it will hand to the printer even though the
    /// printer would index only the last twelve. The field still prints the value it came
    /// in with, character for character, which is what the split has to get right.
    /// </summary>
    [Fact]
    public void OnlyTheRightMostTwelveDigitsAreIndexed()
    {
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(
            "^XA^PW480^LL320^FO40,50^A0N,40^SN0012345678901234,1,Y^FS^XZ", dpmm: 8);

        var text = Assert.IsType<TextElement>(Assert.Single(imported.Document.Elements));
        Assert.Equal("0012##SERIAL##", text.Text);
        Assert.Equal(345678901234, imported.Document.Variables["SERIAL"].CounterStart);
        Assert.Equal(12, imported.Document.Variables["SERIAL"].CounterPadding);
        Assert.Contains(
            "^FD0012345678901234^FS",
            new ZplGenerator().Generate(imported.Document),
            StringComparison.Ordinal);
    }

    /// <summary>A value with no digits anywhere is printed unchanged on every copy, which
    /// is ordinary field data and no counter at all.</summary>
    [Fact]
    public void AValueWithNoDigits_IsOrdinaryFieldData()
    {
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(
            "^XA^PW480^LL320^FO40,50^A0N,40^SNAMOSTRA,1,Y^FS^XZ", dpmm: 8);

        var text = Assert.IsType<TextElement>(Assert.Single(imported.Document.Elements));
        Assert.Equal("AMOSTRA", text.Text);
        Assert.Empty(imported.Document.Variables);
    }

    /// <summary>Two ^SN fields started at the same number with the same step advance in
    /// lockstep, so they are one counter seen twice. Different numbers are two counters
    /// and get two names.</summary>
    [Fact]
    public void FieldsCountingInLockstep_ShareOneVariable()
    {
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(
            "^XA^PW480^LL320"
            + "^FO40,50^A0N,40^SN001,1,Y^FS"
            + "^FO40,120^A0N,40^SNLOTE 001,1,Y^FS"
            + "^FO40,190^A0N,40^SN500,2,Y^FS^XZ",
            dpmm: 8);

        Assert.Equal(
            ["##SERIAL##", "LOTE ##SERIAL##", "##SERIAL2##"],
            imported.Document.Elements.Cast<TextElement>().Select(e => e.Text));
        Assert.Equal(["SERIAL", "SERIAL2"], imported.Document.Variables.Keys.Order());
        Assert.Equal(500, imported.Document.Variables["SERIAL2"].CounterStart);
    }

    /// <summary>A recovered counter must not take a marker name the file already writes,
    /// wherever in the file it is written: a field further down is just as much a collision
    /// as one already read.</summary>
    [Fact]
    public void ARecoveredCounterDoesNotTakeANameTheFileAlreadyUses()
    {
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(
            "^XA^PW480^LL320"
            + "^FO40,50^A0N,40^SN001,1,Y^FS"
            + "^FO40,120^A0N,40^FD##SERIAL##^FS^XZ",
            dpmm: 8);

        Assert.Equal("##SERIAL2##", ((TextElement)imported.Document.Elements[0]).Text);
        Assert.Equal(["SERIAL2"], imported.Document.Variables.Keys);
    }

    /// <summary>
    /// A ^SN whose value carries trailing text still becomes a counter, and it stops being
    /// the printer's: the printer advances the end of the field, so a suffix rules ^SN out
    /// on the way back and the run is numbered here instead.
    ///
    /// The label prints the same either way. What changes is the shape of the job, which
    /// DynamicField already states on every render, so the import does not repeat it.
    /// </summary>
    [Fact]
    public void ACounterFollowedByText_FallsBackToNumberingHere()
    {
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(
            "^XA^PW480^LL320^FO40,50^A0N,40^SN0012AB,1,Y^FS^XZ", dpmm: 8);

        var generator = new ZplGenerator();
        string regenerated = generator.Generate(imported.Document);

        Assert.DoesNotContain("^SN", regenerated, StringComparison.Ordinal);
        Assert.Contains("^FD0012AB^FS", regenerated, StringComparison.Ordinal);
        Assert.Contains(
            generator.LastRun.Warnings,
            w => w.Contains("nothing may follow the marker", StringComparison.Ordinal));
    }

    /// <summary>Counters belong to the fields that name them, so a block that was not
    /// opened does not leave its variables on the document.</summary>
    [Fact]
    public void OnlyTheOpenedBlocksCountersReachTheDocument()
    {
        const string zpl =
            "^XA^PW480^LL320^FO40,50^A0N,40^SN001,1,Y^FS^XZ"
            + "^XA^PW480^LL320^FO40,50^A0N,40^SN900,3,N^FS^XZ";

        ZplDocumentImportResult second = ZplDocumentImport.FromZpl(zpl, dpmm: 8, labelIndex: 1);

        VariableDefinition counter = Assert.Single(second.Document.Variables).Value;
        Assert.Equal(900, counter.CounterStart);
        Assert.Equal(3, counter.CounterStep);
        Assert.Equal(0, counter.CounterPadding);
    }
}
