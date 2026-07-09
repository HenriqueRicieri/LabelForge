using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

public sealed class ZplSizeAndMultiLabelTests
{
    [Fact]
    public void SizeScanner_ReadsPrintWidthAndLabelLength()
    {
        const string zpl = "^XA\n^PW812\n^LL1218\n^FO10,10^A0N,30^FDx^FS\n^XZ";
        Assert.Equal(812, ZplSizeScanner.PrintWidthDots(zpl));
        Assert.Equal(1218, ZplSizeScanner.LabelLengthDots(zpl));
    }

    [Fact]
    public void SizeScanner_ReturnsNull_WhenAbsent()
    {
        const string zpl = "^XA^FO10,10^A0N,30^FDx^FS^XZ";
        Assert.Null(ZplSizeScanner.PrintWidthDots(zpl));
        Assert.Null(ZplSizeScanner.LabelLengthDots(zpl));
    }

    [Fact]
    public void Renderer_ReportsLabelCount_ForMultipleBlocks()
    {
        const string zpl =
            "^XA^FO40,40^A0N,40^FDLabel one^FS^XZ" +
            "^XA^FO40,40^A0N,40^FDLabel two^FS^XZ";

        var renderer = new BinaryKitsRenderer();
        var result = renderer.Render(zpl, 100, 30, 8);

        Assert.Equal(2, result.LabelCount);
    }

    [Fact]
    public void Renderer_SelectsRequestedLabel_ProducingDifferentImages()
    {
        const string zpl =
            "^XA^FO40,40^A0N,40^FDLabel one^FS^XZ" +
            "^XA^FO40,40^A0N,40^FDcompletely different second label^FS^XZ";

        var renderer = new BinaryKitsRenderer();
        var first = renderer.Render(zpl, 100, 30, 8, labelIndex: 0);
        var second = renderer.Render(zpl, 100, 30, 8, labelIndex: 1);

        Assert.True(first.Png.Length > 0);
        Assert.True(second.Png.Length > 0);
        Assert.NotEqual(first.Png.Length, second.Png.Length);
    }

    [Fact]
    public void Renderer_ClampsOutOfRangeLabelIndex()
    {
        const string zpl = "^XA^FO40,40^A0N,40^FDonly^FS^XZ";
        var result = new BinaryKitsRenderer().Render(zpl, 100, 30, 8, labelIndex: 5);
        Assert.Equal(1, result.LabelCount);
        Assert.True(result.Png.Length > 0);
    }
}
