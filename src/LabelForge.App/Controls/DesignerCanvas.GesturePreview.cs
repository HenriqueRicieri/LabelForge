using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using LabelForge.App.Rendering;
using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.App.Controls;

public sealed partial class DesignerCanvas
{
    public static readonly StyledProperty<CanvasGesturePreview?> GesturePreviewProperty =
        AvaloniaProperty.Register<DesignerCanvas, CanvasGesturePreview?>(nameof(GesturePreview));

    public static readonly StyledProperty<Vector> GestureOffsetProperty =
        AvaloniaProperty.Register<DesignerCanvas, Vector>(nameof(GestureOffset));

    public CanvasGesturePreview? GesturePreview
    {
        get => GetValue(GesturePreviewProperty);
        set => SetValue(GesturePreviewProperty, value);
    }

    public Vector GestureOffset
    {
        get => GetValue(GestureOffsetProperty);
        set => SetValue(GestureOffsetProperty, value);
    }

    public event Action<GestureKind, IReadOnlyList<Element>, string>? GestureStarted;
    public event Action<bool>? GestureEnded;
    private bool _gesturePreviewActive;

    private void StartGesturePreview(GestureKind kind, IReadOnlyList<Element> elements, string snapshot)
    {
        _gesturePreviewActive = true;
        SetCurrentValue(GestureOffsetProperty, default(Vector));
        GestureStarted?.Invoke(kind, elements, snapshot);
    }

    private void EndGesturePreview(bool committed)
    {
        if (!_gesturePreviewActive) return;
        _gesturePreviewActive = false;
        if (!committed) SetCurrentValue(GestureOffsetProperty, default(Vector));
        GestureEnded?.Invoke(committed);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_gesturePreviewActive) CancelGesture();
        base.OnPointerCaptureLost(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_gesturePreviewActive) CancelGesture();
        base.OnDetachedFromVisualTree(e);
    }

    private bool DrawGesturePreview(DrawingContext context, double scale, Point origin, Rect labelRect)
    {
        if (GesturePreview is not { } preview) return false;

        Rect viewport = ToView(preview.Viewport, default);
        using (context.PushClip(viewport))
        {
            context.FillRectangle(Brushes.White, viewport);
            if (preview.Below is { } below)
                context.DrawImage(below, new Rect(below.Size), viewport);
            if (preview.Moving is { } moving)
                context.DrawImage(moving, new Rect(moving.Size), ToView(preview.MovingBounds, GestureOffset));
            if (preview.Above is { } above)
                context.DrawImage(above, new Rect(above.Size), viewport);

            Rect label = viewport.Intersect(labelRect);
            IBrush dim = IsDark ? DarkDimBrush : DimBrush;
            if (label.Width <= 0 || label.Height <= 0)
            {
                context.FillRectangle(dim, viewport);
            }
            else
            {
                context.FillRectangle(dim, new Rect(viewport.X, viewport.Y, viewport.Width, label.Y - viewport.Y));
                context.FillRectangle(dim, new Rect(viewport.X, label.Bottom, viewport.Width, viewport.Bottom - label.Bottom));
                context.FillRectangle(dim, new Rect(viewport.X, label.Y, label.X - viewport.X, label.Height));
                context.FillRectangle(dim, new Rect(label.Right, label.Y, viewport.Right - label.Right, label.Height));
            }
        }
        return true;

        Rect ToView(DotRect bounds, Vector offset) => new(
            origin.X + (bounds.X + offset.X) * scale,
            origin.Y + (bounds.Y + offset.Y) * scale,
            bounds.Width * scale,
            bounds.Height * scale);
    }
}
