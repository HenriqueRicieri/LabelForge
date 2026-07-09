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

    private readonly ElementBoundsCalculator _bounds = new();
    private bool _dragging;
    private Point _dragStartDots;
    private int _elementStartX;
    private int _elementStartY;
    private int _candidateX;
    private int _candidateY;

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

            var rect = new Rect(
                origin.X + x * scale,
                origin.Y + y * scale,
                Math.Max(bounds.Width * scale, 4),
                Math.Max(bounds.Height * scale, 4));

            context.DrawRectangle(null, _dragging ? GhostPen : SelectionPen, rect);

            if (!_dragging)
            {
                const double handle = 7;
                foreach (var corner in new[]
                         {
                             rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight,
                         })
                {
                    var handleRect = new Rect(
                        corner.X - handle / 2, corner.Y - handle / 2, handle, handle);
                    context.DrawRectangle(Brushes.White, SelectionPen, handleRect);
                }
            }
        }
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
        double dotX = (p.X - origin.X) / scale;
        double dotY = (p.Y - origin.Y) / scale;

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
        if (!_dragging || Document is not { } doc)
        {
            return;
        }

        var (scale, origin) = GetTransform();
        Point p = e.GetPosition(this);
        double dotX = (p.X - origin.X) / scale;
        double dotY = (p.Y - origin.Y) / scale;

        _candidateX = Math.Clamp(
            _elementStartX + (int)Math.Round(dotX - _dragStartDots.X), 0, Math.Max(doc.WidthDots - 1, 0));
        _candidateY = Math.Clamp(
            _elementStartY + (int)Math.Round(dotY - _dragStartDots.Y), 0, Math.Max(doc.HeightDots - 1, 0));

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);

        if (SelectedElement is { } selected &&
            (selected.X != _candidateX || selected.Y != _candidateY))
        {
            selected.X = _candidateX;
            selected.Y = _candidateY;
            DocumentEdited?.Invoke(this, EventArgs.Empty);
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
