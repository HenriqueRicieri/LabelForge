using LabelForge.Core.Editing;
using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

public sealed class SelectionMarqueeTests
{
    [Theory]
    [InlineData(100, 100, 250, 250, false)]
    [InlineData(100, 250, 250, 100, false)]
    [InlineData(250, 100, 100, 250, true)]
    [InlineData(250, 250, 100, 100, true)]
    public void HorizontalDirectionDecidesWhetherCrossingsCount(int x1, int y1, int x2, int y2, bool crossing)
    {
        var inside = Box(120, 120, 80, 40);
        var partial = Box(220, 170, 80, 40);
        var document = Label(inside, partial, Box(420, 120, 60, 40));
        var marquee = new SelectionMarquee(x1, y1, x2, y2);

        Assert.Equal(crossing, marquee.Crossing);
        Assert.Equal(crossing ? [inside, partial] : new[] { inside }, marquee.Select(document));
    }

    [Fact]
    public void EnclosureIncludesExactEdgesWithoutRoundingFractionalDotsOutward()
    {
        var field = Box(-100, -40, 80, 30);
        var document = Label(field);
        Assert.Equal([field], new SelectionMarquee(-100, -40, -20, -10).Select(document));
        Assert.Empty(new SelectionMarquee(-99.9, -40, -20, -10).Select(document));
        Assert.Empty(new SelectionMarquee(-100, -40, -20.1, -10).Select(document));
    }

    [Fact]
    public void CrossingRequiresOverlapRatherThanOnlyEdgeContact()
    {
        var field = Box(100, 100, 80, 40);
        var document = Label(field);
        Assert.Empty(new SelectionMarquee(100, 90, 20, 160).Select(document));
        Assert.Equal([field], new SelectionMarquee(100.1, 90, 20, 160).Select(document));
    }

    [Theory]
    [InlineData(100, 100, 100, 200)]
    [InlineData(100, 100, 200, 100)]
    [InlineData(100, 100, 100, 100)]
    public void AnEmptyBandSelectsNothing(int x1, int y1, int x2, int y2)
    {
        Assert.Empty(new SelectionMarquee(x1, y1, x2, y2).Select(Label(Box(90, 90, 100, 100))));
    }

    [Fact]
    public void EnclosureRequiresEveryVisibleMemberOfAGroup()
    {
        Guid id = Guid.NewGuid();
        var inside = Box(120, 120, 80, 40);
        var outside = Box(420, 120, 60, 40);
        inside.GroupId = outside.GroupId = id;
        var document = Label(inside, outside);

        Assert.Empty(new SelectionMarquee(100, 100, 250, 250).Select(document));
        Assert.Equal([inside, outside], new SelectionMarquee(250, 100, 100, 250).Select(document));
        Assert.Equal([inside, outside], new SelectionMarquee(100, 100, 500, 250).Select(document));
        outside.IsVisible = false;
        Assert.Equal([inside, outside], new SelectionMarquee(100, 100, 250, 250).Select(document));
    }

    [Fact]
    public void HiddenFieldsDoNotHitAndLocksDoNotPreventSelection()
    {
        var hidden = Box(120, 120, 80, 40);
        hidden.IsVisible = false;
        var locked = Box(120, 180, 80, 40);
        locked.IsLocked = true;
        Assert.Equal([locked], new SelectionMarquee(100, 100, 250, 250).Select(Label(hidden, locked)));
    }

    [Fact]
    public void RotatedBaselineFieldsUseTheirDrawnBoundsAndLeaveTheDocumentUntouched()
    {
        var text = new TextElement { X = 200, Y = 200, Text = "Text", FontHeightDots = 30,
            Anchor = FieldAnchor.Baseline, Orientation = Orientation.Rotated270 };
        var document = Label(text);
        DotRect bounds = new ElementBoundsCalculator().GetBounds(text);
        string snapshot = LabelDocumentJson.Serialize(document);
        string zpl = new ZplGenerator().Generate(document);
        var marquee = new SelectionMarquee(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height);

        Assert.Equal([text], marquee.Select(document));
        Assert.Empty((marquee with { EndX = marquee.EndX - 1 }).Select(document));
        Assert.Equal(snapshot, LabelDocumentJson.Serialize(document));
        Assert.Equal(zpl, new ZplGenerator().Generate(document));
    }

    private static BoxElement Box(int x, int y, int width, int height) =>
        new() { X = x, Y = y, WidthDots = width, HeightDots = height };

    private static LabelDocument Label(params Element[] elements) => new() { Elements = elements.ToList() };
}
