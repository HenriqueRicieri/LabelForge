using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class AnchorPlacementTests
{
    [Theory]
    [InlineData(Orientation.Normal)]
    [InlineData(Orientation.Rotated90)]
    [InlineData(Orientation.Rotated180)]
    [InlineData(Orientation.Rotated270)]
    public void TextKeepsItsDrawnBoundsWhenAnchorChanges(Orientation orientation) =>
        AssertRoundTrip(new TextElement
        {
            X = 220, Y = 310, Text = "Anchor", FontHeightDots = 39,
            Orientation = orientation,
        });

    [Fact]
    public void BarcodeKeepsSideDigitsInPlaceWhenAnchorChanges() =>
        AssertRoundTrip(new BarcodeElement
        {
            X = 220, Y = 310, Data = "5901234123457",
            Symbology = BarcodeSymbology.Ean13,
            Orientation = Orientation.Rotated90,
        });

    [Fact]
    public void QrKeepsItsDrawnOffsetInPlaceWhenAnchorChanges() =>
        AssertRoundTrip(new QrCodeElement { X = 220, Y = 310, Data = "ANCHOR" });

    [Fact]
    public void VerticalLineKeepsItsDrawnBoundsWhenAnchorChanges() =>
        AssertRoundTrip(new LineElement
        {
            X = 220, Y = 310, LengthDots = 241, ThicknessDots = 4,
            IsVertical = true,
        });

    private static void AssertRoundTrip(Element element)
    {
        var calculator = new ElementBoundsCalculator();
        DotRect before = calculator.GetBounds(element);
        int x = element.X;
        int y = element.Y;

        AnchorPlacement.Set(element, FieldAnchor.Baseline);
        Assert.Equal(FieldAnchor.Baseline, element.Anchor);
        Assert.Equal(before, calculator.GetBounds(element));

        AnchorPlacement.Set(element, FieldAnchor.TopLeft);
        Assert.Equal(before, calculator.GetBounds(element));
        Assert.Equal(x, element.X);
        Assert.Equal(y, element.Y);
    }
}
