using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// What the printer does with a label once it has printed it: ^MM's print mode, ^PQ's
/// cut-after-group value, and ^LT's registration nudge.
///
/// All three are job settings in the same family as ^PQ, ^MD and ^PR, so the property
/// that matters most is the one those already have: a document that asks for none of
/// them generates exactly the bytes it did before any of this existed.
/// </summary>
public sealed class MediaHandlingTests
{
    private static string Header(int widthDots, int heightDots) =>
        $"^XA\n^CI28\n^PW{widthDots}\n^LL{heightDots}\n^LH0,0\n";

    private static LabelDocument Doc(params Element[] elements)
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 150, Dpmm = 8 }; // 800 x 1200
        foreach (Element e in elements)
        {
            doc.Elements.Add(e);
        }

        return doc;
    }

    private static LabelDocument Labelled() =>
        Doc(new TextElement { X = 0, Y = 0, FontHeightDots = 30, Text = "A" });

    /// <summary>The whole contract of the printer-default value: it is not a mode, it is
    /// the absence of one, so nothing is stated and the operator's setting stands.</summary>
    [Fact]
    public void PrinterDefault_EmitsNothingAtAll()
    {
        LabelDocument doc = Labelled();

        Assert.Equal(MediaHandling.PrinterDefault, doc.Print.MediaHandling);
        Assert.Equal(
            Header(800, 1200) + "^FO0,0^A0N,30^FDA^FS\n^XZ",
            new ZplGenerator().Generate(doc));
    }

    [Theory]
    [InlineData(MediaHandling.TearOff, "^MMT")]
    [InlineData(MediaHandling.PeelOff, "^MMP")]
    [InlineData(MediaHandling.Rewind, "^MMR")]
    [InlineData(MediaHandling.Applicator, "^MMA")]
    [InlineData(MediaHandling.Cutter, "^MMC")]
    [InlineData(MediaHandling.DelayedCutter, "^MMD")]
    public void EveryMode_EmitsItsOwnLetterAndComesBack(MediaHandling mode, string expected)
    {
        LabelDocument doc = Labelled();
        doc.Print.MediaHandling = mode;

        string zpl = new ZplGenerator().Generate(doc);
        Assert.Contains(expected + "\n", zpl, StringComparison.Ordinal);

        LabelDocument back = ZplDocumentImport.FromZpl(zpl).Document;
        Assert.Equal(mode, back.Print.MediaHandling);
    }

    /// <summary>Prepeel presents the next label before it is asked for, which only a
    /// peeler can do. Stating it in another mode would be carrying a setting into a
    /// machine that has nothing to peel.</summary>
    [Fact]
    public void Prepeel_RidesPeelOffAndNoOtherMode()
    {
        LabelDocument doc = Labelled();
        doc.Print.MediaHandling = MediaHandling.PeelOff;
        doc.Print.Prepeel = true;
        Assert.Contains("^MMP,Y\n", new ZplGenerator().Generate(doc), StringComparison.Ordinal);

        doc.Print.MediaHandling = MediaHandling.Cutter;
        string cutter = new ZplGenerator().Generate(doc);
        Assert.Contains("^MMC\n", cutter, StringComparison.Ordinal);
        Assert.DoesNotContain(",Y", cutter, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepeel_RoundTrips()
    {
        LabelDocument doc = Labelled();
        doc.Print.MediaHandling = MediaHandling.PeelOff;
        doc.Print.Prepeel = true;

        var generator = new ZplGenerator();
        string once = generator.Generate(doc);
        LabelDocument back = ZplDocumentImport.FromZpl(once).Document;

        Assert.True(back.Print.Prepeel);
        Assert.Equal(once, new ZplGenerator().Generate(back));
    }

    /// <summary>
    /// The modes the manual lists that this designer does not model: RFID, kiosk, and the
    /// two reserved letters. None of them describes what happens to an ordinary label, and
    /// guessing one of them wrong changes what the machine does with the media, so it is
    /// named rather than mapped onto the nearest thing.
    /// </summary>
    [Theory]
    [InlineData("F")]
    [InlineData("K")]
    [InlineData("L")]
    public void AModeNotModelled_IsReportedRatherThanGuessedAt(string letter)
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            $"^XA\n^MM{letter}\n^FO0,0^A0N,30^FDx^FS\n^XZ");

        Assert.Equal(MediaHandling.PrinterDefault, result.Document.Print.MediaHandling);
        Assert.Contains(result.Warnings, w => w.Contains("^MM" + letter, StringComparison.Ordinal));
    }

    /// <summary>
    /// ^MM is persistent, the way ^PW and ^LL are, and the corpus is what settles it: two
    /// of the three real files that state a print mode put it in a bare setup block of its
    /// own and never mention it again. Reading it per block would drop the mode from the
    /// only block that draws anything.
    /// </summary>
    [Fact]
    public void AModeStatedInASetupBlock_ReachesTheLabelThatFollows()
    {
        // The shape 440.zpl and 208v1.ZPL are written in.
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^COY,0^MMT^MD+0\n^XZ\n^XA\n^FO10,10^A0N,30^FDreal label^FS\n^XZ");

        Assert.Equal(1, result.SelectedIndex);
        Assert.Equal(MediaHandling.TearOff, result.Document.Print.MediaHandling);
    }

    /// <summary>^LT moves the printed format relative to the media's own top edge, which
    /// the label's coordinate space cannot express: it is a registration nudge for how the
    /// stock sits, not eight dots of design.</summary>
    [Theory]
    [InlineData(12)]
    [InlineData(-12)]
    public void LabelTop_IsStatedBeforeAnyFieldAndComesBack(int offset)
    {
        LabelDocument doc = Labelled();
        doc.Print.LabelTopDots = offset;

        string zpl = new ZplGenerator().Generate(doc);
        Assert.Contains($"^LT{offset}\n", zpl, StringComparison.Ordinal);
        Assert.True(
            zpl.IndexOf("^LT", StringComparison.Ordinal) < zpl.IndexOf("^FS", StringComparison.Ordinal),
            "^LT has to precede the fields it moves.");

        Assert.Equal(offset, ZplDocumentImport.FromZpl(zpl).Document.Print.LabelTopDots);
    }

    /// <summary>Zero emits nothing rather than ^LT0: a label that stated it would be
    /// overwriting a setting somebody made on the printer's front panel.</summary>
    [Fact]
    public void LabelTop_OfZeroStatesNothing()
    {
        Assert.DoesNotContain("^LT", new ZplGenerator().Generate(Labelled()), StringComparison.Ordinal);
    }

    [Fact]
    public void LabelTop_IsClampedToTheRangeThePrinterAccepts()
    {
        LabelDocument doc = Labelled();
        doc.Print.LabelTopDots = 5000;

        Assert.Contains("^LT120\n", new ZplGenerator().Generate(doc), StringComparison.Ordinal);
    }

    /// <summary>
    /// Reaching ^PQ's override flag means stating the two parameters in between. The
    /// replicate count is ZPL's own default and Y is what makes the printer cut without
    /// also pausing to wait for someone to press a button.
    /// </summary>
    [Fact]
    public void CutAfterGroup_StatesTheWholeOfPrintQuantity()
    {
        LabelDocument doc = Labelled();
        doc.Print.Copies = 20;
        doc.Print.MediaHandling = MediaHandling.Cutter;
        doc.Print.CutAfterLabels = 5;

        Assert.Contains("^PQ20,5,0,Y\n", new ZplGenerator().Generate(doc), StringComparison.Ordinal);
    }

    /// <summary>The bare form is what every label written before the cutter existed
    /// generates, and it has to stay that way.</summary>
    [Fact]
    public void NoCutGroup_LeavesPrintQuantityExactlyAsItWas()
    {
        LabelDocument doc = Labelled();
        doc.Print.Copies = 20;
        doc.Print.MediaHandling = MediaHandling.Cutter;

        Assert.Contains("^PQ20\n", new ZplGenerator().Generate(doc), StringComparison.Ordinal);
    }

    /// <summary>A group with a single copy is still stated: ^PQ is the only place it can
    /// be said, and a run of one that never completes a group is the operator's business
    /// rather than something to silently drop.</summary>
    [Fact]
    public void ACutGroup_IsStatedEvenWhenTheQuantityIsOne()
    {
        LabelDocument doc = Labelled();
        doc.Print.MediaHandling = MediaHandling.Cutter;
        doc.Print.CutAfterLabels = 4;

        Assert.Contains("^PQ1,4,0,Y\n", new ZplGenerator().Generate(doc), StringComparison.Ordinal);
    }

    /// <summary>^PQ counts pulls of the web rather than labels, and the group is in the
    /// same units as the quantity beside it, so it goes through the same conversion rather
    /// than a second one that could disagree.</summary>
    [Fact]
    public void OnMultiAcrossStock_TheGroupCountsRowsJustAsTheQuantityDoes()
    {
        LabelDocument doc = Labelled();
        doc.LabelsAcross = 3;
        doc.AcrossGapMm = 3;
        doc.Print.Copies = 12;
        doc.Print.MediaHandling = MediaHandling.Cutter;
        doc.Print.CutAfterLabels = 6;

        PrintJobResult job = PrintJob.Build(doc, new DateTime(2026, 7, 28, 9, 0, 0));

        // 12 labels is 4 pulls, and a group of 6 labels is 2 of them.
        Assert.Contains("^PQ4,2,0,Y\n", job.Zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void CutGroup_RoundTripsByteForByte()
    {
        LabelDocument doc = Labelled();
        doc.Print.Copies = 20;
        doc.Print.MediaHandling = MediaHandling.Cutter;
        doc.Print.CutAfterLabels = 5;

        string once = new ZplGenerator().Generate(doc);
        LabelDocument back = ZplDocumentImport.FromZpl(once).Document;

        Assert.Equal(5, back.Print.CutAfterLabels);
        Assert.Equal(MediaHandling.Cutter, back.Print.MediaHandling);
        Assert.Equal(once, new ZplGenerator().Generate(back));
    }

    /// <summary>
    /// The other half of ^PQ's group parameter stops the printer and waits for a person.
    /// Nothing here can carry that, and turning it into a cut would be a different machine
    /// doing a different thing to the media, so it is named.
    /// </summary>
    [Fact]
    public void PausingAfterAGroup_IsNamedRatherThanTurnedIntoACut()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^FO0,0^A0N,30^FDx^FS\n^PQ50,10,0,N\n^XZ");

        Assert.Equal(0, result.Document.Print.CutAfterLabels);
        Assert.Contains(result.Warnings, w => w.Contains("pauses", StringComparison.Ordinal));
    }

    /// <summary>
    /// Real files write ^PQ's four parameters in full and say nothing with them: the
    /// corpus writes `^PQ1,0,0,Y` and the ZDesigner driver `^PQ1,0,1,Y`. Both mean "no
    /// group, cut rather than pause", so neither may produce a warning - a note that fires
    /// on every real file buries the ones that name a loss.
    /// </summary>
    [Theory]
    [InlineData("^PQ1,0,0,Y")]
    [InlineData("^PQ1,0,1,Y")]
    [InlineData("^PQ,0,0,Y")]
    public void TheFormRealFilesWrite_SaysNothingAndWarnsAboutNothing(string quantity)
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            $"^XA\n^FO0,0^A0N,30^FDx^FS\n{quantity}\n^XZ");

        Assert.Equal(1, result.Document.Print.Copies);
        Assert.Equal(0, result.Document.Print.CutAfterLabels);
        Assert.Empty(result.Warnings);
    }

    /// <summary>Replicates only mean anything beside a ^SN, and 0 and 1 are written
    /// interchangeably for "one of each", so only a real multiplier is worth a line.</summary>
    [Fact]
    public void ReplicatesOfASerialNumber_AreNamedWhenThereAreReallyMoreThanOne()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^FO0,0^A0N,30^FDx^FS\n^PQ50,0,4,Y\n^XZ");

        Assert.Contains(result.Warnings, w => w.Contains("replicates", StringComparison.Ordinal));
    }

    /// <summary>None of it reaches the canvas. These are instructions to a machine about
    /// media, and the offline renderer would only flag them as commands it does not
    /// know.</summary>
    [Fact]
    public void NoneOfItReachesThePreview()
    {
        LabelDocument doc = Labelled();
        doc.Print.MediaHandling = MediaHandling.Cutter;
        doc.Print.CutAfterLabels = 5;
        doc.Print.LabelTopDots = 20;

        string preview = new ZplGenerator().GeneratePreview(doc, offsetDots: 0);

        Assert.DoesNotContain("^MM", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("^LT", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("^PQ", preview, StringComparison.Ordinal);
    }

    /// <summary>
    /// The .lfl format is also the undo snapshot format, so this is what makes the mode an
    /// ordinary undoable edit as well as a saved one.
    /// </summary>
    [Fact]
    public void AllOfIt_SurvivesTheProjectFile()
    {
        LabelDocument doc = Labelled();
        doc.Print.MediaHandling = MediaHandling.PeelOff;
        doc.Print.Prepeel = true;
        doc.Print.CutAfterLabels = 7;
        doc.Print.LabelTopDots = -9;

        LabelDocument back = LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(doc));

        Assert.Equal(MediaHandling.PeelOff, back.Print.MediaHandling);
        Assert.True(back.Print.Prepeel);
        Assert.Equal(7, back.Print.CutAfterLabels);
        Assert.Equal(-9, back.Print.LabelTopDots);
    }

    /// <summary>A label saved before any of this existed names none of it, and has to open
    /// as the printer's own setting rather than as a mode nobody chose.</summary>
    [Fact]
    public void AFileSavedBeforeThisExisted_OpensAsThePrintersOwnSetting()
    {
        const string old = """
            {
              "SchemaVersion": 1,
              "Document": {
                "WidthMm": 100, "HeightMm": 150, "Dpmm": 8,
                "Print": { "Copies": 3, "DarknessDelta": 0, "SpeedIps": 0 },
                "Elements": []
              }
            }
            """;

        LabelDocument doc = LabelDocumentJson.Deserialize(old);

        Assert.Equal(3, doc.Print.Copies);
        Assert.Equal(MediaHandling.PrinterDefault, doc.Print.MediaHandling);
        Assert.Equal(0, doc.Print.CutAfterLabels);
        Assert.Equal(0, doc.Print.LabelTopDots);
        Assert.Equal("^XA\n^CI28\n^PW800\n^LL1200\n^LH0,0\n^PQ3\n^XZ", new ZplGenerator().Generate(doc));
    }

    /// <summary>
    /// The load-bearing property for the whole family, stated once over all of it: a
    /// document that sets none of these generates the bytes it always did.
    /// </summary>
    [Fact]
    public void ADocumentThatAsksForNoneOfIt_IsUnchangedByAllOfIt()
    {
        LabelDocument doc = Labelled();
        doc.Print.Copies = 3;
        doc.Print.DarknessDelta = -5;
        doc.Print.SpeedIps = 4;

        Assert.Equal(
            Header(800, 1200) + "^PR4\n^MD-5\n^FO0,0^A0N,30^FDA^FS\n^PQ3\n^XZ",
            new ZplGenerator().Generate(doc));
    }
}
