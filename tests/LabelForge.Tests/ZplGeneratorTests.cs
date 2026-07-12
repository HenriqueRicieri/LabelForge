using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// Golden tests for the ZPL generator: exact output for each element type plus the
/// classic pitfalls (^FH escaping, dpmm rounding, z-order, visibility). Explicit
/// expected strings double as living documentation of the emitted format.
/// </summary>
public sealed class ZplGeneratorTests
{
    private static string Header(int widthDots, int heightDots) =>
        $"^XA\n^CI28\n^PW{widthDots}\n^LL{heightDots}\n^LH0,0\n";

    private static LabelDocument Doc(params Element[] elements)
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 150, Dpmm = 8 }; // 800 x 1200 dots
        foreach (var e in elements)
        {
            doc.Elements.Add(e);
        }

        return doc;
    }

    [Fact]
    public void Text_EmitsFontZeroAndFieldData()
    {
        var zpl = new ZplGenerator().Generate(Doc(
            new TextElement { X = 50, Y = 60, FontHeightDots = 30, Text = "Hello" }));

        Assert.Equal(Header(800, 1200) + "^FO50,60^A0N,30^FDHello^FS\n^XZ", zpl);
    }

    [Fact]
    public void Text_WithExplicitWidthAndRotation()
    {
        var zpl = new ZplGenerator().Generate(Doc(
            new TextElement
            {
                X = 10, Y = 20, FontHeightDots = 40, FontWidthDots = 35,
                Orientation = Orientation.Rotated270, Text = "Seq",
            }));

        Assert.Equal(Header(800, 1200) + "^FO10,20^A0B,40,35^FDSeq^FS\n^XZ", zpl);
    }

    [Fact]
    public void Box_EmitsGraphicBox()
    {
        var zpl = new ZplGenerator().Generate(Doc(
            new BoxElement { X = 53, Y = 510, WidthDots = 421, HeightDots = 372, ThicknessDots = 2 }));

        Assert.Equal(Header(800, 1200) + "^FO53,510^GB421,372,2,B^FS\n^XZ", zpl);
    }

    [Fact]
    public void Line_HorizontalAndVertical_AreSolidBars()
    {
        var horizontal = new ZplGenerator().Generate(Doc(
            new LineElement { X = 5, Y = 5, LengthDots = 300, ThicknessDots = 4, IsVertical = false }));
        Assert.Equal(Header(800, 1200) + "^FO5,5^GB300,4,4,B^FS\n^XZ", horizontal);

        var vertical = new ZplGenerator().Generate(Doc(
            new LineElement { X = 5, Y = 5, LengthDots = 300, ThicknessDots = 4, IsVertical = true }));
        Assert.Equal(Header(800, 1200) + "^FO5,5^GB4,300,4,B^FS\n^XZ", vertical);
    }

    [Fact]
    public void Barcode_Code128_EmitsByAndBc()
    {
        var zpl = new ZplGenerator().Generate(Doc(
            new BarcodeElement
            {
                X = 100, Y = 200, Symbology = BarcodeSymbology.Code128,
                Data = "ABC123", HeightDots = 120, ModuleWidthDots = 3,
            }));

        Assert.Equal(Header(800, 1200) + "^BY3^FO100,200^BCN,120,Y,N,N^FDABC123^FS\n^XZ", zpl);
    }

    [Fact]
    public void Barcode_Code39_IncludesRatio()
    {
        var zpl = new ZplGenerator().Generate(Doc(
            new BarcodeElement
            {
                X = 0, Y = 0, Symbology = BarcodeSymbology.Code39,
                Data = "CODE39", HeightDots = 80, ModuleWidthDots = 2, WideBarRatio = 3.0,
                PrintInterpretationLine = false,
            }));

        Assert.Equal(Header(800, 1200) + "^BY2,3.0^FO0,0^B3N,N,80,N,N^FDCODE39^FS\n^XZ", zpl);
    }

    [Fact]
    public void Barcode_Ean13_EmitsBe()
    {
        var zpl = new ZplGenerator().Generate(Doc(
            new BarcodeElement
            {
                X = 40, Y = 40, Symbology = BarcodeSymbology.Ean13,
                Data = "123456789012", HeightDots = 100, ModuleWidthDots = 2,
            }));

        Assert.Equal(Header(800, 1200) + "^BY2^FO40,40^BEN,100,Y,N^FD123456789012^FS\n^XZ", zpl);
    }

    [Fact]
    public void Qr_EmitsBqWithErrorCorrectionPrefix()
    {
        var zpl = new ZplGenerator().Generate(Doc(
            new QrCodeElement
            {
                X = 290, Y = 155, Data = "HELLO", Magnification = 8,
                ErrorCorrection = QrErrorCorrection.Low,
            }));

        Assert.Equal(Header(800, 1200) + "^FO290,155^BQN,2,8^FDLA,HELLO^FS\n^XZ", zpl);
    }

    [Fact]
    public void FieldData_EscapesControlCharactersWithFieldHex()
    {
        var zpl = new ZplGenerator().Generate(Doc(
            new TextElement { X = 0, Y = 0, FontHeightDots = 30, Text = "A^B~C_D" }));

        Assert.Equal(Header(800, 1200) + "^FO0,0^A0N,30^FH_^FDA_5EB_7EC_5FD^FS\n^XZ", zpl);
    }

    [Fact]
    public void Generate_OrdersByZOrder_AndSkipsInvisible()
    {
        var zpl = new ZplGenerator().Generate(Doc(
            new TextElement { X = 0, Y = 0, FontHeightDots = 30, Text = "second", ZOrder = 2 },
            new TextElement { X = 0, Y = 0, FontHeightDots = 30, Text = "first", ZOrder = 1 },
            new TextElement { X = 0, Y = 0, FontHeightDots = 30, Text = "hidden", ZOrder = 3, IsVisible = false }));

        Assert.Equal(
            Header(800, 1200) +
            "^FO0,0^A0N,30^FDfirst^FS\n^FO0,0^A0N,30^FDsecond^FS\n^XZ",
            zpl);
    }

    [Fact]
    public void Generate_SkipsElementsWhoseOriginIsOffTheLabel()
    {
        // ZPL cannot express a negative ^FO, and an origin past the edge prints
        // nothing; the export path drops all three and keeps the label clean.
        var zpl = new ZplGenerator().Generate(Doc(
            new TextElement { X = -5, Y = 10, FontHeightDots = 30, Text = "negative" },
            new TextElement { X = 810, Y = 10, FontHeightDots = 30, Text = "past-right" },
            new TextElement { X = 10, Y = 1500, FontHeightDots = 30, Text = "past-bottom" },
            new TextElement { X = 10, Y = 10, FontHeightDots = 30, Text = "inside" }));

        Assert.Equal(Header(800, 1200) + "^FO10,10^A0N,30^FDinside^FS\n^XZ", zpl);
    }

    [Fact]
    public void Generate_KeepsElementsThatOverflowTheEdge()
    {
        // Origin on the label, footprint past the right edge: emitted as-is, the
        // printer clips the overflow at the edge.
        var zpl = new ZplGenerator().Generate(Doc(
            new BoxElement { X = 700, Y = 10, WidthDots = 300, HeightDots = 50, ThicknessDots = 2 }));

        Assert.Equal(Header(800, 1200) + "^FO700,10^GB300,50,2,B^FS\n^XZ", zpl);
    }

    [Fact]
    public void GeneratePreview_OffsetsOriginsAndKeepsOffLabelElements()
    {
        var zpl = new ZplGenerator().GeneratePreview(Doc(
            new TextElement { X = -40, Y = 10, FontHeightDots = 30, Text = "parked" },
            new TextElement { X = 10, Y = 20, FontHeightDots = 30, Text = "inside" }),
            offsetDots: 160);

        Assert.Equal(
            Header(1120, 1520) +
            "^FO120,170^A0N,30^FDparked^FS\n^FO170,180^A0N,30^FDinside^FS\n^XZ",
            zpl);
    }

    [Fact]
    public void GeneratePreview_SkipsElementsBeyondThePasteboard()
    {
        var zpl = new ZplGenerator().GeneratePreview(Doc(
            new TextElement { X = -200, Y = 0, FontHeightDots = 30, Text = "too far" }),
            offsetDots: 160);

        Assert.Equal("^XA\n^CI28\n^PW1120\n^LL1520\n^LH0,0\n^XZ", zpl);
    }

    [Fact]
    public void Density_ChangesPrintWidthAndLength()
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 150, Dpmm = 12 }; // 1200 x 1800
        var zpl = new ZplGenerator().Generate(doc);
        Assert.Equal("^XA\n^CI28\n^PW1200\n^LL1800\n^LH0,0\n^XZ", zpl);
    }

    [Fact]
    public void KitchenSink_RendersToANonEmptyImage()
    {
        var doc = Doc(
            new BoxElement { X = 10, Y = 10, WidthDots = 780, HeightDots = 1180, ThicknessDots = 3 },
            new TextElement { X = 40, Y = 40, FontHeightDots = 40, Text = "LabelForge" },
            new BarcodeElement { X = 40, Y = 120, Symbology = BarcodeSymbology.Code128, Data = "ABC123", HeightDots = 120 },
            new BarcodeElement { X = 40, Y = 320, Symbology = BarcodeSymbology.Ean13, Data = "123456789012", HeightDots = 100 },
            new QrCodeElement { X = 500, Y = 120, Data = "https://labelforge.app", Magnification = 6 });

        string zpl = new ZplGenerator().Generate(doc);
        RenderResult result = new BinaryKitsRenderer().Render(zpl, doc.WidthMm, doc.HeightMm, doc.Dpmm);

        Assert.Empty(result.Errors);
        Assert.True(result.Png.Length > 0, "kitchen-sink label produced no image");
    }
}
