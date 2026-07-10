using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.App.Controls;

/// <summary>
/// The design surface. Follows the WYSIWYG rule: the rendered label bitmap (from
/// IZplRenderer, debounced by the view model) is drawn as the underlay and is the
/// only visual truth; on top the control draws an interaction overlay (selection
/// outlines, handles, drag ghosts, marquee) computed from model geometry in dot
/// space. Hit-testing uses ElementBoundsCalculator, never Avalonia visuals.
/// Selection lives in a shared SelectionSet: plain click selects, Ctrl/Shift+click
/// toggles, dragging on empty space draws a marquee, and dragging a selected
/// element moves the whole selection.
/// </summary>
public sealed class DesignerCanvas : Control
{
    public static readonly StyledProperty<IImage?> UnderlayProperty =
        AvaloniaProperty.Register<DesignerCanvas, IImage?>(nameof(Underlay));

    public static readonly StyledProperty<LabelDocument?> DocumentProperty =
        AvaloniaProperty.Register<DesignerCanvas, LabelDocument?>(nameof(Document));

    public static readonly StyledProperty<SelectionSet?> SelectionProperty =
        AvaloniaProperty.Register<DesignerCanvas, SelectionSet?>(nameof(Selection));

    static DesignerCanvas()
    {
        AffectsRender<DesignerCanvas>(UnderlayProperty, DocumentProperty, SelectionProperty);
    }

    private const double HandleSize = 9;

    private static readonly SolidColorBrush SurfaceBrush = new(Color.FromRgb(0xD9, 0xD9, 0xD9));
    private static readonly SolidColorBrush DarkSurfaceBrush = new(Color.FromRgb(0x3C, 0x3C, 0x3C));
    private static readonly Pen LabelBorderPen = new(Brushes.Gray, 1);
    private static readonly Pen SelectionPen = new(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1.5);
    private static readonly Pen GhostPen = new(
        new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1.5, new DashStyle([4, 4], 0));

    private readonly ElementBoundsCalculator _bounds = new();

    // Group drag.
    private bool _dragging;
    private Point _dragStartDots;
    private readonly List<(Element Element, int StartX, int StartY)> _dragItems = [];
    private int _dragDx;
    private int _dragDy;

    // Resize (single selection only).
    private bool _resizing;
    private DotRect _resizeStartBounds;
    private int _candidateWidth;
    private int _candidateHeight;

    // Marquee selection.
    private bool _marquee;
    private Point _marqueeStart;
    private Point _marqueeCurrent;

    // Explicit view transform once the user zooms or pans; null means auto-fit.
    private double? _userScale;
    private Point _viewOrigin;
    private bool _panning;
    private Point _panLast;

    public DesignerCanvas()
    {
        Focusable = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The area around the label follows the app theme; the label itself stays
    /// white because it represents physical label stock (WYSIWYG).</summary>
    private IBrush SurfaceThemeBrush() =>
        ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? DarkSurfaceBrush : SurfaceBrush;

    public IImage? Underlay
    {
        get => GetValue(UnderlayProperty);
        set => SetValue(UnderlayProperty, value);
    }

    public LabelDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public SelectionSet? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    /// <summary>Raised after the user commits an edit through the canvas (drag, resize, nudge).</summary>
    public event EventHandler? DocumentEdited;

    /// <summary>Raised when the user presses Delete with a selection.</summary>
    public event EventHandler? DeleteRequested;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DocumentProperty)
        {
            // A different document means a different size; go back to auto-fit.
            _userScale = null;
        }
        else if (change.Property == SelectionProperty)
        {
            if (change.OldValue is SelectionSet oldSelection)
            {
                oldSelection.Changed -= OnSelectionChanged;
            }

            if (change.NewValue is SelectionSet newSelection)
            {
                newSelection.Changed += OnSelectionChanged;
            }
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => InvalidateVisual();

    private (double Scale, Point Origin) GetTransform()
    {
        if (_userScale is { } explicitScale)
        {
            return (explicitScale, _viewOrigin);
        }

        var doc = Document;
        if (doc is null || doc.WidthDots <= 0 || doc.HeightDots <= 0 ||
            Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return (1, default);
        }

        const double margin = 16;
        double scale = Math.Min(
            (Bounds.Width - 2 * margin) / doc.WidthDots,
            (Bounds.Height - 2 * margin) / doc.HeightDots);
        scale = Math.Max(scale, 0.01);

        var origin = new Point(
            (Bounds.Width - doc.WidthDots * scale) / 2,
            (Bounds.Height - doc.HeightDots * scale) / 2);
        return (scale, origin);
    }

    /// <summary>Returns to the auto-fit view (Ctrl+0).</summary>
    public void ResetView()
    {
        _userScale = null;
        InvalidateVisual();
    }

    /// <summary>Captures the current fit transform so zoom/pan can start from it.</summary>
    private void EnsureExplicitTransform()
    {
        if (_userScale is null)
        {
            (double scale, Point origin) = GetTransform();
            _userScale = scale;
            _viewOrigin = origin;
        }
    }

    private void ZoomAt(Point pivot, double factor)
    {
        EnsureExplicitTransform();
        double oldScale = _userScale!.Value;
        double newScale = Math.Clamp(oldScale * factor, 0.05, 40);
        double ratio = newScale / oldScale;

        // Keep the point under the cursor stationary while scaling.
        _viewOrigin = new Point(
            pivot.X - (pivot.X - _viewOrigin.X) * ratio,
            pivot.Y - (pivot.Y - _viewOrigin.Y) * ratio);
        _userScale = newScale;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Point position = e.GetPosition(this);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ZoomAt(position, e.Delta.Y > 0 ? 1.15 : 1 / 1.15);
        }
        else
        {
            EnsureExplicitTransform();
            const double step = 40;
            double dx = (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? e.Delta.Y : e.Delta.X) * step;
            double dy = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0 : e.Delta.Y * step;
            _viewOrigin = new Point(_viewOrigin.X + dx, _viewOrigin.Y + dy);
            InvalidateVisual();
        }

        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(SurfaceThemeBrush(), new Rect(Bounds.Size));

        var doc = Document;
        if (doc is null)
        {
            return;
        }

        var (scale, origin) = GetTransform();
        var labelRect = new Rect(origin.X, origin.Y, doc.WidthDots * scale, doc.HeightDots * scale);

        context.FillRectangle(Brushes.White, labelRect);
        context.DrawRectangle(null, LabelBorderPen, labelRect.Inflate(0.5));

        if (Underlay is { } underlay)
        {
            context.DrawImage(underlay, new Rect(underlay.Size), labelRect);
        }

        if (Selection is { Count: > 0 } selection)
        {
            foreach (Element element in selection.Items)
            {
                if (!doc.Elements.Contains(element))
                {
                    continue;
                }

                DotRect bounds = _bounds.GetBounds(element);
                bool moving = _dragging && _dragItems.Any(d => d.Element == element);
                int x = moving ? bounds.X + _dragDx : bounds.X;
                int y = moving ? bounds.Y + _dragDy : bounds.Y;
                int w = _resizing && selection.Count == 1 ? _candidateWidth : bounds.Width;
                int h = _resizing && selection.Count == 1 ? _candidateHeight : bounds.Height;

                var rect = new Rect(
                    origin.X + x * scale,
                    origin.Y + y * scale,
                    Math.Max(w * scale, 4),
                    Math.Max(h * scale, 4));

                bool ghost = moving || (_resizing && selection.Count == 1);
                context.DrawRectangle(null, ghost ? GhostPen : SelectionPen, rect);

                if (!ghost && selection.Count == 1)
                {
                    foreach (var corner in new[]
                             {
                                 rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight,
                             })
                    {
                        var handleRect = new Rect(
                            corner.X - HandleSize / 2, corner.Y - HandleSize / 2, HandleSize, HandleSize);
                        context.DrawRectangle(Brushes.White, SelectionPen, handleRect);
                    }
                }
            }
        }

        if (_marquee)
        {
            context.DrawRectangle(null, GhostPen, MarqueeRect());
        }
    }

    private Rect MarqueeRect() => new(
        Math.Min(_marqueeStart.X, _marqueeCurrent.X),
        Math.Min(_marqueeStart.Y, _marqueeCurrent.Y),
        Math.Abs(_marqueeCurrent.X - _marqueeStart.X),
        Math.Abs(_marqueeCurrent.Y - _marqueeStart.Y));

    /// <summary>The bottom-right (resize) handle, only for a single unlocked selection.</summary>
    private Rect? ResizeHandleRect()
    {
        if (Selection is not { Count: 1 } selection ||
            selection.Primary is not { IsLocked: false } selected ||
            Document is not { } doc || !doc.Elements.Contains(selected))
        {
            return null;
        }

        var (scale, origin) = GetTransform();
        DotRect bounds = _bounds.GetBounds(selected);
        double cornerX = origin.X + (bounds.X + bounds.Width) * scale;
        double cornerY = origin.Y + (bounds.Y + bounds.Height) * scale;

        // Slightly larger than the drawn handle so it is easy to grab.
        const double grab = HandleSize + 4;
        return new Rect(cornerX - grab / 2, cornerY - grab / 2, grab, grab);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var doc = Document;
        var selection = Selection;
        if (doc is null || selection is null)
        {
            return;
        }

        var (scale, origin) = GetTransform();
        Point p = e.GetPosition(this);

        // Middle button pans the view; it never touches the selection.
        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            EnsureExplicitTransform();
            _panning = true;
            _panLast = p;
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Pointer.Capture(this);
            return;
        }

        double dotX = (p.X - origin.X) / scale;
        double dotY = (p.Y - origin.Y) / scale;

        // The resize handle wins over element hit-testing, otherwise grabbing the
        // corner of a small element would just re-select it.
        if (ResizeHandleRect() is { } handle && handle.Contains(p) &&
            selection.Primary is { } primary)
        {
            _resizing = true;
            _dragStartDots = new Point(dotX, dotY);
            _resizeStartBounds = _bounds.GetBounds(primary);
            _candidateWidth = _resizeStartBounds.Width;
            _candidateHeight = _resizeStartBounds.Height;
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                        e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        Element? hit = doc.Elements
            .Where(el => el.IsVisible)
            .OrderByDescending(el => el.ZOrder)
            .FirstOrDefault(el => _bounds.GetBounds(el).Contains((int)dotX, (int)dotY));

        if (hit is null)
        {
            // Empty space: start a marquee; plain click also clears the selection.
            if (!additive)
            {
                selection.Clear();
            }

            _marquee = true;
            _marqueeStart = p;
            _marqueeCurrent = p;
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        if (additive)
        {
            selection.Toggle(hit);
            return;
        }

        if (!selection.Contains(hit))
        {
            selection.Set(hit);
        }

        // Drag the whole (unlocked part of the) selection together.
        _dragItems.Clear();
        foreach (Element element in selection.Items.Where(el => !el.IsLocked))
        {
            _dragItems.Add((element, element.X, element.Y));
        }

        if (_dragItems.Count > 0)
        {
            _dragging = true;
            _dragStartDots = new Point(dotX, dotY);
            _dragDx = 0;
            _dragDy = 0;
            e.Pointer.Capture(this);
        }

        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point p = e.GetPosition(this);

        if (_panning)
        {
            _viewOrigin = new Point(
                _viewOrigin.X + (p.X - _panLast.X),
                _viewOrigin.Y + (p.Y - _panLast.Y));
            _panLast = p;
            InvalidateVisual();
            return;
        }

        if (_marquee)
        {
            _marqueeCurrent = p;
            InvalidateVisual();
            return;
        }

        if (!_dragging && !_resizing)
        {
            Cursor = ResizeHandleRect() is { } handle && handle.Contains(p)
                ? new Cursor(StandardCursorType.BottomRightCorner)
                : Cursor.Default;
            return;
        }

        if (Document is not { } doc)
        {
            return;
        }

        var (scale, origin) = GetTransform();
        double dotX = (p.X - origin.X) / scale;
        double dotY = (p.Y - origin.Y) / scale;

        if (_resizing)
        {
            _candidateWidth = Math.Max(
                _resizeStartBounds.Width + (int)Math.Round(dotX - _dragStartDots.X), 4);
            _candidateHeight = Math.Max(
                _resizeStartBounds.Height + (int)Math.Round(dotY - _dragStartDots.Y), 4);
        }
        else
        {
            (_dragDx, _dragDy) = ClampGroupDelta(
                doc,
                (int)Math.Round(dotX - _dragStartDots.X),
                (int)Math.Round(dotY - _dragStartDots.Y));
        }

        InvalidateVisual();
    }

    /// <summary>Clamps a group-move delta so every dragged element stays on the label,
    /// preserving the elements' relative offsets.</summary>
    private (int Dx, int Dy) ClampGroupDelta(LabelDocument doc, int dx, int dy)
    {
        int dxLow = int.MinValue, dxHigh = int.MaxValue;
        int dyLow = int.MinValue, dyHigh = int.MaxValue;

        foreach ((Element _, int startX, int startY) in _dragItems)
        {
            dxLow = Math.Max(dxLow, -startX);
            dxHigh = Math.Min(dxHigh, Math.Max(doc.WidthDots - 1, 0) - startX);
            dyLow = Math.Max(dyLow, -startY);
            dyHigh = Math.Min(dyHigh, Math.Max(doc.HeightDots - 1, 0) - startY);
        }

        return (Math.Clamp(dx, dxLow, dxHigh), Math.Clamp(dy, dyLow, dyHigh));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_panning)
        {
            _panning = false;
            Cursor = Cursor.Default;
            e.Pointer.Capture(null);
            return;
        }

        if (_marquee)
        {
            _marquee = false;
            e.Pointer.Capture(null);
            CommitMarquee();
            InvalidateVisual();
            return;
        }

        if (!_dragging && !_resizing)
        {
            return;
        }

        bool wasResizing = _resizing;
        _dragging = false;
        _resizing = false;
        e.Pointer.Capture(null);

        if (wasResizing && Selection?.Primary is { } primary)
        {
            // The gesture targets the on-screen (rotated) footprint; the resizer
            // reasons in the element's intrinsic axes, so swap for 90/270.
            (int w, int h) = primary.Orientation is Orientation.Rotated90 or Orientation.Rotated270
                ? (_candidateHeight, _candidateWidth)
                : (_candidateWidth, _candidateHeight);
            ElementResizer.Resize(primary, w, h);
            DocumentEdited?.Invoke(this, EventArgs.Empty);
        }
        else if (_dragDx != 0 || _dragDy != 0)
        {
            foreach ((Element element, int startX, int startY) in _dragItems)
            {
                element.X = startX + _dragDx;
                element.Y = startY + _dragDy;
            }

            DocumentEdited?.Invoke(this, EventArgs.Empty);
        }

        _dragItems.Clear();
        _dragDx = 0;
        _dragDy = 0;
        InvalidateVisual();
    }

    private void CommitMarquee()
    {
        if (Document is not { } doc || Selection is not { } selection)
        {
            return;
        }

        var (scale, origin) = GetTransform();
        Rect band = MarqueeRect();

        int x = (int)Math.Floor((band.X - origin.X) / scale);
        int y = (int)Math.Floor((band.Y - origin.Y) / scale);
        int w = Math.Max((int)Math.Ceiling(band.Width / scale), 1);
        int h = Math.Max((int)Math.Ceiling(band.Height / scale), 1);
        var dotBand = new DotRect(x, y, w, h);

        var hits = doc.Elements
            .Where(el => el.IsVisible && _bounds.GetBounds(el).Intersects(dotBand))
            .ToList();

        if (hits.Count > 0)
        {
            selection.SetMany(hits);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Selection is not { Count: > 0 } selection || Document is not { } doc)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        (int dx, int dy) = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => (0, 0),
        };

        if (dx == 0 && dy == 0)
        {
            return;
        }

        // Nudge the whole selection with the same clamped delta.
        _dragItems.Clear();
        foreach (Element element in selection.Items.Where(el => !el.IsLocked))
        {
            _dragItems.Add((element, element.X, element.Y));
        }

        if (_dragItems.Count == 0)
        {
            return;
        }

        (dx, dy) = ClampGroupDelta(doc, dx, dy);
        foreach ((Element element, int startX, int startY) in _dragItems)
        {
            element.X = startX + dx;
            element.Y = startY + dy;
        }

        _dragItems.Clear();
        DocumentEdited?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }
}
