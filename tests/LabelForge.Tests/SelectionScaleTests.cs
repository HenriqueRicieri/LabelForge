using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class SelectionScaleTests
{
    [Fact]
    public void ScalingMovesAndSizesEveryFieldFromTheSameAnchor()
    {
        var first = new BoxElement { X = 100, Y = 100, WidthDots = 80, HeightDots = 60 };
        var second = new BoxElement { X = 260, Y = 200, WidthDots = 40, HeightDots = 60 };
        var scaling = Start(first, second);

        scaling.Apply(new ScaleFrame(100, 100, 400, 320), 1, 1, false);

        Assert.Equal((100, 100, 160, 120), (first.X, first.Y, first.WidthDots, first.HeightDots));
        Assert.Equal((420, 300, 80, 120), (second.X, second.Y, second.WidthDots, second.HeightDots));
        Assert.True(scaling.HasChanged);
        Assert.Equal(0, scaling.Constrained);
    }

    [Theory]
    [InlineData(-1, -1, false, 0, 20, 300, 240)]
    [InlineData(1, 1, true, 0, 20, 400, 320)]
    [InlineData(1, 0, false, 100, 100, 300, 160)]
    public void DrawnBoundsFollowTheRequestedAnchor(int horizontal, int vertical, bool centered,
        int x, int y, int width, int height)
    {
        var scaling = Start(new BoxElement { X = 100, Y = 100, WidthDots = 80, HeightDots = 60 },
            new BoxElement { X = 260, Y = 200, WidthDots = 40, HeightDots = 60 });
        var frame = new ScaleFrame(x, y, width, height);

        scaling.Apply(frame, horizontal, vertical, centered);

        Assert.Equal(new DotRect(x, y, width, height), SelectionScale.GetBounds(scaling.Elements));
    }

    [Fact]
    public void RepeatedFramesDoNotAccumulateTextOrBarcodeRounding()
    {
        var scaling = Start(new TextElement { X = 100, Y = 100, Text = "width", FontHeightDots = 31, FontWidthDots = 17 },
            new BarcodeElement { X = 200, Y = 180, Data = "123456", ModuleWidthDots = 3, HeightDots = 83 });
        DotRect b = scaling.StartBounds;
        var frame = new ScaleFrame(b.X, b.Y, b.Width * 1.37, b.Height * 1.37);
        scaling.Apply(frame, 1, 1, false);
        string once = ElementSnapshot.Capture(scaling.Elements);

        for (int i = 0; i < 20; i++) scaling.Apply(frame, 1, 1, false);

        Assert.Equal(once, ElementSnapshot.Capture(scaling.Elements));
    }

    public static IEnumerable<object[]> Fields()
    {
        yield return [new TextElement { Text = "A", FontHeightDots = 31, FontWidthDots = 17 }];
        yield return [new TextElement { Text = "bitmap", Font = 'A', FontHeightDots = 18 }];
        yield return [new TextElement { Text = "block text", FontHeightDots = 40,
            BlockWidthDots = 150, BlockMaxLines = 3 }];
        yield return [new BarcodeElement { Data = "123456", ModuleWidthDots = 3, HeightDots = 83 }];
        yield return [new QrCodeElement { Data = "Scale", Magnification = 3 }];
        yield return [new DataMatrixElement { Data = "Scale", ModuleSizeDots = 3 }];
        yield return [new Pdf417Element { Data = "Scale", ModuleWidthDots = 3, RowHeightDots = 5 }];
        yield return [new ImageElement { ImageData = [1, 2, 3], WidthDots = 43, HeightDots = 29 }];
        yield return [new BoxElement { WidthDots = 43, HeightDots = 29 }];
        yield return [new EllipseElement { WidthDots = 43, HeightDots = 29 }];
        yield return [new LineElement { LengthDots = 43, ThicknessDots = 3 }];
        yield return [new DiagonalLineElement { WidthDots = 43, HeightDots = 29 }];
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void ReturningToTheStartRestoresEveryElementProperty(Element field)
    {
        field.X = 120;
        field.Y = 110;
        field.GroupId = Guid.NewGuid();
        field.Name = "Original";
        var scaling = Start(field, new BoxElement { X = 420, Y = 300, WidthDots = 30, HeightDots = 40 });
        DotRect b = scaling.StartBounds;
        scaling.Apply(new ScaleFrame(30, 50, b.Width * 0.7, b.Height * 1.3), -1, -1, false);

        scaling.Apply(new ScaleFrame(b.X, b.Y, b.Width, b.Height), -1, -1, false);

        Assert.Equal(scaling.Snapshot, ElementSnapshot.Capture(scaling.Elements));
        Assert.False(scaling.HasChanged);
    }

    [Fact]
    public void TextInMultipleSelectionUsesTheDraggedAxisAndReturnsToAutomaticWidth()
    {
        var text = new TextElement { X = 100, Y = 100, Text = "Group", FontHeightDots = 40 };
        var scaling = Start(text, new BoxElement { X = 300, Y = 200,
            WidthDots = 40, HeightDots = 40 });
        DotRect b = scaling.StartBounds;

        scaling.Apply(new ScaleFrame(b.X, b.Y, b.Width * 1.5, b.Height), 1, 0, false);
        Assert.Equal(40, text.FontHeightDots);
        Assert.True(text.FontWidthDots > 40);

        scaling.Apply(new ScaleFrame(b.X, b.Y, b.Width, b.Height * 1.5), 0, 1, false);
        Assert.True(text.FontHeightDots > 40);
        Assert.Equal(40, text.FontWidthDots);

        scaling.Apply(new ScaleFrame(b.X, b.Y, b.Width, b.Height), 0, 1, false);
        Assert.Equal((40, 0), (text.FontHeightDots, text.FontWidthDots));
        Assert.False(scaling.HasChanged);
    }

    [Fact]
    public void TextBlockInSelectionScalesItsWrappingWidthWithCharacterWidth()
    {
        var text = new TextElement { X = 100, Y = 100, Text = "Group block",
            FontHeightDots = 40, BlockWidthDots = 200, BlockMaxLines = 3 };
        var scaling = Start(text, new BoxElement { X = 400, Y = 200,
            WidthDots = 40, HeightDots = 40 });
        DotRect b = scaling.StartBounds;

        scaling.Apply(new ScaleFrame(b.X, b.Y, b.Width * 1.5, b.Height), 1, 0, false);
        Assert.Equal((40, 60, 300),
            (text.FontHeightDots, text.FontWidthDots, text.BlockWidthDots));

        scaling.Apply(new ScaleFrame(b.X, b.Y, b.Width, b.Height), 1, 0, false);
        Assert.Equal((40, 0, 200),
            (text.FontHeightDots, text.FontWidthDots, text.BlockWidthDots));
        Assert.False(scaling.HasChanged);
    }

    [Theory]
    [InlineData(Orientation.Normal)]
    [InlineData(Orientation.Rotated90)]
    [InlineData(Orientation.Rotated180)]
    [InlineData(Orientation.Rotated270)]
    public void BaselineFieldsScaleInDrawnCoordinates(Orientation orientation)
    {
        var text = new TextElement { X = 400, Y = 350, Anchor = FieldAnchor.Baseline,
            Orientation = orientation, Text = "Hi", FontHeightDots = 20, FontWidthDots = 10 };
        var scaling = Start(text, new BoxElement { X = 100, Y = 100, WidthDots = 40, HeightDots = 20 });
        var calculator = new ElementBoundsCalculator();
        DotRect before = calculator.GetBounds(text), bounds = scaling.StartBounds;

        scaling.Apply(new ScaleFrame(bounds.X, bounds.Y, bounds.Width * 2, bounds.Height * 2), 1, 1, false);

        DotRect after = calculator.GetBounds(text);
        Assert.Equal(40, text.FontHeightDots);
        Assert.Equal(20, text.FontWidthDots);
        Assert.Equal(bounds.X + 2 * (before.X - bounds.X), after.X);
        Assert.Equal(bounds.Y + 2 * (before.Y - bounds.Y), after.Y);
        Assert.Equal(FieldAnchor.Baseline, text.Anchor);
        Assert.Equal(orientation, text.Orientation);
    }

    [Fact]
    public void VerticalLinesUseTheirRealAxisAndRetainStrokeThickness()
    {
        var line = new LineElement { X = 100, Y = 100, IsVertical = true, LengthDots = 90,
            ThicknessDots = 3, Orientation = Orientation.Rotated90 };
        var scaling = Start(line, new BoxElement { X = 300, Y = 100, WidthDots = 40, HeightDots = 40 });
        DotRect b = scaling.StartBounds;

        scaling.Apply(new ScaleFrame(b.X, b.Y, b.Width * 2, b.Height * 2), 1, 1, false);

        Assert.Equal(180, line.LengthDots);
        Assert.Equal(3, line.ThicknessDots);
        Assert.True(scaling.Constrained > 0);
    }

    [Fact]
    public void QuantizedSymbolsKeepTheirOppositeEdgeWhenScalingFromTheLeft()
    {
        var qr = new QrCodeElement { X = 300, Y = 100, Data = "Scale", Magnification = 3 };
        var scaling = Start(new BoxElement { X = 100, Y = 100, WidthDots = 20, HeightDots = 20 }, qr);
        DotRect before = new ElementBoundsCalculator().GetBounds(qr), b = scaling.StartBounds;
        double width = b.Width * 1.1;

        scaling.Apply(new ScaleFrame(b.X + b.Width - width, b.Y, width, b.Height), -1, 0, false);

        DotRect after = new ElementBoundsCalculator().GetBounds(qr);
        Assert.Equal(before.X + before.Width, after.X + after.Width);
        Assert.Equal(3, qr.Magnification);
        Assert.True(scaling.Constrained > 0);
    }

    [Fact]
    public void LockedOrDetachedSelectionsCannotStart()
    {
        var doc = new LabelDocument();
        Guid group = Guid.NewGuid();
        var first = new BoxElement { GroupId = group };
        var second = new BoxElement { X = 200 };
        var hiddenLock = new BoxElement { GroupId = group, IsLocked = true, IsVisible = false };
        doc.Elements.Add(first);
        doc.Elements.Add(second);
        doc.Elements.Add(hiddenLock);

        Assert.Null(SelectionScale.Start(doc, [first, second]));
        hiddenLock.IsLocked = false;
        Assert.NotNull(SelectionScale.Start(doc, [first, second]));
        Assert.Null(SelectionScale.Start(doc, [first, new BoxElement()]));
        Assert.Null(SelectionScale.Start(doc, [first]));
        Assert.Null(SelectionScale.Start(doc, []));
    }

    [Fact]
    public void SnappingHonoursCentreAndFreeCornerModifiers()
    {
        var scaling = Start(new BoxElement { X = 200, Y = 160, WidthDots = 80, HeightDots = 60 },
            new BoxElement { X = 360, Y = 260, WidthDots = 40, HeightDots = 60 });

        ScaleFrame centre = scaling.Snap(new ScaleFrame(102, 160, 396, 160), 1, 0, true, true, [500], [], 4);
        Assert.Equal(new ScaleFrame(100, 160, 400, 160, 500), centre);
        ScaleFrame free = scaling.Snap(new ScaleFrame(200, 160, 298, 198), 1, 1, false, false, [500], [360], 4);
        Assert.Equal(new ScaleFrame(200, 160, 300, 200, 500, 360), free);
        ScaleFrame proportional = scaling.Snap(new ScaleFrame(200, 160, 298, 238.4), 1, 1, false, true, [500], [], 4);
        Assert.Equal(new ScaleFrame(200, 160, 300, 240, 500), proportional);
    }

    [Theory]
    [InlineData(8, 2.5, true, false, false, 20)]
    [InlineData(12, 2.5, true, false, false, 30)]
    [InlineData(24, 2.5, true, false, true, 600)]
    [InlineData(8, 2.5, true, true, false, 1)]
    [InlineData(8, 2.5, true, true, true, 10)]
    [InlineData(8, 2.5, false, false, false, 1)]
    [InlineData(8, 0, true, false, true, 10)]
    [InlineData(8, 0.1, true, false, false, 1)]
    public void GridNudgesUseTheActivePitchWithFineAndLargeOverrides(int dpmm, double pitch,
        bool snap, bool fine, bool large, int expected) =>
        Assert.Equal(expected, DesignGrid.NudgeStep(new LabelDocument { Dpmm = dpmm, GridPitchMm = pitch }, snap, fine, large));

    private static SelectionScale Start(params Element[] elements)
    {
        var document = new LabelDocument();
        foreach (Element element in elements) document.Elements.Add(element);
        return SelectionScale.Start(document, elements)!;
    }
}
