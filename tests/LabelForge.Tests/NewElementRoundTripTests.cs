using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// .lfl round trips for the newer document features: Data Matrix and image elements
/// (including the binary payload), print settings, and variable sample values.
/// </summary>
public sealed class NewElementRoundTripTests
{
    [Fact]
    public void DataMatrix_RoundTrips()
    {
        var doc = new LabelDocument();
        doc.Elements.Add(new DataMatrixElement { X = 10, Y = 20, Data = "##SERIE##", ModuleSizeDots = 6 });

        var restored = LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(doc));

        var dm = Assert.IsType<DataMatrixElement>(Assert.Single(restored.Elements));
        Assert.Equal("##SERIE##", dm.Data);
        Assert.Equal(6, dm.ModuleSizeDots);
    }

    [Fact]
    public void Image_RoundTripsBytesAndSettings()
    {
        byte[] png = TestImages.SolidPng(3, 2, 10, 20, 30);
        var doc = new LabelDocument();
        doc.Elements.Add(new ImageElement
        {
            ImageData = png, SourcePixelWidth = 3, SourcePixelHeight = 2,
            WidthDots = 120, HeightDots = 80, Dithering = DitherMode.Ordered,
        });

        var restored = LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(doc));

        var image = Assert.IsType<ImageElement>(Assert.Single(restored.Elements));
        Assert.Equal(png, image.ImageData);
        Assert.Equal(3, image.SourcePixelWidth);
        Assert.Equal(DitherMode.Ordered, image.Dithering);
        Assert.Equal(120, image.WidthDots);
    }

    [Fact]
    public void PrintSettingsAndSamples_RoundTrip()
    {
        var doc = new LabelDocument();
        doc.Print.Copies = 7;
        doc.Print.DarknessDelta = 12;
        doc.Print.SpeedIps = 6;
        doc.SampleValues["LOTE"] = "L-42";

        var restored = LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(doc));

        Assert.Equal(7, restored.Print.Copies);
        Assert.Equal(12, restored.Print.DarknessDelta);
        Assert.Equal(6, restored.Print.SpeedIps);
        Assert.Equal("L-42", restored.SampleValues["LOTE"]);
    }

    [Fact]
    public void OlderDocumentWithoutTheNewFields_LoadsWithDefaults()
    {
        // A pre-print-settings .lfl: schema 1, no Print or SampleValues properties.
        const string legacy = """
            {"SchemaVersion":1,"Document":{"WidthMm":100,"HeightMm":60,"Dpmm":8,"Elements":[]}}
            """;

        var restored = LabelDocumentJson.Deserialize(legacy);

        Assert.Equal(1, restored.Print.Copies);
        Assert.Equal(0, restored.Print.DarknessDelta);
        Assert.Empty(restored.SampleValues);
    }
}
