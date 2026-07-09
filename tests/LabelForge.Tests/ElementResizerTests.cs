using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class ElementResizerTests
{
    private readonly ElementBoundsCalculator _bounds = new();

    [Fact]
    public void Box_ResizesFreely_WithMinimum()
    {
        var box = new BoxElement { WidthDots = 100, HeightDots = 100 };

        ElementResizer.Resize(box, 250, 180);
        Assert.Equal((250, 180), (box.WidthDots, box.HeightDots));

        ElementResizer.Resize(box, 1, 1);
        Assert.Equal((4, 4), (box.WidthDots, box.HeightDots));
    }

    [Fact]
    public void Line_ResizesAlongItsAxisOnly()
    {
        var horizontal = new LineElement { LengthDots = 100, ThicknessDots = 3 };
        ElementResizer.Resize(horizontal, 400, 999);
        Assert.Equal(400, horizontal.LengthDots);
        Assert.Equal(3, horizontal.ThicknessDots);

        var vertical = new LineElement { LengthDots = 100, ThicknessDots = 3, IsVertical = true };
        ElementResizer.Resize(vertical, 999, 250);
        Assert.Equal(250, vertical.LengthDots);
    }

    [Fact]
    public void Text_ScalesHeight_AndProportionalExplicitWidth()
    {
        var auto = new TextElement { FontHeightDots = 40 };
        ElementResizer.Resize(auto, 0, 80);
        Assert.Equal(80, auto.FontHeightDots);
        Assert.Equal(0, auto.FontWidthDots);

        var fixedWidth = new TextElement { FontHeightDots = 40, FontWidthDots = 30 };
        ElementResizer.Resize(fixedWidth, 0, 80);
        Assert.Equal(80, fixedWidth.FontHeightDots);
        Assert.Equal(60, fixedWidth.FontWidthDots);
    }

    [Fact]
    public void Barcode_SnapsModuleWidth_WhenWidthDoubles()
    {
        var barcode = new BarcodeElement { Data = "12345678", ModuleWidthDots = 2, HeightDots = 100 };
        int originalWidth = _bounds.GetBounds(barcode).Width;

        ElementResizer.Resize(barcode, originalWidth * 2, barcode.HeightDots + 30);

        Assert.Equal(4, barcode.ModuleWidthDots);
    }

    [Fact]
    public void Barcode_ModuleWidth_StaysWithinZplRange()
    {
        var barcode = new BarcodeElement { Data = "1", ModuleWidthDots = 2, HeightDots = 100 };

        ElementResizer.Resize(barcode, 100_000, 130);
        Assert.Equal(10, barcode.ModuleWidthDots);

        ElementResizer.Resize(barcode, 1, 130);
        Assert.Equal(1, barcode.ModuleWidthDots);
    }

    [Fact]
    public void Qr_SnapsMagnification()
    {
        var qr = new QrCodeElement { Data = "HELLO", Magnification = 2 };
        int side = _bounds.GetBounds(qr).Width;

        ElementResizer.Resize(qr, side * 3, side * 3);

        Assert.Equal(6, qr.Magnification);
    }
}
