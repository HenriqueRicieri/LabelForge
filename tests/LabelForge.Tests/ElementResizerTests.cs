using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

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
    public void Text_EdgeHandles_ChangeOnlyTheirOwnFontDimension()
    {
        var text = new TextElement { Text = "WIDE", FontHeightDots = 40 };
        TextResizeStart start = ElementResizer.CaptureText(text);

        ElementResizer.ResizeText(text, start, start.BoundsWidthDots, 80, TextResizeMode.Height);
        Assert.Equal(80, text.FontHeightDots);
        Assert.Equal(40, text.FontWidthDots);
        Assert.Equal(start.BoundsWidthDots, _bounds.GetLocalBounds(text).Width);

        ElementResizer.ResizeText(text, start, start.BoundsWidthDots * 2,
            start.BoundsHeightDots, TextResizeMode.Width);
        Assert.Equal(40, text.FontHeightDots);
        Assert.Equal(80, text.FontWidthDots);
        Assert.InRange(_bounds.GetLocalBounds(text).Width, start.BoundsWidthDots * 2 - 1,
            start.BoundsWidthDots * 2 + 1);

        ElementResizer.ResizeText(text, start, start.BoundsWidthDots + 1,
            start.BoundsHeightDots, TextResizeMode.Width);
        Assert.Equal((40, 0), (text.FontHeightDots, text.FontWidthDots));

        ElementResizer.ResizeText(text, start, start.BoundsWidthDots,
            start.BoundsHeightDots, TextResizeMode.Width);
        Assert.Equal((40, 0), (text.FontHeightDots, text.FontWidthDots));
    }

    [Fact]
    public void Text_CornerPreservesAutomaticWidth_AndShiftAllowsFreeAxes()
    {
        var text = new TextElement { Text = "WIDE", FontHeightDots = 40 };
        TextResizeStart start = ElementResizer.CaptureText(text);

        ElementResizer.ResizeText(text, start, start.BoundsWidthDots * 2, 80,
            TextResizeMode.Proportional);
        Assert.Equal((80, 0), (text.FontHeightDots, text.FontWidthDots));

        ElementResizer.ResizeText(text, start, start.BoundsWidthDots * 2, 60,
            TextResizeMode.Free);
        Assert.Equal((60, 80), (text.FontHeightDots, text.FontWidthDots));
    }

    [Fact]
    public void TextResizeEmitsExplicitWidthOnlyWhenTheAxesSeparate()
    {
        var text = new TextElement { Text = "SIZE", FontHeightDots = 40 };
        var document = new LabelDocument();
        document.Elements.Add(text);
        TextResizeStart start = ElementResizer.CaptureText(text);

        ElementResizer.ResizeText(text, start, start.BoundsWidthDots, 80, TextResizeMode.Height);
        Assert.Contains("^A0N,80,40^FDSIZE", new ZplGenerator().Generate(document),
            StringComparison.Ordinal);

        ElementResizer.ResizeText(text, start, start.BoundsWidthDots * 2, 80,
            TextResizeMode.Proportional);
        Assert.Contains("^A0N,80^FDSIZE", new ZplGenerator().Generate(document),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TextBlockResizeKeepsItsSelectionWidthUnderTheHandle()
    {
        var text = new TextElement { Text = "WRAPPED WORDS", FontHeightDots = 40,
            BlockWidthDots = 200, BlockMaxLines = 3 };
        TextResizeStart start = ElementResizer.CaptureText(text);

        ElementResizer.ResizeText(text, start, 300, start.BoundsHeightDots,
            TextResizeMode.Width);
        Assert.Equal((40, 60, 300),
            (text.FontHeightDots, text.FontWidthDots, text.BlockWidthDots));
        Assert.Equal(300, _bounds.GetLocalBounds(text).Width);
        Assert.Equal(start.BoundsHeightDots, _bounds.GetLocalBounds(text).Height);

        ElementResizer.ResizeText(text, start, start.BoundsWidthDots,
            start.BoundsHeightDots * 2, TextResizeMode.Height);
        Assert.Equal((80, 40, 200),
            (text.FontHeightDots, text.FontWidthDots, text.BlockWidthDots));

        ElementResizer.ResizeText(text, start, 300, start.BoundsHeightDots * 3 / 2,
            TextResizeMode.Proportional);
        Assert.Equal((60, 0, 300),
            (text.FontHeightDots, text.FontWidthDots, text.BlockWidthDots));

        ElementResizer.ResizeText(text, start, start.BoundsWidthDots,
            start.BoundsHeightDots, TextResizeMode.Width);
        Assert.Equal((40, 0, 200),
            (text.FontHeightDots, text.FontWidthDots, text.BlockWidthDots));
    }

    [Fact]
    public void BitmapText_EdgesSnapToWholeCellMultiples()
    {
        var text = new TextElement { Text = "WIDE", Font = 'A', FontHeightDots = 27 };
        TextResizeStart start = ElementResizer.CaptureText(text);

        ElementResizer.ResizeText(text, start, start.BoundsWidthDots * 2,
            start.BoundsHeightDots, TextResizeMode.Width);
        Assert.Equal((27, 30), (text.FontHeightDots, text.FontWidthDots));

        ElementResizer.ResizeText(text, start, start.BoundsWidthDots, 54,
            TextResizeMode.Height);
        Assert.Equal((54, 15), (text.FontHeightDots, text.FontWidthDots));
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
