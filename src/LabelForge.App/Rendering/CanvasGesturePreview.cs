using System;
using Avalonia.Media.Imaging;
using LabelForge.Core.Model;

namespace LabelForge.App.Rendering;

public sealed class CanvasGesturePreview(
    Bitmap? below, Bitmap? moving, Bitmap? above, DotRect viewport, DotRect movingBounds) : IDisposable
{
    public Bitmap? Below { get; } = below;
    public Bitmap? Moving { get; private set; } = moving;
    public Bitmap? Above { get; } = above;
    public DotRect Viewport { get; } = viewport;
    public DotRect MovingBounds { get; private set; } = movingBounds;

    public void ReplaceMoving(Bitmap? moving, DotRect bounds)
    {
        Bitmap? previous = Moving;
        Moving = moving;
        MovingBounds = bounds;
        previous?.Dispose();
    }

    public void Dispose()
    {
        Below?.Dispose();
        Moving?.Dispose();
        Above?.Dispose();
    }
}
