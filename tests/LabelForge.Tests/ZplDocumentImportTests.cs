using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// Reading a label back into the model. The load-bearing test is the round trip:
/// generate, parse, generate again, and the bytes have to match. It is worth more than
/// any number of per-command assertions, because it fails the moment the parser and the
/// generator disagree about anything at all, including things nobody thought to assert.
/// </summary>
public sealed class ZplDocumentImportTests
{
    private static LabelDocument Everything()
    {
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        document.Print.Copies = 3;
        document.Print.DarknessDelta = -4;
        document.Print.SpeedIps = 6;

        document.Elements.Add(new TextElement
        {
            X = 20, Y = 30, Text = "Plain text", FontHeightDots = 40,
        });
        document.Elements.Add(new TextElement
        {
            X = 20, Y = 80, Text = "Wide ##MARKER##", FontHeightDots = 30, FontWidthDots = 22,
            Orientation = Orientation.Rotated90,
        });
        document.Elements.Add(new TextElement
        {
            // The characters that force ^FH_ hex escaping on the way out.
            X = 20, Y = 130, Text = "caret ^ tilde ~ under _", FontHeightDots = 24,
        });
        document.Elements.Add(new BoxElement
        {
            X = 300, Y = 30, WidthDots = 200, HeightDots = 120, ThicknessDots = 4,
        });
        document.Elements.Add(new LineElement
        {
            X = 300, Y = 180, LengthDots = 240, ThicknessDots = 3,
        });
        document.Elements.Add(new LineElement
        {
            X = 560, Y = 30, IsVertical = true, LengthDots = 150, ThicknessDots = 5,
        });
        document.Elements.Add(new BoxElement
        {
            // The white erase real labels put in front of a graphic; read as black it
            // paints a slab over what it was there to reveal.
            X = 300, Y = 200, WidthDots = 120, HeightDots = 60, ThicknessDots = 60,
            IsWhite = true,
        });
        document.Elements.Add(new BarcodeElement
        {
            X = 20, Y = 200, Symbology = BarcodeSymbology.Code128, Data = "ABC-123",
            HeightDots = 90, ModuleWidthDots = 3, PrintInterpretationLine = true,
        });
        document.Elements.Add(new BarcodeElement
        {
            X = 20, Y = 320, Symbology = BarcodeSymbology.Code39, Data = "CODE39",
            HeightDots = 70, ModuleWidthDots = 2, WideBarRatio = 2.5,
            PrintInterpretationLine = false,
        });
        document.Elements.Add(new BarcodeElement
        {
            X = 300, Y = 320, Symbology = BarcodeSymbology.Ean13, Data = "5901234123457",
            HeightDots = 80, ModuleWidthDots = 2,
        });
        document.Elements.Add(new BarcodeElement
        {
            X = 500, Y = 320, Symbology = BarcodeSymbology.UpcA, Data = "036000291452",
            HeightDots = 80, ModuleWidthDots = 2,
        });
        document.Elements.Add(new QrCodeElement
        {
            X = 620, Y = 30, Data = "https://example.com/label", Magnification = 6,
            ErrorCorrection = QrErrorCorrection.Quartile,
        });
        document.Elements.Add(new DataMatrixElement
        {
            X = 620, Y = 200, Data = "LF-000123", ModuleSizeDots = 5,
            Orientation = Orientation.Rotated180,
        });
        document.Elements.Add(new Pdf417Element
        {
            X = 20, Y = 420, Data = "CARGO-000123", ModuleWidthDots = 3, RowHeightDots = 10,
            SecurityLevel = 4, DataColumns = 6, Orientation = Orientation.Rotated270,
        });
        document.Elements.Add(new Pdf417Element
        {
            // The automatic column count, which emits an empty argument the parser has
            // to read back as automatic rather than as zero columns.
            X = 300, Y = 420, Data = "AUTO SHAPE", DataColumns = 0, Truncate = true,
        });
        document.Elements.Add(new ImageElement
        {
            X = 620, Y = 380, ImageData = TestImages.HalfBlackPng(),
            SourcePixelWidth = 8, SourcePixelHeight = 1,
            WidthDots = 48, HeightDots = 24, Dithering = DitherMode.Threshold,
        });

        return document;
    }

    /// <summary>Every element type, both graphic forms, hex-escaped text, rotations and
    /// job settings, through a full cycle.</summary>
    [Fact]
    public void RoundTrip_OfEveryElementType_IsByteIdentical()
    {
        LabelDocument original = Everything();
        string first = new ZplGenerator().Generate(original);

        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(first, dpmm: 8);

        Assert.Empty(imported.Warnings);
        Assert.Equal(original.Elements.Count, imported.Document.Elements.Count);
        Assert.Equal(first, new ZplGenerator().Generate(imported.Document));
    }

    /// <summary>A repeated image generates ~DG plus ^XG instead of an inline field, so
    /// the parser has to resolve a download to get back to the same document.</summary>
    [Fact]
    public void RoundTrip_OfASharedGraphic_IsByteIdentical()
    {
        var document = new LabelDocument { WidthMm = 60, HeightMm = 40, Dpmm = 8 };
        foreach (int y in new[] { 20, 140, 260 })
        {
            document.Elements.Add(new ImageElement
            {
                X = 30, Y = y, ImageData = TestImages.HalfBlackPng(),
                SourcePixelWidth = 8, SourcePixelHeight = 1,
                WidthDots = 32, HeightDots = 16, Dithering = DitherMode.Threshold,
            });
        }

        string first = new ZplGenerator().Generate(document);
        Assert.Contains("~DG", first, StringComparison.Ordinal);

        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(first, dpmm: 8);

        Assert.Empty(imported.Warnings);
        Assert.Equal(3, imported.Document.Elements.Count);
        Assert.Equal(first, new ZplGenerator().Generate(imported.Document));
    }

    /// <summary>
    /// Every block's size is reported, not just the one handed back.
    ///
    /// One block is all a caller gets, so without this it has no way to offer the others
    /// or to say which are worth opening. That matters on real files: one in the corpus
    /// holds twenty-seven labels, and an import that could not reach past the first would
    /// be a one-way door.
    /// </summary>
    [Fact]
    public void EveryBlockIsCounted_SoTheOthersCanBeOffered()
    {
        const string threeLabels =
            "^XA^PW800^LL400^XZ"
            + "^XA^PW800^LL400^FO10,10^A0N,30^FDfirst^FS^FO10,50^A0N,30^FDsecond^FS^XZ"
            + "^XA^PW800^LL400^FO10,10^A0N,30^FDonly one^FS^XZ";

        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(threeLabels, dpmm: 8);

        Assert.Equal([0, 2, 1], result.BlockElementCounts);
        Assert.Equal(3, result.LabelCount);

        // The bare configuration block is skipped, which is why counting them matters.
        Assert.Equal(1, result.SelectedIndex);
    }

    /// <summary>Asking for a block by number gives that one, which is what lets a caller
    /// walk the file.</summary>
    [Fact]
    public void AnotherBlockCanBeAskedForByNumber()
    {
        const string threeLabels =
            "^XA^PW800^LL400^XZ"
            + "^XA^PW800^LL400^FO10,10^A0N,30^FDfirst^FS^XZ"
            + "^XA^PW800^LL400^FO10,10^A0N,30^FDthird^FS^XZ";

        ZplDocumentImportResult third = ZplDocumentImport.FromZpl(threeLabels, 8, labelIndex: 2);

        Assert.Equal(2, third.SelectedIndex);
        Assert.Equal(
            "third",
            Assert.IsType<TextElement>(Assert.Single(third.Document.Elements)).Text);
        Assert.Equal([0, 1, 1], third.BlockElementCounts);
    }

    [Fact]
    public void Size_ComesFromPrintWidthAndLabelLengthAtTheGivenDensity()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^PW1200\n^LL1800\n^XZ", dpmm: 12);

        Assert.Equal(1200, result.Document.WidthDots);
        Assert.Equal(1800, result.Document.HeightDots);
        Assert.Equal(100, result.Document.WidthMm, 3);
        Assert.Equal(150, result.Document.HeightMm, 3);
    }

    [Fact]
    public void LabelHome_ShiftsFieldOrigins()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^LH20,30\n^FO10,10^A0N,30^FDhi^FS\n^XZ");

        Element element = Assert.Single(result.Document.Elements);
        Assert.Equal(30, element.X);
        Assert.Equal(40, element.Y);
    }

    [Fact]
    public void PrintSettings_AreRecovered()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^PR8\n^MD-6\n^FO0,0^A0N,20^FDx^FS\n^PQ7\n^XZ");

        Assert.Equal(7, result.Document.Print.Copies);
        Assert.Equal(-6, result.Document.Print.DarknessDelta);
        Assert.Equal(8, result.Document.Print.SpeedIps);
    }

    [Theory]
    [InlineData("N", Orientation.Normal)]
    [InlineData("R", Orientation.Rotated90)]
    [InlineData("I", Orientation.Rotated180)]
    [InlineData("B", Orientation.Rotated270)]
    public void Orientation_IsReadFromTheFieldLetter(string letter, Orientation expected)
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            $"^XA\n^FO0,0^A0{letter},30^FDhi^FS\n^XZ");

        Assert.Equal(expected, Assert.Single(result.Document.Elements).Orientation);
    }

    /// <summary>A ^GB whose border fills its height is how ZPL draws a horizontal rule.
    /// Reading it back as a line rather than a flat box loses nothing, because both
    /// generate the same command.</summary>
    [Fact]
    public void FilledBox_ComesBackAsALine()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^FO10,10^GB240,3,3,B^FS\n^FO10,50^GB3,240,3,B^FS\n^FO10,100^GB100,80,2,B^FS\n^XZ");

        var horizontal = Assert.IsType<LineElement>(result.Document.Elements[0]);
        Assert.False(horizontal.IsVertical);
        Assert.Equal(240, horizontal.LengthDots);

        var vertical = Assert.IsType<LineElement>(result.Document.Elements[1]);
        Assert.True(vertical.IsVertical);
        Assert.Equal(240, vertical.LengthDots);

        var box = Assert.IsType<BoxElement>(result.Document.Elements[2]);
        Assert.Equal(100, box.WidthDots);
        Assert.Equal(80, box.HeightDots);
    }

    [Fact]
    public void HexEscapedText_IsDecodedBackToItsCharacters()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^FO0,0^A0N,30^FH_^FDa_5Eb_7Ec_5Fd^FS\n^XZ");

        var text = Assert.IsType<TextElement>(Assert.Single(result.Document.Elements));
        Assert.Equal("a^b~c_d", text.Text);
    }

    [Fact]
    public void TemplateMarkers_SurviveVerbatim()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^FO0,0^A0N,30^FD##FILIAL_DOCUMENTO##^FS\n^XZ");

        var text = Assert.IsType<TextElement>(Assert.Single(result.Document.Elements));
        Assert.Equal("##FILIAL_DOCUMENTO##", text.Text);
    }

    [Fact]
    public void QrPayload_SplitsOffTheErrorCorrectionPrefix()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^FO0,0^BQN,2,7^FDHA,https://example.com^FS\n^XZ");

        var qr = Assert.IsType<QrCodeElement>(Assert.Single(result.Document.Elements));
        Assert.Equal("https://example.com", qr.Data);
        Assert.Equal(QrErrorCorrection.High, qr.ErrorCorrection);
        Assert.Equal(7, qr.Magnification);
    }

    [Fact]
    public void MultipleBlocks_AreCountedAndSelectable()
    {
        const string zpl = "^XA\n^FO0,0^A0N,30^FDfirst^FS\n^XZ\n^XA\n^FO0,0^A0N,30^FDsecond^FS\n^XZ";

        Assert.Equal(2, ZplDocumentImport.FromZpl(zpl).LabelCount);
        Assert.Equal("first",
            ((TextElement)ZplDocumentImport.FromZpl(zpl).Document.Elements[0]).Text);
        Assert.Equal("second",
            ((TextElement)ZplDocumentImport.FromZpl(zpl, labelIndex: 1).Document.Elements[0]).Text);
    }

    /// <summary>
    /// Real files routinely open with a bare configuration block, which is what several
    /// corpus labels do. Counting from zero handed back an empty document for that whole
    /// shape, so with no index asked for, the first block holding anything wins.
    /// </summary>
    [Fact]
    public void AnEmptyLeadingBlock_IsSkippedUnlessAskedForByIndex()
    {
        const string zpl = "^XA\n^MMT\n^XZ\n^XA\n^FO10,10^A0N,30^FDreal label^FS\n^XZ";

        ZplDocumentImportResult auto = ZplDocumentImport.FromZpl(zpl);

        Assert.Equal(2, auto.LabelCount);
        Assert.Equal(1, auto.SelectedIndex);
        Assert.Equal("real label", ((TextElement)auto.Document.Elements[0]).Text);

        // Asking for the empty block by number still gives the empty block.
        ZplDocumentImportResult explicitly = ZplDocumentImport.FromZpl(zpl, labelIndex: 0);
        Assert.Equal(0, explicitly.SelectedIndex);
        Assert.Empty(explicitly.Document.Elements);
    }

    [Fact]
    public void WarningsBelongToTheBlockThatWasImported()
    {
        const string zpl = "^XA\n^CO0\n^XZ\n^XA\n^FO0,0^GE10,10,1,B^FS\n^FO0,0^A0N,30^FDx^FS\n^XZ";

        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(zpl);

        Assert.Contains(result.Warnings, w => w.Contains("^GE", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("^CO", StringComparison.Ordinal));
    }

    /// <summary>
    /// Printer setup is dropped on purpose: it belongs to whoever configured the printer,
    /// and a label we re-emit should not carry someone else's tear offset. Saying so
    /// separately is what keeps the warning list to actual losses.
    /// </summary>
    [Fact]
    public void PrinterSetup_IsReportedAsDeliberateRatherThanAsALoss()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^MMT\n^MNY\n^IS R:X.GRF,Y\n^FO0,0^A0N,30^FDkeep^FS\n^XZ");

        Assert.Single(result.Document.Elements);
        string note = Assert.Single(result.Warnings);
        Assert.Contains("deliberate", note, StringComparison.Ordinal);
        Assert.Contains("^MM", note, StringComparison.Ordinal);
        Assert.Contains("^MN", note, StringComparison.Ordinal);
        Assert.Contains("^IS", note, StringComparison.Ordinal);
        Assert.DoesNotContain("not modelled", note, StringComparison.Ordinal);
    }

    /// <summary>
    /// The commands that look like setup but change the label are deliberately not on
    /// that list, because losing one silently would change what prints: these invert or
    /// mirror the whole label, shift every field, serialize a field, store the format on
    /// the printer, or redefine the characters this parser reads.
    /// </summary>
    [Theory]
    [InlineData("^LRY")]
    [InlineData("^PMY")]
    [InlineData("^LS40")]
    [InlineData("^LT20")]
    [InlineData("^SNserial,1,Y")]
    [InlineData("^DFR:FMT.ZPL")]
    [InlineData("^CC~")]
    public void CommandsThatChangeTheLabel_AreNeverTreatedAsPrinterSetup(string command)
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            $"^XA\n{command}\n^FO0,0^A0N,30^FDkeep^FS\n^XZ");

        Assert.Contains(result.Warnings, w => w.Contains("not modelled", StringComparison.Ordinal));
    }

    [Fact]
    public void UnmodelledCommands_AreReportedRatherThanDroppedSilently()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^FO0,0^GE100,100,2,B^FS\n^FO0,0^A0N,30^FDkeep^FS\n^XZ");

        Assert.Single(result.Document.Elements);
        Assert.Contains(result.Warnings, w => w.Contains("^GE", StringComparison.Ordinal));
    }

    [Fact]
    public void ARecalledGraphicWithNoDownload_IsNamedNotSkippedQuietly()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^FO10,10^XGCARIMBO2,1,1^FS\n^XZ");

        Assert.Empty(result.Document.Elements);
        Assert.Contains(result.Warnings, w => w.Contains("CARIMBO2", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyOrJunkInput_GivesAnEmptyDocumentInsteadOfThrowing()
    {
        Assert.Empty(ZplDocumentImport.FromZpl(string.Empty).Document.Elements);
        Assert.Empty(ZplDocumentImport.FromZpl("not zpl at all").Document.Elements);
        Assert.DoesNotContain(
            ZplDocumentImport.FromZpl("^XA^FO^A0^FD^FS^XZ").Warnings,
            w => w.Contains("not modelled", StringComparison.Ordinal));
    }

    /// <summary>The corpus is the eventual target, not this round's promise. What is
    /// asserted here is only that reading it never throws and always reports.</summary>
    [Fact]
    public void RealCorpusFiles_AreReadWithoutThrowing()
    {
        foreach (string path in TestCorpus.Files())
        {
            string zpl = ZplTextFile.ReadFile(path).Text;

            ZplDocumentImportResult result = ZplDocumentImport.FromZpl(zpl);

            Assert.NotNull(result.Document);
            Assert.All(result.Document.Elements, e =>
            {
                Assert.True(e.X >= 0, $"{path}: negative X");
                Assert.True(e.Y >= 0, $"{path}: negative Y");
            });
        }
    }
}
