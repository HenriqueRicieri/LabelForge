using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class RotationGestureTests
{
    [Theory]
    [InlineData(FieldAnchor.TopLeft)]
    [InlineData(FieldAnchor.Baseline)]
    public void Line_FollowsPreviewCentre_AndReturnsWithoutDrift(FieldAnchor anchor)
    {
        var line = new LineElement { X = 200, Y = 300, LengthDots = 301, ThicknessDots = 6, Anchor = anchor };
        var bounds = new ElementBoundsCalculator();
        DotRect before = bounds.GetBounds(line);
        for (int i = 0; i < 20; i++)
        {
            RotationGesture.Apply(line, Orientation.Rotated90, before);
            DotRect after = bounds.GetBounds(line);
            Assert.Equal(6, after.Width);
            Assert.Equal(301, after.Height);
            Assert.InRange(Math.Abs(after.X + after.Width / 2.0 - before.X - before.Width / 2.0), 0, 0.5);
            Assert.InRange(Math.Abs(after.Y + after.Height / 2.0 - before.Y - before.Height / 2.0), 0, 0.5);
            RotationGesture.Apply(line, Orientation.Normal, before);
            Assert.Equal(before, bounds.GetBounds(line));
        }
    }

    [Theory]
    [InlineData(FieldAnchor.TopLeft)]
    [InlineData(FieldAnchor.Baseline)]
    public void RepeatedQuarterTurns_ReturnLineToOriginalPosition(FieldAnchor anchor)
    {
        var line = new LineElement { X = 200, Y = 300, LengthDots = 301,
            ThicknessDots = 6, Anchor = anchor };
        var calculator = new ElementBoundsCalculator();
        DotRect original = calculator.GetBounds(line);

        for (int i = 0; i < 20; i++)
        {
            RotationGesture.Rotate90(line);
            DotRect turned = calculator.GetBounds(line);
            Assert.InRange(Math.Abs(turned.X + turned.Width / 2.0 - original.X - original.Width / 2.0), 0, 0.5);
            Assert.InRange(Math.Abs(turned.Y + turned.Height / 2.0 - original.Y - original.Height / 2.0), 0, 0.5);

            RotationGesture.Rotate90(line);
            Assert.Equal(original, calculator.GetBounds(line));
        }
    }

    [Theory]
    [InlineData(FieldAnchor.TopLeft)]
    [InlineData(FieldAnchor.Baseline)]
    public void ConsecutivePropertyTurns_ReturnLineToOriginalPosition(FieldAnchor anchor)
    {
        var line = new LineElement { X = 200, Y = 300, LengthDots = 301,
            ThicknessDots = 6, Anchor = anchor };
        var calculator = new ElementBoundsCalculator();
        DotRect original = calculator.GetBounds(line);

        RotationGesture.Apply(line, Orientation.Rotated90, calculator.GetBounds(line));
        RotationGesture.Apply(line, Orientation.Normal, calculator.GetBounds(line));

        Assert.Equal(original, calculator.GetBounds(line));
    }

    [Fact]
    public void RepeatedQuarterTurns_ReturnDiagonalToOriginalPosition()
    {
        var diagonal = new DiagonalLineElement { X = 100, Y = 100,
            WidthDots = 241, HeightDots = 80, ThicknessDots = 3 };
        var calculator = new ElementBoundsCalculator();
        DotRect original = calculator.GetBounds(diagonal);

        for (int i = 0; i < 20; i++)
        {
            RotationGesture.Rotate90(diagonal);
            RotationGesture.Rotate90(diagonal);
            Assert.Equal(original, calculator.GetBounds(diagonal));
        }
    }

    [Fact]
    public void FourQuarterTurns_ReturnBaselineTextToOriginalPosition()
    {
        var text = new TextElement { X = 200, Y = 300, Text = "Anchor",
            FontHeightDots = 39, Anchor = FieldAnchor.Baseline };
        var calculator = new ElementBoundsCalculator();
        DotRect original = calculator.GetBounds(text);

        for (int i = 0; i < 20; i++)
        {
            for (int turn = 0; turn < 4; turn++) RotationGesture.Rotate90(text);
            Assert.Equal(original, calculator.GetBounds(text));
            Assert.Equal(Orientation.Normal, text.Orientation);
        }
    }

    [Fact]
    public void Diagonal_TurnsItsBoxAroundThePreviewCentre()
    {
        var line = new DiagonalLineElement { X = 100, Y = 100, WidthDots = 240, HeightDots = 80 };
        var bounds = new ElementBoundsCalculator();
        DotRect before = bounds.GetBounds(line);
        RotationGesture.Apply(line, Orientation.Rotated90, before);
        Assert.Equal(new DotRect(180, 20, 80, 240), bounds.GetBounds(line));
        RotationGesture.Apply(line, Orientation.Normal, before);
        Assert.Equal(before, bounds.GetBounds(line));
    }
}
