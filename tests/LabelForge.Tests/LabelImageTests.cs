using LabelForge.Core.Export;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;

namespace LabelForge.Tests;

/// <summary>
/// PNG and PDF exports are for looking at: approval, documentation, a ticket. They show
/// what the canvas shows, sample values and real counter and date included, but only
/// what prints. Rendering the print ZPL instead drew markers literally, left a printer
/// counter blank, wrote a printer-clock date as "%d/%m/%Y" and returned no image at all
/// for an EAN-13 holding a marker.
/// </summary>
public sealed class LabelImageTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 14, 0, 0);

    private static LabelDocument Template()
    {
        var document = new LabelDocument { WidthMm = 80, HeightMm = 50, Dpmm = 8 };
        document.Elements.Add(new TextElement { X = 30, Y = 25, Text = "Produto: ##PRODUTO##", FontHeightDots = 30 });
        document.Elements.Add(new TextElement { X = 30, Y = 70, Text = "Serie: ##SERIE##", FontHeightDots = 30, ZOrder = 1 });
        document.Elements.Add(new TextElement { X = 330, Y = 70, Text = "##DATA##", FontHeightDots = 30, ZOrder = 2 });
        document.Elements.Add(new BarcodeElement
        {
            X = 30, Y = 130, Data = "##EAN##", HeightDots = 100, Symbology = BarcodeSymbology.Ean13, ZOrder = 3,
        });
        document.SampleValues["PRODUTO"] = "Queijo";
        document.SampleValues["EAN"] = "789123456789";
        document.Variables["SERIE"] = new VariableDefinition
        {
            Kind = VariableKind.Counter, CounterStart = 7, CounterPadding = 4, UsePrinterCounter = true,
        };
        document.Variables["DATA"] = new VariableDefinition
        {
            Kind = VariableKind.Clock, ClockFormat = "dd/MM/yyyy", UsePrinterClock = true,
        };
        return document;
    }

    [Fact]
    public void ShowsTheSamplesTheCounterAndTheDate()
    {
        string zpl = LabelImage.Zpl(Template(), Now);

        Assert.Contains("^FDProduto: Queijo^FS", zpl, StringComparison.Ordinal);
        Assert.Contains("^FDSerie: 0007^FS", zpl, StringComparison.Ordinal);
        Assert.Contains("^FD07/10/2026^FS", zpl, StringComparison.Ordinal);
        Assert.Contains("^FD789123456789^FS", zpl, StringComparison.Ordinal);
        Assert.DoesNotContain("##", zpl, StringComparison.Ordinal);
        Assert.DoesNotContain("^SN", zpl, StringComparison.Ordinal);
        Assert.DoesNotContain("^FC", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesOutWhatDoesNotPrint()
    {
        LabelDocument document = Template();
        document.Elements.Add(new TextElement { X = 30, Y = 300, Text = "kept out", FontHeightDots = 30, DoNotPrint = true });
        document.Elements.Add(new TextElement { X = 900, Y = 30, Text = "parked", FontHeightDots = 30 });

        string zpl = LabelImage.Zpl(document, Now);

        Assert.DoesNotContain("kept out", zpl, StringComparison.Ordinal);
        Assert.DoesNotContain("parked", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersAnImageForATemplate()
    {
        byte[] png = LabelImage.Png(new BinaryKitsRenderer(), Template(), Now);

        Assert.NotEmpty(png);
    }

    /// <summary>A label the renderer cannot draw is an error, never an empty file
    /// reported as exported.</summary>
    [Fact]
    public void ALabelThatCannotBeDrawnIsAnError()
    {
        LabelDocument document = Template();
        document.SampleValues["EAN"] = "not digits";

        var error = Assert.Throws<InvalidOperationException>(
            () => LabelImage.Png(new BinaryKitsRenderer(), document, Now));
        Assert.Contains("could not be drawn", error.Message, StringComparison.Ordinal);
    }
}
