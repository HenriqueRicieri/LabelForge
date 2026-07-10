using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class ElementBoundsTests
{
    private readonly ElementBoundsCalculator _sut = new();

    [Fact]
    public void Box_BoundsAreExact()
    {
        var box = new BoxElement { X = 10, Y = 20, WidthDots = 300, HeightDots = 150 };
        Assert.Equal(new DotRect(10, 20, 300, 150), _sut.GetBounds(box));
    }

    [Fact]
    public void Line_SwapsDimensions_WhenVertical()
    {
        var line = new LineElement { X = 5, Y = 5, LengthDots = 200, ThicknessDots = 4 };
        Assert.Equal(new DotRect(5, 5, 200, 4), _sut.GetBounds(line));

        line.IsVertical = true;
        Assert.Equal(new DotRect(5, 5, 4, 200), _sut.GetBounds(line));
    }

    [Fact]
    public void Text_WidthGrowsWithContent()
    {
        var shortText = new TextElement { Text = "AB", FontHeightDots = 40 };
        var longText = new TextElement { Text = "ABCDEFGH", FontHeightDots = 40 };

        Assert.True(_sut.GetBounds(longText).Width > _sut.GetBounds(shortText).Width);
        Assert.Equal(40, _sut.GetBounds(shortText).Height);
    }

    [Fact]
    public void Barcode_WidthScalesWithModuleWidth()
    {
        var narrow = new BarcodeElement { Data = "12345678", ModuleWidthDots = 2, HeightDots = 100 };
        var wide = new BarcodeElement { Data = "12345678", ModuleWidthDots = 4, HeightDots = 100 };

        Assert.Equal(_sut.GetBounds(narrow).Width * 2, _sut.GetBounds(wide).Width);
    }

    [Fact]
    public void Qr_SideScalesWithMagnification()
    {
        var small = new QrCodeElement { Data = "HELLO", Magnification = 2 };
        var large = new QrCodeElement { Data = "HELLO", Magnification = 6 };

        var smallBounds = _sut.GetBounds(small);
        Assert.Equal(smallBounds.Width, smallBounds.Height);
        Assert.Equal(smallBounds.Width * 3, _sut.GetBounds(large).Width);
    }

    [Fact]
    public void Qr_BoundsMirrorBinaryKitsTenDotVerticalOffset()
    {
        var qr = new QrCodeElement { X = 100, Y = 100, Data = "HELLO", Magnification = 4 };
        Assert.Equal(110, _sut.GetBounds(qr).Y);
        Assert.Equal(100, _sut.GetBounds(qr).X);
    }

    [Fact]
    public void Rotation_SwapsWidthAndHeight()
    {
        var text = new TextElement { Text = "Hello", FontHeightDots = 40 };
        var normal = _sut.GetBounds(text);

        text.Orientation = Orientation.Rotated90;
        var rotated = _sut.GetBounds(text);

        Assert.Equal(normal.Width, rotated.Height);
        Assert.Equal(normal.Height, rotated.Width);
    }

    [Fact]
    public void Contains_ChecksPointMembership()
    {
        var rect = new DotRect(10, 10, 100, 50);
        Assert.True(rect.Contains(10, 10));
        Assert.True(rect.Contains(109, 59));
        Assert.False(rect.Contains(110, 30));
        Assert.False(rect.Contains(9, 30));
    }
}
