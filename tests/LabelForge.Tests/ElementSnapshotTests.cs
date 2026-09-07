using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// Escape mid-gesture puts things back, and this is what "back" means. The case that
/// decides the design is the resize: it quantizes, so undoing it by arithmetic does not
/// return the original value, and only a copy of the original does.
/// </summary>
public sealed class ElementSnapshotTests
{
    [Fact]
    public void APositionComesBack()
    {
        var box = new BoxElement { X = 40, Y = 60, WidthDots = 200, HeightDots = 100 };
        string before = ElementSnapshot.Capture([box]);

        box.X = 517;
        box.Y = 903;

        Assert.Equal(1, ElementSnapshot.Restore(before, [box]));
        Assert.Equal(40, box.X);
        Assert.Equal(60, box.Y);
    }

    /// <summary>The reason the restore is a snapshot and not a subtraction. A barcode's
    /// width moves in whole modules, so the size it had before a resize is not recoverable
    /// from the size it has after one.</summary>
    [Fact]
    public void AQuantizedResizeComesBackToTheValueItActuallyHad()
    {
        var barcode = new BarcodeElement
        {
            X = 30, Y = 30, Data = "7891234567895", HeightDots = 80, ModuleWidthDots = 3,
        };
        string before = ElementSnapshot.Capture([barcode]);

        ElementResizer.Resize(barcode, 900, 140);
        Assert.NotEqual(3, barcode.ModuleWidthDots);

        ElementSnapshot.Restore(before, [barcode]);
        Assert.Equal(3, barcode.ModuleWidthDots);
        Assert.Equal(80, barcode.HeightDots);
    }

    /// <summary>The selection, the drag list and the canvas all hold references to the
    /// live elements, so a restore that swapped instances would leave every one of them
    /// pointing at something the document no longer has.</summary>
    [Fact]
    public void TheSameInstanceIsRestored()
    {
        var text = new TextElement { X = 10, Y = 10, Text = "before", FontHeightDots = 40 };
        string snapshot = ElementSnapshot.Capture([text]);
        var stillTheSame = text;

        text.X = 999;
        text.Text = "after";
        ElementSnapshot.Restore(snapshot, [text]);

        Assert.Same(stillTheSame, text);
        Assert.Equal(10, text.X);
        Assert.Equal("before", text.Text);
    }

    [Fact]
    public void SeveralElementsAreMatchedByIdRatherThanByOrder()
    {
        var first = new BoxElement { X = 10, Y = 10, WidthDots = 50, HeightDots = 50 };
        var second = new BoxElement { X = 200, Y = 200, WidthDots = 50, HeightDots = 50 };
        string before = ElementSnapshot.Capture([first, second]);

        first.X = 0;
        second.X = 0;

        Assert.Equal(2, ElementSnapshot.Restore(before, new[] { second, first }));
        Assert.Equal(10, first.X);
        Assert.Equal(200, second.X);
    }

    /// <summary>A gesture that added elements is the caller's to unwind; this speaks only
    /// for the ones present in both, and must leave everything else alone.</summary>
    [Fact]
    public void AnElementTheSnapshotNeverSawIsUntouched()
    {
        var known = new BoxElement { X = 10, Y = 10, WidthDots = 50, HeightDots = 50 };
        string before = ElementSnapshot.Capture([known]);

        var stranger = new BoxElement { X = 700, Y = 700, WidthDots = 50, HeightDots = 50 };
        known.X = 0;

        Assert.Equal(1, ElementSnapshot.Restore(before, [known, stranger]));
        Assert.Equal(10, known.X);
        Assert.Equal(700, stranger.X);
    }

    [Fact]
    public void EveryPropertyOfTheTypeComesBackAndNotJustGeometry()
    {
        var text = new TextElement
        {
            X = 5,
            Y = 6,
            Text = "original",
            FontHeightDots = 40,
            FontWidthDots = 30,
            Font = 'D',
            Orientation = Orientation.Normal,
            IsReversed = false,
            Name = "header",
        };
        string before = ElementSnapshot.Capture([text]);

        text.Text = "changed";
        text.FontHeightDots = 90;
        text.FontWidthDots = 0;
        text.Font = '0';
        text.Orientation = Orientation.Rotated270;
        text.IsReversed = true;
        text.Name = "something else";

        ElementSnapshot.Restore(before, [text]);

        Assert.Equal("original", text.Text);
        Assert.Equal(40, text.FontHeightDots);
        Assert.Equal(30, text.FontWidthDots);
        Assert.Equal('D', text.Font);
        Assert.Equal(Orientation.Normal, text.Orientation);
        Assert.False(text.IsReversed);
        Assert.Equal("header", text.Name);
    }
}
