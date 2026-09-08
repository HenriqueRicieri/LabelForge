using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

public sealed class DrawGestureTests
{
    [Theory]
    [InlineData(400, 300, 100, 100, false, false)]
    [InlineData(-200, 300, -200, 100, true, false)]
    [InlineData(400, -100, 100, -100, false, true)]
    [InlineData(-200, -100, -200, -100, true, true)]
    public void Box_NormalizesEveryQuadrant(int x, int y, int expectedX, int expectedY, bool left, bool top)
    {
        Assert.Equal(new DrawTarget(expectedX, expectedY, 300, 200, left, top, null),
            DrawGesture.Calculate(new BoxElement(), 100, 100, x, y, false));
    }

    [Theory]
    [InlineData(300, 20, 100, 100, 300, Orientation.Normal)]
    [InlineData(20, 300, 100, 100, 300, Orientation.Rotated90)]
    [InlineData(-300, 20, -200, 100, 300, Orientation.Normal)]
    [InlineData(20, -300, 100, -200, 300, Orientation.Rotated90)]
    [InlineData(30, 30, 100, 100, 30, Orientation.Normal)]
    public void Line_UsesDominantAxisAndKeepsThePressEnd(int dx, int dy, int x, int y, int length, Orientation rotation)
    {
        DrawTarget target = DrawGesture.Calculate(new LineElement(), 100, 100, 100 + dx, 100 + dy, false);
        Assert.Equal((x, y, rotation), (target.X, target.Y, target.Rotation));
        Assert.Equal(length, rotation == Orientation.Normal ? target.Width : target.Height);
    }

    [Theory]
    [InlineData(100, 60, Orientation.Rotated90)]
    [InlineData(-100, -60, Orientation.Rotated90)]
    [InlineData(100, -60, Orientation.Normal)]
    [InlineData(-100, 60, Orientation.Normal)]
    public void Diagonal_UsesQuadrant(int dx, int dy, Orientation rotation) =>
        Assert.Equal(rotation, DrawGesture.Calculate(new DiagonalLineElement(), 0, 0, dx, dy, false).Rotation);

    [Fact]
    public void Snapping_PreservesAnchorAndOptionalAspect()
    {
        DrawTarget target = DrawGesture.Calculate(new BoxElement(), 100, 100, 297, 248, false);
        DrawTarget snapped = DrawGesture.Snap(target, 100, 100, [300], [250], 6, false, out _, out _);
        Assert.Equal((100, 100, 200, 150), (snapped.X, snapped.Y, snapped.Width, snapped.Height));
        target = DrawGesture.Calculate(new BoxElement(), 400, 400, 103, 252, true);
        snapped = DrawGesture.Snap(target, 400, 400, [100], [], 6, true, out _, out _);
        Assert.Equal((100, 100, 300, 300), (snapped.X, snapped.Y, snapped.Width, snapped.Height));
    }

    [Fact]
    public void Shift_MakesShapesSquareAndKeepsImageSourceAspect()
    {
        foreach (Element element in new Element[] { new BoxElement(), new EllipseElement(), new DiagonalLineElement() })
        {
            DrawTarget target = DrawGesture.Calculate(element, 100, 100, -200, -100, true);
            Assert.Equal((-200, -200, 300, 300), (target.X, target.Y, target.Width, target.Height));
        }

        var image = new ImageElement { SourcePixelWidth = 400, SourcePixelHeight = 200 };
        DrawTarget picture = DrawGesture.Calculate(image, 100, 100, 200, 200, true);
        Assert.Equal((200, 100), (picture.Width, picture.Height));
    }
}
