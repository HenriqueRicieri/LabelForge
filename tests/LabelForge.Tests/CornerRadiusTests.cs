using System.Text;
using LabelForge.Core.Export;
using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// The die-cut corner radius describes the stock, not the print. These tests hold that
/// line: it must shape what the designer and the PDF show, and must never appear in
/// the ZPL, which has no notion of the label's outline.
/// </summary>
public sealed class CornerRadiusTests
{
    private static LabelDocument Doc(double radiusMm = 0) =>
        new() { WidthMm = 100, HeightMm = 60, Dpmm = 8, CornerRadiusMm = radiusMm };

    [Fact]
    public void TheZpl_IsByteIdenticalWithAndWithoutARadius()
    {
        LabelDocument square = Doc();
        LabelDocument rounded = Doc(radiusMm: 3);
        foreach (LabelDocument doc in new[] { square, rounded })
        {
            doc.Elements.Add(new TextElement { X = 20, Y = 20, Text = "Rounded" });
            doc.Elements.Add(new BarcodeElement { X = 20, Y = 80, Data = "123456" });
        }

        Assert.Equal(new ZplGenerator().Generate(square), new ZplGenerator().Generate(rounded));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(30, 30)]     // exactly half the 60 mm side
    [InlineData(45, 30)]     // past half: the corners would overlap
    [InlineData(-5, 0)]      // a negative radius is not a shape
    public void EffectiveRadius_ClampsToHalfTheShorterSide(double set, double expected)
    {
        Assert.Equal(expected, Doc(set).EffectiveCornerRadiusMm);
    }

    [Theory]
    [InlineData(8, 24)]
    [InlineData(12, 36)]
    [InlineData(24, 72)]
    public void RadiusInDots_FollowsTheDocumentDensity(int dpmm, int expectedDots)
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = dpmm, CornerRadiusMm = 3 };

        Assert.Equal(expectedDots, doc.CornerRadiusDots);
    }

    [Fact]
    public void Radius_RoundTripsThroughTheProjectFormat()
    {
        LabelDocument reloaded = LabelDocumentJson.Deserialize(
            LabelDocumentJson.Serialize(Doc(radiusMm: 1.5)));

        Assert.Equal(1.5, reloaded.CornerRadiusMm);
    }

    [Fact]
    public void DocumentsSavedBeforeTheRadiusExisted_LoadWithSquareCorners()
    {
        const string legacy = """
        {
          "SchemaVersion": 1,
          "Document": { "WidthMm": 100, "HeightMm": 60, "Dpmm": 8, "Elements": [] }
        }
        """;

        LabelDocument doc = LabelDocumentJson.Deserialize(legacy);

        Assert.Equal(0, doc.CornerRadiusMm);
        Assert.Equal(0, doc.CornerRadiusDots);
    }

    [Fact]
    public void CatalogMediaCarryTheirDieCutRadius()
    {
        // 719 of the 797 catalog entries have one; a media picked in the designer is
        // what puts a radius on the document in the first place.
        Assert.Contains(Core.Media.StockCatalog.All, media => media.RadiusMm > 0);
    }

    [Fact]
    public void Pdf_WithARadius_IsStillAValidPdfAtTheSameSize()
    {
        LabelDocument doc = Doc(radiusMm: 3);
        doc.Elements.Add(new TextElement { X = 20, Y = 20, Text = "Rounded", FontHeightDots = 40 });
        byte[] png = new BinaryKitsRenderer()
            .Render(new ZplGenerator().Generate(doc), doc.WidthMm, doc.HeightMm, doc.Dpmm).Png;

        byte[] pdf = PdfExporter.FromPng(
            png, doc.WidthMm, doc.HeightMm, doc.EffectiveCornerRadiusMm);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(pdf.Length > 1000, "PDF suspiciously small");

        // Clipping must not change the sheet: 100 mm is still 283 pt across.
        Assert.Contains("283", Encoding.ASCII.GetString(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_WithAnOversizedRadius_ClampsInsteadOfFailing()
    {
        LabelDocument doc = Doc(radiusMm: 500);
        byte[] png = new BinaryKitsRenderer()
            .Render(new ZplGenerator().Generate(doc), doc.WidthMm, doc.HeightMm, doc.Dpmm).Png;

        // Straight from the document, unclamped, the way a careless caller would pass it.
        byte[] pdf = PdfExporter.FromPng(png, doc.WidthMm, doc.HeightMm, doc.CornerRadiusMm);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
