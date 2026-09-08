using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// Turning a field, for the two elements that turn without saying so in
/// <see cref="Element.Orientation"/>.
///
/// A line and a diagonal were turned by ticking a checkbox, which meant the canvas offered
/// no handle for something the printer will happily draw either way round. They turn through
/// the same handle and the same Ctrl + R as everything else now, and
/// <see cref="FieldRotation"/> is the one place that knows which property carries the turn.
///
/// What these hold, above all: the BYTES do not change. A line turned by the handle is the
/// vertical line that used to be ticked, to the character, or every label already on disk
/// would come back different.
/// </summary>
public sealed class FieldRotationTests
{
    private const int Dpmm = 8;

    private static LabelDocument Label(Element element)
    {
        var document = new LabelDocument { WidthMm = 100, HeightMm = 62.5, Dpmm = Dpmm };
        document.Elements.Add(element);
        return document;
    }

    private static string Zpl(Element element) => new ZplGenerator().Generate(Label(element));

    [Fact]
    public void CanRotate_CoversTheFieldsAQuarterTurnMeansSomethingFor()
    {
        Assert.True(FieldRotation.CanRotate(new LineElement()));
        Assert.True(FieldRotation.CanRotate(new DiagonalLineElement()));
        Assert.True(FieldRotation.CanRotate(new TextElement { Text = "x" }));
        Assert.True(FieldRotation.CanRotate(new BarcodeElement { Data = "123" }));

        // ^GB, ^GE and ^GF state a width and a height and draw them, so there is no turn to
        // offer and the handle stays hidden.
        Assert.False(FieldRotation.CanRotate(new BoxElement()));
        Assert.False(FieldRotation.CanRotate(new EllipseElement()));
        Assert.False(FieldRotation.CanRotate(new ImageElement()));
    }

    [Fact]
    public void Applies_IsUnchangedByCanRotateArriving()
    {
        // The two questions are different and both are still asked. Applies is what decides
        // whether Orientation reaches the ZPL, and the bounds calculator's side swap rides
        // on it; a line joining CanRotate must not join this.
        Assert.False(FieldRotation.Applies(new LineElement()));
        Assert.False(FieldRotation.Applies(new DiagonalLineElement()));
        Assert.False(FieldRotation.Applies(new BoxElement()));
        Assert.True(FieldRotation.Applies(new TextElement { Text = "x" }));
    }

    [Fact]
    public void Stops_AreTwoForABarAndFourForAField()
    {
        Assert.Equal(2, FieldRotation.Stops(new LineElement()));
        Assert.Equal(2, FieldRotation.Stops(new DiagonalLineElement()));
        Assert.Equal(4, FieldRotation.Stops(new TextElement { Text = "x" }));
    }

    [Fact]
    public void Rotate90_OnALine_FlipsVertical_AndTwiceIsIdentity()
    {
        var line = new LineElement { X = 40, Y = 60, LengthDots = 300, ThicknessDots = 4 };
        Assert.False(line.IsVertical);

        FieldRotation.Rotate90(line);
        Assert.True(line.IsVertical);
        Assert.Equal(Orientation.Rotated90, FieldRotation.Get(line));

        FieldRotation.Rotate90(line);
        Assert.False(line.IsVertical);
        Assert.Equal(Orientation.Normal, FieldRotation.Get(line));

        // The property the generator does read is the only one that moved.
        Assert.Equal(Orientation.Normal, line.Orientation);
    }

    [Fact]
    public void Rotate90_OnALine_GeneratesWhatTheTickedBoxGenerated()
    {
        var turned = new LineElement { X = 40, Y = 60, LengthDots = 300, ThicknessDots = 4 };
        FieldRotation.Rotate90(turned);

        var ticked = new LineElement
        {
            X = 40, Y = 60, LengthDots = 300, ThicknessDots = 4, IsVertical = true,
        };

        Assert.Equal(Zpl(ticked), Zpl(turned));
    }

    [Fact]
    public void Rotate90_OnADiagonal_FlipsTheLeanAndSwapsTheBox()
    {
        var diagonal = new DiagonalLineElement
        {
            X = 40, Y = 60, WidthDots = 200, HeightDots = 80, ThicknessDots = 3,
        };
        Assert.True(diagonal.LeansRight);

        FieldRotation.Rotate90(diagonal);

        // "/" turned clockwise runs the other way, in a box on its side.
        Assert.False(diagonal.LeansRight);
        Assert.Equal(80, diagonal.WidthDots);
        Assert.Equal(200, diagonal.HeightDots);
        Assert.Equal(Orientation.Rotated90, FieldRotation.Get(diagonal));

        FieldRotation.Rotate90(diagonal);

        Assert.True(diagonal.LeansRight);
        Assert.Equal(200, diagonal.WidthDots);
        Assert.Equal(80, diagonal.HeightDots);
        Assert.Equal(Orientation.Normal, FieldRotation.Get(diagonal));
    }

    [Fact]
    public void Rotate90_OnADiagonal_GeneratesWhatTheTickedBoxGenerated()
    {
        var turned = new DiagonalLineElement
        {
            X = 40, Y = 60, WidthDots = 200, HeightDots = 80, ThicknessDots = 3,
        };
        FieldRotation.Rotate90(turned);

        var ticked = new DiagonalLineElement
        {
            X = 40, Y = 60, WidthDots = 80, HeightDots = 200, ThicknessDots = 3,
            LeansRight = false,
        };

        Assert.Equal(Zpl(ticked), Zpl(turned));
    }

    [Fact]
    public void Set_LandsOnALegalStopWhenTheHandleIsDraggedPastOne()
    {
        // Two stops, so 180 is 0 and 270 is 90. The handle can be dragged the whole way
        // round and always arrives somewhere the element can actually be.
        var line = new LineElement();

        FieldRotation.Set(line, Orientation.Rotated180);
        Assert.False(line.IsVertical);

        FieldRotation.Set(line, Orientation.Rotated270);
        Assert.True(line.IsVertical);
        Assert.Equal(Orientation.Rotated90, FieldRotation.Get(line));

        var diagonal = new DiagonalLineElement { WidthDots = 200, HeightDots = 80 };
        FieldRotation.Set(diagonal, Orientation.Rotated270);
        Assert.False(diagonal.LeansRight);
        Assert.Equal(80, diagonal.WidthDots);

        // Asking for a stop it is already on does nothing, or the sides would swap twice.
        FieldRotation.Set(diagonal, Orientation.Rotated90);
        Assert.False(diagonal.LeansRight);
        Assert.Equal(80, diagonal.WidthDots);
    }

    [Fact]
    public void Set_NeverWritesOrientationOnSomethingTheGeneratorIgnoresItOn()
    {
        // The whole reason the pair exists: writing Element.Orientation on a line would turn
        // nothing and change the document while doing it.
        var line = new LineElement();
        var diagonal = new DiagonalLineElement();
        var box = new BoxElement();

        FieldRotation.Set(line, Orientation.Rotated90);
        FieldRotation.Set(diagonal, Orientation.Rotated90);
        FieldRotation.Set(box, Orientation.Rotated90);

        Assert.Equal(Orientation.Normal, line.Orientation);
        Assert.Equal(Orientation.Normal, diagonal.Orientation);
        Assert.Equal(Orientation.Normal, box.Orientation);
    }

    [Theory]
    [InlineData(Orientation.Normal)]
    [InlineData(Orientation.Rotated90)]
    [InlineData(Orientation.Rotated180)]
    [InlineData(Orientation.Rotated270)]
    public void Get_ReadsBackWhatSetWrote(Orientation orientation)
    {
        var text = new TextElement { Text = "x" };
        FieldRotation.Set(text, orientation);
        Assert.Equal(orientation, FieldRotation.Get(text));

        // For the two-stop elements the answer is the stop it landed on, which is what the
        // panel and the handle both read back.
        Orientation stop = orientation is Orientation.Rotated90 or Orientation.Rotated270
            ? Orientation.Rotated90
            : Orientation.Normal;

        var line = new LineElement();
        FieldRotation.Set(line, orientation);
        Assert.Equal(stop, FieldRotation.Get(line));

        var diagonal = new DiagonalLineElement();
        FieldRotation.Set(diagonal, orientation);
        Assert.Equal(stop, FieldRotation.Get(diagonal));
    }

    [Fact]
    public void Rotate90_LeavesAloneWhatCannotTurn()
    {
        var box = new BoxElement { WidthDots = 200, HeightDots = 80 };
        string before = Zpl(box);

        FieldRotation.Rotate90(box);

        Assert.Equal(before, Zpl(box));
        Assert.Equal(200, box.WidthDots);
        Assert.Equal(80, box.HeightDots);
    }
}
