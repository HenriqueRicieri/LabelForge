using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// The .lfl format, frozen. Fixtures/format-v1.lfl was written by the serializer once,
/// with every element type and every setting away from its default, and is committed as
/// it came out. Round trips alone could not catch a renamed enum member, a changed
/// "$type" discriminator or a property that stopped being written: both sides of a round
/// trip change together, and every label already saved by a user stops opening. If this
/// test fails, the change breaks existing files and needs a migration, not a new fixture.
/// </summary>
public sealed class LabelFileFormatTests
{
    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "format-v1.lfl"))
            .ReplaceLineEndings("\n");

    [Fact]
    public void TheSavedFormatStillReadsAndWritesTheSameBytes()
    {
        string saved = Fixture();

        string written = LabelDocumentJson.Serialize(LabelDocumentJson.Deserialize(saved))
            .ReplaceLineEndings("\n");

        Assert.Equal(saved, written);
    }

    [Fact]
    public void EveryElementTypeAndSettingComesBack()
    {
        LabelDocument document = LabelDocumentJson.Deserialize(Fixture());

        Assert.Equal(
            ["text", "barcode", "qr", "datamatrix", "pdf417", "image", "line", "box", "ellipse", "diagonal"],
            document.Elements.Select(e => e switch
            {
                TextElement => "text",
                BarcodeElement => "barcode",
                QrCodeElement => "qr",
                DataMatrixElement => "datamatrix",
                Pdf417Element => "pdf417",
                ImageElement => "image",
                LineElement => "line",
                BoxElement => "box",
                EllipseElement => "ellipse",
                DiagonalLineElement => "diagonal",
                _ => "unknown",
            }));

        var text = (TextElement)document.Elements[0];
        Assert.Equal((TextJustification.Center, FieldAnchor.Baseline, Orientation.Rotated90),
            (text.Justification, text.Anchor, text.Orientation));
        Assert.Equal(BarcodeSymbology.Interleaved2of5, ((BarcodeElement)document.Elements[1]).Symbology);
        Assert.Equal(QrErrorCorrection.Quartile, ((QrCodeElement)document.Elements[2]).ErrorCorrection);
        Assert.Equal(DitherMode.Atkinson, ((ImageElement)document.Elements[5]).Dithering);
        Assert.Equal(MediaHandling.Cutter, document.Print.MediaHandling);
        Assert.Equal(VariableKind.Counter, document.Variables["SERIE"].Kind);
        Assert.Equal(VariableKind.Clock, document.Variables["DATA"].Kind);
        Assert.Equal("{{", document.Markers.Open);
        Assert.Equal(50.8, document.HeightMm, 3);
    }
}
