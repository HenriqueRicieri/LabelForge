using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

public sealed class QuietZoneFrameTests
{
    private static BarcodeElement Barcode() => new()
    {
        X = 200, Y = 150, Data = "ABC12345", HeightDots = 80,
        ModuleWidthDots = 2, PrintInterpretationLine = false,
    };

    private static LabelDocument Label(params Element[] elements)
    {
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        foreach (var element in elements) document.Elements.Add(element);
        return document;
    }

    private static BoxElement Frame(Element code, int gap = 10)
    {
        DotRect zone = QuietZone.For(code).Around(new ElementBoundsCalculator().GetBounds(code));
        return new BoxElement
        {
            X = zone.X - 10 - gap, Y = zone.Y - 10 - gap,
            WidthDots = zone.Width + 20 + 2 * gap, HeightDots = zone.Height + 20 + 2 * gap,
            ThicknessDots = 10,
        };
    }

    [Theory]
    [InlineData(Orientation.Normal)]
    [InlineData(Orientation.Rotated90)]
    [InlineData(Orientation.Rotated180)]
    [InlineData(Orientation.Rotated270)]
    public void AFrameAroundTheQuietZone_DoesNotCrowdTheBarcode(Orientation orientation)
    {
        var code = Barcode();
        code.X = 300;
        code.Y = 100;
        code.Orientation = orientation;
        Assert.Empty(QuietZoneChecker.Check(Label(code, Frame(code))));
    }

    [Fact]
    public void AFrameAroundAQrCode_ReservesAllFourMargins()
    {
        var code = new QrCodeElement { X = 200, Y = 150, Data = "LabelForge", Magnification = 2 };
        Assert.Empty(QuietZoneChecker.Check(Label(code, Frame(code))));
    }

    [Fact]
    public void AnInnerEdgeExactlyTouchingTheQuietZone_IsClear()
    {
        var code = Barcode();
        Assert.Empty(QuietZoneChecker.Check(Label(code, Frame(code, gap: 0))));
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("top")]
    [InlineData("bottom")]
    public void OneDotOfBorderInsideTheQuietZone_IsReported(string edge)
    {
        var code = Barcode();
        var frame = Frame(code);
        switch (edge)
        {
            case "left": frame.X += 11; frame.WidthDots -= 11; break;
            case "right": frame.WidthDots -= 11; break;
            case "top": frame.Y += 11; frame.HeightDots -= 11; break;
            case "bottom": frame.HeightDots -= 11; break;
        }
        var finding = Assert.Single(QuietZoneChecker.Check(Label(code, frame)));
        Assert.Same(code, finding.Code);
        Assert.Same(frame, finding.Intruder);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(61)]
    [InlineData(int.MaxValue)]
    public void AThickBoxStillCrowdsItsInterior(int thickness)
    {
        var code = Barcode();
        var frame = Frame(code);
        frame.ThicknessDots = thickness;
        Assert.Same(frame, Assert.Single(QuietZoneChecker.Check(Label(code, frame))).Intruder);
    }

    [Fact]
    public void AFrameDoesNotHideOtherIntruders()
    {
        var code = Barcode();
        var intruder = new BoxElement { X = 185, Y = 160, WidthDots = 5, HeightDots = 5, ThicknessDots = 5 };
        Assert.Same(intruder, Assert.Single(QuietZoneChecker.Check(Label(code, Frame(code), intruder))).Intruder);
    }

    [Fact]
    public void RoundedBoxesAndEllipsesKeepTheirConservativeFootprints()
    {
        var code = Barcode();
        var frame = Frame(code);
        frame.CornerRoundness = 8;
        Assert.Same(frame, Assert.Single(QuietZoneChecker.Check(Label(code, frame))).Intruder);
        var ellipse = new EllipseElement
        {
            X = frame.X, Y = frame.Y, WidthDots = frame.WidthDots, HeightDots = frame.HeightDots, ThicknessDots = 10,
        };
        Assert.Same(ellipse, Assert.Single(QuietZoneChecker.Check(Label(code, ellipse))).Intruder);
    }

    [Fact]
    public void CheckingAFrameLeavesTheDocumentAndZplUntouched()
    {
        var code = Barcode();
        var document = Label(code, Frame(code));
        string json = LabelDocumentJson.Serialize(document);
        string zpl = new ZplGenerator().Generate(document);
        QuietZoneChecker.Check(document);
        Assert.Equal(json, LabelDocumentJson.Serialize(document));
        Assert.Equal(zpl, new ZplGenerator().Generate(document));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(10, false)]
    [InlineData(10, true)]
    public void RendererLeavesTheRectangularFrameInteriorBlank(int thickness, bool reversed)
    {
        var frame = new BoxElement
        {
            X = 50, Y = 50, WidthDots = 160, HeightDots = 100,
            ThicknessDots = thickness, IsReversed = reversed,
        };
        var document = Label(frame);
        var result = new BinaryKitsRenderer().Render(new ZplGenerator().Generate(document), 100, 60, 8);
        Assert.Empty(result.Errors);
        using var image = SKBitmap.Decode(result.Png);
        Assert.NotNull(image);
        int interiorInk = 0;
        for (int y = frame.Y + thickness; y < frame.Y + frame.HeightDots - thickness; y++)
            for (int x = frame.X + thickness; x < frame.X + frame.WidthDots - thickness; x++)
                if (image.GetPixel(x, y).Red < 128) interiorInk++;
        Assert.Equal(0, interiorInk);
        Assert.True(image.GetPixel(frame.X, frame.Y + frame.HeightDots / 2).Red < 128);
    }
}
