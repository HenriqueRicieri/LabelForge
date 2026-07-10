using System.Text;
using LabelForge.Core.Export;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

public sealed class PdfExporterTests
{
    [Fact]
    public void FromPng_ProducesAPdf_AtTheLabelsPhysicalSize()
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 12 };
        doc.Elements.Add(new TextElement { X = 40, Y = 40, Text = "PDF export", FontHeightDots = 60 });
        doc.Elements.Add(new BarcodeElement { X = 40, Y = 160, Data = "123456", HeightDots = 120 });

        string zpl = new ZplGenerator().Generate(doc);
        RenderResult render = new BinaryKitsRenderer().Render(zpl, doc.WidthMm, doc.HeightMm, doc.Dpmm);

        byte[] pdf = PdfExporter.FromPng(render.Png, doc.WidthMm, doc.HeightMm);

        Assert.True(pdf.Length > 1000, "PDF suspiciously small");
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));

        // 100 mm = 283.46 pt; the page dictionary carries the MediaBox in clear text.
        string text = Encoding.ASCII.GetString(pdf);
        Assert.Contains("MediaBox", text);
        Assert.Contains("283", text);
    }

    [Fact]
    public void FromPng_RejectsEmptyImageAndBadSize()
    {
        Assert.Throws<ArgumentException>(() => PdfExporter.FromPng([], 100, 60));
        Assert.ThrowsAny<ArgumentException>(() => PdfExporter.FromPng([1, 2, 3], 0, 60));
    }
}
