using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class LabelDocumentJsonTests
{
    private static LabelDocument SampleDocument()
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 12 };
        doc.Elements.Add(new TextElement
        {
            Name = "Title", X = 50, Y = 40, Text = "Olá ##VAR##", FontHeightDots = 60,
            Orientation = Orientation.Rotated270, ZOrder = 1,
        });
        doc.Elements.Add(new BarcodeElement
        {
            Name = "Code", X = 50, Y = 170, Data = "LF-1", Symbology = BarcodeSymbology.Code39,
            HeightDots = 120, ModuleWidthDots = 3, WideBarRatio = 2.5, PrintInterpretationLine = false,
        });
        doc.Elements.Add(new QrCodeElement
        {
            X = 600, Y = 170, Data = "https://x", Magnification = 4, ErrorCorrection = QrErrorCorrection.High,
        });
        doc.Elements.Add(new LineElement { X = 10, Y = 10, LengthDots = 300, ThicknessDots = 2, IsVertical = true });
        doc.Elements.Add(new BoxElement { X = 5, Y = 5, WidthDots = 700, HeightDots = 400, IsLocked = true });
        return doc;
    }

    [Fact]
    public void RoundTrip_PreservesEveryElementTypeAndProperty()
    {
        var original = SampleDocument();

        string json = LabelDocumentJson.Serialize(original);
        LabelDocument restored = LabelDocumentJson.Deserialize(json);

        // Re-serializing the restored document must reproduce the exact JSON:
        // any property that fails to round-trip would show up as a difference.
        Assert.Equal(json, LabelDocumentJson.Serialize(restored));

        Assert.Equal(5, restored.Elements.Count);
        Assert.Equal(original.Elements[0].Id, restored.Elements[0].Id);

        var text = Assert.IsType<TextElement>(restored.Elements[0]);
        Assert.Equal("Olá ##VAR##", text.Text);
        Assert.Equal(Orientation.Rotated270, text.Orientation);

        var barcode = Assert.IsType<BarcodeElement>(restored.Elements[1]);
        Assert.Equal(BarcodeSymbology.Code39, barcode.Symbology);
        Assert.Equal(2.5, barcode.WideBarRatio);
        Assert.False(barcode.PrintInterpretationLine);

        Assert.True(Assert.IsType<BoxElement>(restored.Elements[4]).IsLocked);
    }

    [Fact]
    public void SingleElement_RoundTrips_WithTypeAndProperties()
    {
        var original = new BarcodeElement
        {
            X = 10, Y = 20, Data = "ABC", Symbology = BarcodeSymbology.Code39,
            ModuleWidthDots = 4, Orientation = Orientation.Rotated90,
        };

        Element copy = LabelDocumentJson.DeserializeElement(LabelDocumentJson.SerializeElement(original));

        var barcode = Assert.IsType<BarcodeElement>(copy);
        Assert.Equal(original.Id, barcode.Id);
        Assert.Equal("ABC", barcode.Data);
        Assert.Equal(BarcodeSymbology.Code39, barcode.Symbology);
        Assert.Equal(4, barcode.ModuleWidthDots);
        Assert.Equal(Orientation.Rotated90, barcode.Orientation);
    }

    [Fact]
    public void Deserialize_RejectsNewerSchemaVersions()
    {
        string json = LabelDocumentJson.Serialize(new LabelDocument())
            .Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 999");

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => LabelDocumentJson.Deserialize(json));
    }
}
