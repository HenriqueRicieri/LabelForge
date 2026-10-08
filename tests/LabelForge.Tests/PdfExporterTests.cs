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

        // 100 mm = 283.46 pt wide and 60 mm = 170.08 pt tall. The page dictionary carries
        // the MediaBox in clear text; both sides are read, so a page with width and height
        // swapped fails instead of passing on the "283" it also contains.
        string text = Encoding.ASCII.GetString(pdf);
        var mediaBox = System.Text.RegularExpressions.Regex.Match(
            text, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]");
        Assert.True(mediaBox.Success, "no MediaBox");
        double width = double.Parse(mediaBox.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        double height = double.Parse(mediaBox.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        // The box is written in whole points, so within one of the exact size.
        Assert.InRange(width, 100 / 25.4 * 72 - 1, 100 / 25.4 * 72 + 1);
        Assert.InRange(height, 60 / 25.4 * 72 - 1, 60 / 25.4 * 72 + 1);
    }

    [Fact]
    public void FromPng_RejectsEmptyImageAndBadSize()
    {
        Assert.Throws<ArgumentException>(() => PdfExporter.FromPng([], 100, 60));
        Assert.ThrowsAny<ArgumentException>(() => PdfExporter.FromPng([1, 2, 3], 0, 60));
    }
}
