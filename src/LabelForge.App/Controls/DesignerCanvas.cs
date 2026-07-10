using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using LabelForge.Core.Model;

namespace LabelForge.App.Controls;

/// <summary>
/// The design surface. Follows the WYSIWYG rule: the rendered label bitmap (from
/// IZplRenderer, debounced by the view model) is drawn as the underlay and is the
/// only visual truth; on top the control draws an interaction overlay (selection
/// outline, handles, drag ghost) computed from model geometry in dot space.
/// Hit-testing uses ElementBoundsCalculator, never Avalonia visuals.
/// </summary>
public sealed class DesignerCanvas : Control
{
    public static readonly StyledProperty<IImage?> UnderlayProperty =
        AvaloniaProperty.Register<DesignerCanvas, IImage?>(nameof(Underlay));

    public static readonly StyledProperty<LabelDocument?> DocumentProperty =
        AvaloniaProperty.Register<DesignerCanvas, LabelDocument?>(nameof(Document));

    public static readonly StyledProperty<Element?> SelectedElementProperty =
        AvaloniaProperty.Register<DesignerCanvas, Element?>(
            nameof(SelectedElement), defaultBindingMode: BindingMode.TwoWay);

    static DesignerCanvas()
    {
        AffectsRender<DesignerCanvas>(UnderlayProperty, DocumentProperty, SelectedElementProperty);
    }

    private static readonly SolidColorBrush SurfaceBrush = new(Color.FromRgb(0xD9, 0xD9, 0xD9));
    private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0x25, 0x63, 0xEB));
    private static readonly Pen LabelBorderPen = new(Brushes.Gray, 1);
    private static readonly Pen SelectionPen = new(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1.5);
    private static readonly Pen GhostPen = new(
        new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1.5, new DashStyle([4, 4], 0));

    private const double HandleSize = 9;

    private readonly ElementBoundsCalculator _bounds = new();
    private bool _dragging;
    private bool _resizing;
    private Point _dragStartDots;
    private int _elementStartX;
    private int _elementStartY;
    private int _candidateX;
    private int _candidateY;
    private DotRect _resizeStartBounds;
    private int _candidateWidth;
    private int _candidateHeight;

    // Explicit view transform once the user zooms or pans; null means auto-fit.
    private double? _userScale;
    private Point _viewOrigin;
    private bool _panning;
    private Point _panLast;

    public DesignerCanvas()
    {
        Focusable = true;
    }

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

    public Element? SelectedElement
    {
        get => GetValue(SelectedElementProperty);
        set => SetValue(SelectedElementProperty, value);
    }

    /// <summary>Raised after the user commits an edit through the canvas (drag, nudge).</summary>
    public event EventHandler? DocumentEdited;

    /// <summary>Raised when the user presses Delete with a selection.</summary>
    public event EventHandler? DeleteRequested;

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // A different document means a different size; go back to auto-fit.
        if (change.Property == DocumentProperty)
        {
            _userScale = null;
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(SurfaceBrush, new Rect(Bounds.Size));

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

        if (SelectedElement is { } selected && doc.Elements.Contains(selected))
        {
            DotRect bounds = _bounds.GetBounds(selected);
            int x = _dragging ? _candidateX : bounds.X;
            int y = _dragging ? _candidateY : bounds.Y;
            int w = _resizing ? _candidateWidth : bounds.Width;
            int h = _resizing ? _candidateHeight : bounds.Height;

            var rect = new Rect(
                origin.X + x * scale,
                origin.Y + y * scale,
                Math.Max(w * scale, 4),
                Math.Max(h * scale, 4));

            bool ghost = _dragging || _resizing;
            context.DrawRectangle(null, ghost ? GhostPen : SelectionPen, rect);

            if (!ghost)
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

    /// <summary>The bottom-right (resize) handle of the current selection, in screen space.</summary>
    private Rect? ResizeHandleRect()
    {
        if (SelectedElement is not { IsLocked: false } selected ||
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
        if (doc is null)
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
            SelectedElement is { } sel)
        {
            _resizing = true;
            _dragStartDots = new Point(dotX, dotY);
            _resizeStartBounds = _bounds.GetBounds(sel);
            _candidateWidth = _resizeStartBounds.Width;
            _candidateHeight = _resizeStartBounds.Height;
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        Element? hit = doc.Elements
            .Where(el => el.IsVisible)
            .OrderByDescending(el => el.ZOrder)
            .FirstOrDefault(el => _bounds.GetBounds(el).Contains((int)dotX, (int)dotY));

        SelectedElement = hit;

        if (hit is { IsLocked: false })
        {
            _dragging = true;
            _dragStartDots = new Point(dotX, dotY);
            _elementStartX = hit.X;
            _elementStartY = hit.Y;
            _candidateX = hit.X;
            _candidateY = hit.Y;
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
            _candidateX = Math.Clamp(
                _elementStartX + (int)Math.Round(dotX - _dragStartDots.X), 0, Math.Max(doc.WidthDots - 1, 0));
            _candidateY = Math.Clamp(
                _elementStartY + (int)Math.Round(dotY - _dragStartDots.Y), 0, Math.Max(doc.HeightDots - 1, 0));
        }

        InvalidateVisual();
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

        if (!_dragging && !_resizing)
        {
            return;
        }

        bool wasResizing = _resizing;
        _dragging = false;
        _resizing = false;
        e.Pointer.Capture(null);

        if (SelectedElement is { } selected)
        {
            if (wasResizing)
            {
                // The gesture targets the on-screen (rotated) footprint; the resizer
                // reasons in the element's intrinsic axes, so swap for 90/270.
                (int w, int h) = selected.Orientation is Orientation.Rotated90 or Orientation.Rotated270
                    ? (_candidateHeight, _candidateWidth)
                    : (_candidateWidth, _candidateHeight);
                ElementResizer.Resize(selected, w, h);
                DocumentEdited?.Invoke(this, EventArgs.Empty);
            }
            else if (selected.X != _candidateX || selected.Y != _candidateY)
            {
                selected.X = _candidateX;
                selected.Y = _candidateY;
                DocumentEdited?.Invoke(this, EventArgs.Empty);
            }
        }

        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (SelectedElement is not { IsLocked: false } selected || Document is not { } doc)
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

        selected.X = Math.Clamp(selected.X + dx, 0, Math.Max(doc.WidthDots - 1, 0));
        selected.Y = Math.Clamp(selected.Y + dy, 0, Math.Max(doc.HeightDots - 1, 0));
        DocumentEdited?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }
}
