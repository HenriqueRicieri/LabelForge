using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class SelectedQuietZoneTests
{
    [Theory]
    [InlineData(Orientation.Normal)]
    [InlineData(Orientation.Rotated90)]
    [InlineData(Orientation.Rotated180)]
    [InlineData(Orientation.Rotated270)]
    public void SelectedWarningsMatchTheFullReport(Orientation orientation)
    {
        var document = new LabelDocument { WidthMm = 120, HeightMm = 100, Dpmm = 8 };
        for (int i = 0; i < 78; i++)
        {
            Element element = (i % 3) switch
            {
                0 => new TextElement { Text = $"Product {i}", FontHeightDots = 20 },
                1 => new BarcodeElement { Data = $"ABC{i:000}", ModuleWidthDots = 1, HeightDots = 30, PrintInterpretationLine = false },
                _ => new QrCodeElement { Data = $"Item {i}", Magnification = 1 },
            };
            element.X = 30 + i % 6 * 150;
            element.Y = 30 + i / 6 * 55;
            element.Orientation = orientation;
            document.Elements.Add(element);
        }

        foreach (bool continuous in new[] { false, true })
        {
            document.IsContinuous = continuous;
            var findings = QuietZoneChecker.Check(document);
            foreach (Element element in document.Elements)
                Assert.Equal(findings.Any(f => ReferenceEquals(f.Code, element)),
                    QuietZoneChecker.IsCrowded(document, element));
        }
    }

    [Fact]
    public void SelectedWarningUpdatesWhenTheNeighbourOrSymbolChanges()
    {
        var code = new BarcodeElement { X = 200, Y = 150, Data = "ABC", ModuleWidthDots = 2, HeightDots = 80, PrintInterpretationLine = false };
        var neighbour = new BoxElement { X = 100, Y = 150, WidthDots = 92, HeightDots = 40 };
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        document.Elements.Add(code);
        document.Elements.Add(neighbour);

        Assert.True(QuietZoneChecker.IsCrowded(document, code));
        neighbour.WidthDots = 79;
        Assert.False(QuietZoneChecker.IsCrowded(document, code));
        neighbour.WidthDots = 92;
        neighbour.IsVisible = false;
        Assert.False(QuietZoneChecker.IsCrowded(document, code));
        neighbour.IsVisible = true;
        neighbour.DoNotPrint = true;
        Assert.False(QuietZoneChecker.IsCrowded(document, code));
        neighbour.DoNotPrint = false;
        code.DoNotPrint = true;
        Assert.False(QuietZoneChecker.IsCrowded(document, code));
        code.DoNotPrint = false;
        code.IsVisible = false;
        Assert.False(QuietZoneChecker.IsCrowded(document, code));
        code.IsVisible = true;
        document.CheckQuietZones = false;
        Assert.False(QuietZoneChecker.IsCrowded(document, code));
        document.CheckQuietZones = true;
        document.Elements.Remove(code);
        Assert.False(QuietZoneChecker.IsCrowded(document, code));
    }

    [Fact]
    public void SelectedWarningRespectsTheEmptyInteriorOfAFrame()
    {
        var code = new QrCodeElement { X = 200, Y = 150, Data = "LabelForge", Magnification = 2 };
        DotRect zone = QuietZone.For(code).Around(new ElementBoundsCalculator().GetBounds(code));
        var frame = new BoxElement
        {
            X = zone.X - 10, Y = zone.Y - 10,
            WidthDots = zone.Width + 20, HeightDots = zone.Height + 20, ThicknessDots = 10,
        };
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        document.Elements.Add(code);
        document.Elements.Add(frame);

        Assert.False(QuietZoneChecker.IsCrowded(document, code));
        frame.WidthDots--;
        Assert.True(QuietZoneChecker.IsCrowded(document, code));
    }

    [Fact]
    public void SelectedWarningRespectsContinuousStockAndTheSideEdge()
    {
        var code = new BarcodeElement { X = 200, Y = 450, Data = "ABC", HeightDots = 80, PrintInterpretationLine = false };
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        document.Elements.Add(code);

        Assert.True(QuietZoneChecker.IsCrowded(document, code));
        document.IsContinuous = true;
        Assert.False(QuietZoneChecker.IsCrowded(document, code));
        code.X = 0;
        Assert.True(QuietZoneChecker.IsCrowded(document, code));
    }
}
