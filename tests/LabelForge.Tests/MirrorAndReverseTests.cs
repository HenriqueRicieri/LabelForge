using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// The two label-wide print options: ^PM mirrors the printable area, ^LR reverses every
/// field on it.
///
/// They keep the contract ^MD, ^PR and ^MM already have - the printer's own setting is
/// the default and emits nothing - and they part company on one thing the canvas cares
/// about. ^LR is ink and the offline engine draws it, so it rides the preview; ^PM is a
/// transform the engine does not implement at all, so the panel says so instead.
/// </summary>
public sealed class MirrorAndReverseTests
{
    private static string Header(int widthDots, int heightDots) =>
        $"^XA\n^CI28\n^PW{widthDots}\n^LL{heightDots}\n^LH0,0\n";

    private static LabelDocument Doc(params Element[] elements)
    {
        var doc = new LabelDocument { WidthMm = 60, HeightMm = 40, Dpmm = 8 }; // 480 x 320
        foreach (Element e in elements)
        {
            doc.Elements.Add(e);
        }

        return doc;
    }

    private static LabelDocument Labelled() =>
        Doc(new TextElement { X = 0, Y = 0, FontHeightDots = 30, Text = "A" });

    /// <summary>The contract both of them inherit: off is the absence of a command, not a
    /// command saying "off", so every label written before this generates its old bytes.</summary>
    [Fact]
    public void NeitherOption_EmitsAnythingWhenOff()
    {
        LabelDocument doc = Labelled();

        Assert.False(doc.Print.Mirror);
        Assert.False(doc.Print.ReverseAll);
        Assert.Equal(
            Header(480, 320) + "^FO0,0^A0N,30^FDA^FS\n^XZ",
            new ZplGenerator().Generate(doc));
    }

    [Fact]
    public void Mirror_IsStatedOnceBeforeTheFields()
    {
        LabelDocument doc = Labelled();
        doc.Print.Mirror = true;

        Assert.Equal(
            Header(480, 320) + "^PMY\n^FO0,0^A0N,30^FDA^FS\n^XZ",
            new ZplGenerator().Generate(doc));
    }

    /// <summary>"Only fields following this command are affected", so a ^LR after the
    /// first field would reverse part of the label.</summary>
    [Fact]
    public void Reverse_IsStatedOnceBeforeTheFields()
    {
        LabelDocument doc = Labelled();
        doc.Print.ReverseAll = true;

        Assert.Equal(
            Header(480, 320) + "^LRY\n^FO0,0^A0N,30^FDA^FS\n^XZ",
            new ZplGenerator().Generate(doc));
    }

    /// <summary>
    /// The split that decides where each one is emitted. The canvas is the render of the
    /// ZPL we generate, so a command the engine honours belongs in the preview and one it
    /// cannot draw is only noise there.
    /// </summary>
    [Fact]
    public void ThePreviewCarriesTheReverseAndNotTheMirror()
    {
        LabelDocument doc = Labelled();
        doc.Print.Mirror = true;
        doc.Print.ReverseAll = true;

        string preview = new ZplGenerator().GeneratePreview(doc, offsetDots: 0);

        Assert.Contains("^LRY", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("^PM", preview, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RoundTrip_KeepsBothFlags(bool mirror, bool reverse)
    {
        LabelDocument doc = Doc(
            new TextElement { X = 10, Y = 10, FontHeightDots = 30, Text = "one" },
            new BoxElement { X = 10, Y = 60, WidthDots = 200, HeightDots = 40, ThicknessDots = 2 });
        doc.Print.Mirror = mirror;
        doc.Print.ReverseAll = reverse;

        string first = new ZplGenerator().Generate(doc);
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(first, dpmm: 8);

        Assert.Empty(imported.Warnings);
        Assert.Equal(mirror, imported.Document.Print.Mirror);
        Assert.Equal(reverse, imported.Document.Print.ReverseAll);
        Assert.Equal(first, new ZplGenerator().Generate(imported.Document));
    }

    /// <summary>
    /// A field's own ^FR survives a label-wide ^LR rather than being swallowed by it, and
    /// is not stated twice. Both matter: the two do not cancel on a printer (measured
    /// below), so a label carrying both has to regenerate carrying both, and a label that
    /// carried neither must not come back with an ^FR on every field.
    /// </summary>
    [Fact]
    public void AFieldsOwnReverse_SurvivesALabelWideOne()
    {
        LabelDocument doc = Doc(
            new TextElement { X = 10, Y = 10, FontHeightDots = 30, Text = "plain" },
            new TextElement
            {
                X = 10, Y = 60, FontHeightDots = 30, Text = "knockout", IsReversed = true,
            });
        doc.Print.ReverseAll = true;

        string first = new ZplGenerator().Generate(doc);
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(first, dpmm: 8);

        Assert.True(imported.Document.Print.ReverseAll);
        Assert.False(imported.Document.Elements[0].IsReversed);
        Assert.True(imported.Document.Elements[1].IsReversed);
        Assert.Equal(first, new ZplGenerator().Generate(imported.Document));
    }

    /// <summary>Both are printer state rather than properties of one block: each "remains
    /// active" until its own N form arrives or the printer is turned off. A file that
    /// states them in a setup block and draws in the next one means them.</summary>
    [Fact]
    public void BothCarryIntoTheBlocksThatFollow()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^PMY^LRY^XZ\n^XA^PW480^LL320^FO10,10^A0N,30^FDdrawn^FS^XZ");

        Assert.True(result.Document.Print.Mirror);
        Assert.True(result.Document.Print.ReverseAll);
        Assert.Single(result.Document.Elements);
    }

    [Theory]
    [InlineData("^PMN^LRN", false, false)]
    [InlineData("^PMY^LRY", true, true)]
    [InlineData("^PMy^LRy", true, true)]
    public void TheOffFormsTurnBothBackOff(string second, bool mirror, bool reverse)
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            $"^XA^PMY^LRY^XZ\n^XA{second}^PW480^LL320^FO10,10^A0N,30^FDdrawn^FS^XZ");

        Assert.Equal(mirror, result.Document.Print.Mirror);
        Assert.Equal(reverse, result.Document.Print.ReverseAll);
    }

    /// <summary>
    /// Two different rules for a missing argument, each from its own source. The manual
    /// says of ^PM and of nothing else here that an invalid parameter means the command is
    /// ignored; for ^LR it gives "N or last permanently saved value", which decides
    /// nothing, so this follows the renderer the canvas draws with - measured, a bare ^LR
    /// prints exactly as ^LRN.
    /// </summary>
    [Fact]
    public void AMissingArgument_IgnoresTheMirrorAndClearsTheReverse()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^PMY^LRY^XZ\n^XA^PM^LR^PW480^LL320^FO10,10^A0N,30^FDdrawn^FS^XZ");

        Assert.True(result.Document.Print.Mirror);
        Assert.False(result.Document.Print.ReverseAll);
    }

    /// <summary>
    /// Reverse turned on part way through is the one shape a document-wide flag cannot
    /// express, so it is folded into the fields it reaches instead. The printed label is
    /// identical either way, because the command is defined as exactly that; what changes
    /// is which fields carry it, and hoisting a partial ^LR would reverse fields the file
    /// left alone.
    /// </summary>
    [Fact]
    public void ReverseTurnedOnPartWayThrough_FoldsIntoTheFieldsItReaches()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^PW480^LL320\n^FO10,10^A0N,30^FDbefore^FS\n^LRY\n^FO10,60^A0N,30^FDafter^FS\n^XZ");

        Assert.False(result.Document.Print.ReverseAll);
        Assert.False(result.Document.Elements[0].IsReversed);
        Assert.True(result.Document.Elements[1].IsReversed);
    }

    [Fact]
    public void ReverseCoveringEveryField_BecomesTheLabelsOwnFlag()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^PW480^LL320^LRY\n^FO10,10^A0N,30^FDone^FS\n^FO10,60^A0N,30^FDtwo^FS\n^XZ");

        Assert.True(result.Document.Print.ReverseAll);
        Assert.All(result.Document.Elements, e => Assert.False(e.IsReversed));
    }

    /// <summary>Neither is reported as a loss any more, which is the whole point of
    /// modelling them: the warning list holds only what the import could not keep.</summary>
    [Fact]
    public void NeitherIsReportedAsAnUnmodelledCommand()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^PW480^LL320^PMY^LRY^FO10,10^A0N,30^FDdrawn^FS^XZ");

        Assert.Empty(result.Warnings);
    }

    /// <summary>The .lfl format is also the undo snapshot format, so this is what makes
    /// either option an ordinary undoable edit as well as a saved one. A label written
    /// before they existed names neither and opens with both off.</summary>
    [Fact]
    public void BothSurviveTheProjectFile()
    {
        LabelDocument doc = Labelled();
        doc.Print.Mirror = true;
        doc.Print.ReverseAll = true;

        LabelDocument back = LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(doc));

        Assert.True(back.Print.Mirror);
        Assert.True(back.Print.ReverseAll);

        LabelDocument old = LabelDocumentJson.Deserialize("""
            {
              "SchemaVersion": 1,
              "Document": {
                "WidthMm": 60, "HeightMm": 40, "Dpmm": 8,
                "Print": { "Copies": 1 },
                "Elements": []
              }
            }
            """);

        Assert.False(old.Print.Mirror);
        Assert.False(old.Print.ReverseAll);
    }

    /// <summary>
    /// The WYSIWYG half. ^LR has to remove ink from the bar exactly as a per-field ^FR
    /// does, or the canvas would show one thing and the printer produce another.
    /// Measured against Labelary too, which reverses both the same way.
    /// </summary>
    [Fact]
    public void Renderer_ReversesTheWholeLabelExactlyAsAFieldDoes()
    {
        BoxElement Bar() => new()
        {
            X = 20, Y = 20, WidthDots = 320, HeightDots = 80, ThicknessDots = 80,
        };

        TextElement Caption(bool reversed) => new()
        {
            X = 40, Y = 40, Text = "ABCD", FontHeightDots = 40, FontWidthDots = 40,
            IsReversed = reversed, ZOrder = 1,
        };

        LabelDocument labelWide = Doc(Bar(), Caption(reversed: false));
        labelWide.Print.ReverseAll = true;

        int plain = Ink(Doc(Bar(), Caption(reversed: false)));
        int perField = Ink(Doc(Bar(), Caption(reversed: true)));

        Assert.True(perField < plain, $"a reversed field must remove ink ({perField} vs {plain})");
        Assert.Equal(perField, Ink(labelWide));
    }

    /// <summary>
    /// They do not cancel. A field marked reversed on a label-wide reverse is reversed
    /// once, not back to normal, which is what makes it safe to carry the flag at both
    /// levels. Measured on both engines: the offline renderer and Labelary each draw
    /// ^LRY + ^FR identically to ^FR alone.
    /// </summary>
    [Fact]
    public void ALabelWideReverseAndAFieldsOwn_DoNotCancel()
    {
        BoxElement Bar() => new()
        {
            X = 20, Y = 20, WidthDots = 320, HeightDots = 80, ThicknessDots = 80,
        };

        TextElement Caption(bool reversed) => new()
        {
            X = 40, Y = 40, Text = "ABCD", FontHeightDots = 40, FontWidthDots = 40,
            IsReversed = reversed, ZOrder = 1,
        };

        LabelDocument both = Doc(Bar(), Caption(reversed: true));
        both.Print.ReverseAll = true;

        Assert.Equal(Ink(Doc(Bar(), Caption(reversed: true))), Ink(both));
    }

    /// <summary>
    /// A limitation, pinned so it stays known and so the panel keeps saying it: the
    /// offline engine ignores ^PM outright, where a printer flips the printable area.
    /// Measured against Labelary at 8 dpmm on a 2 inch label, the same 80 by 40 block
    /// draws at x 20..99 unmirrored and x 306..385 mirrored, which is 406 dots less the
    /// far edge of the ink. Nothing here can produce that picture, so the canvas draws
    /// the unmirrored side and the properties panel says which side that is.
    /// </summary>
    [Fact]
    public void Renderer_IgnoresTheMirror()
    {
        LabelDocument doc = Doc(new BoxElement
        {
            X = 20, Y = 20, WidthDots = 80, HeightDots = 40, ThicknessDots = 40,
        });

        (int Ink, int MinX, int MaxX) plain = InkSpan(doc);
        doc.Print.Mirror = true;
        (int Ink, int MinX, int MaxX) mirrored = InkSpan(doc);

        Assert.Equal(3200, plain.Ink);
        Assert.Equal(plain, mirrored);
    }

    /// <summary>
    /// The second half of the same limitation, and the reason it is worth measuring rather
    /// than assuming ^LR reaches everything: the offline engine reverses text and ^GB and
    /// leaves a barcode alone, where Labelary knocks the bars out of the slab under them
    /// (40800 ink to 34800, which is the symbol's own 6000 removed). It is the limit ^FR
    /// already has on an image, in another place.
    /// </summary>
    [Fact]
    public void Renderer_DoesNotReverseABarcode()
    {
        LabelDocument Slab(bool reverseAll)
        {
            LabelDocument doc = Doc(
                new BoxElement
                {
                    X = 20, Y = 120, WidthDots = 340, HeightDots = 120, ThicknessDots = 120,
                },
                new BarcodeElement
                {
                    X = 40, Y = 140, Data = "12345", HeightDots = 60, ModuleWidthDots = 2,
                    PrintInterpretationLine = false, ZOrder = 1,
                });
            doc.Print.ReverseAll = reverseAll;
            return doc;
        }

        Assert.Equal(Ink(Slab(reverseAll: false)), Ink(Slab(reverseAll: true)));
    }

    private static int Ink(LabelDocument document) => InkSpan(document).Ink;

    private static (int Ink, int MinX, int MaxX) InkSpan(LabelDocument document)
    {
        var renderer = new BinaryKitsRenderer();
        RenderResult result = renderer.Render(
            new ZplGenerator().Generate(document), 60, 40, 8);
        Assert.Empty(result.Errors);

        using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
        Assert.NotNull(bitmap);

        int ink = 0, minX = int.MaxValue, maxX = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    ink++;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                }
            }
        }

        return (ink, minX, maxX);
    }
}
