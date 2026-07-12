using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

    public static readonly StyledProperty<bool> IsPlacingProperty =
        AvaloniaProperty.Register<DesignerCanvas, bool>(nameof(IsPlacing));

    public static readonly StyledProperty<int> UnderlayMarginDotsProperty =
        AvaloniaProperty.Register<DesignerCanvas, int>(nameof(UnderlayMarginDots));

    static DesignerCanvas()
    {
        AffectsRender<DesignerCanvas>(
            UnderlayProperty, DocumentProperty, SelectionProperty, UnderlayMarginDotsProperty);
    }

    private enum ResizeHandle
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left,
    }

    private const double HandleSize = 9;

    /// <summary>Width of the mm ruler bands pinned along the top and left edges.</summary>
    private const double RulerSize = 26;

    /// <summary>Gap between the rulers and the label at the auto-fit view.</summary>
    private const double FitGap = 12;

    private static readonly SolidColorBrush SurfaceBrush = new(Color.FromRgb(0xD9, 0xD9, 0xD9));
    private static readonly SolidColorBrush DarkSurfaceBrush = new(Color.FromRgb(0x3C, 0x3C, 0x3C));
    private static readonly Pen LabelBorderPen = new(Brushes.Gray, 1);
    private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0x25, 0x63, 0xEB));
    private static readonly Pen SelectionPen = new(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1.5);
    private static readonly Pen GhostPen = new(
        new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1.5, new DashStyle([4, 4], 0));

    // Off-label content: dashed amber outline, matching the warning text color.
    private static readonly Pen WarnPen = new(
        new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)), 1.5, new DashStyle([4, 3], 0));

    // Pasteboard dim: translucent surface color washed over the expanded underlay so
    // off-label content stays visible but reads as "will not print".
    private static readonly SolidColorBrush DimBrush = new(Color.FromArgb(0xBE, 0xD9, 0xD9, 0xD9));
    private static readonly SolidColorBrush DarkDimBrush = new(Color.FromArgb(0xBE, 0x3C, 0x3C, 0x3C));

    // Ruler chrome, one set per theme.
    private static readonly SolidColorBrush RulerBrush = new(Color.FromRgb(0xEC, 0xEC, 0xEC));
    private static readonly SolidColorBrush DarkRulerBrush = new(Color.FromRgb(0x2E, 0x2E, 0x2E));
    private static readonly SolidColorBrush RulerExtentBrush = new(Colors.White);
    private static readonly SolidColorBrush DarkRulerExtentBrush = new(Color.FromRgb(0x4A, 0x4A, 0x4A));
    private static readonly Pen RulerTickPen = new(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), 1);
    private static readonly Pen DarkRulerTickPen = new(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), 1);
    private static readonly SolidColorBrush RulerTextBrush = new(Color.FromArgb(0x99, 0, 0, 0));
    private static readonly SolidColorBrush DarkRulerTextBrush = new(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
    private static readonly Pen CursorMarkerPen = new(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1);

    // Alignment guides: teal so they read apart from the blue selection and the amber
    // warnings. The transient ruler-press guide is dashed; a guide dragged onto a
    // ruler (about to be deleted) goes translucent.
    private static readonly SolidColorBrush GuideColorBrush = new(Color.FromRgb(0x0D, 0x94, 0x88));
    private static readonly Pen GuidePen = new(GuideColorBrush, 1);
    private static readonly Pen TempGuidePen = new(GuideColorBrush, 1, new DashStyle([4, 3], 0));
    private static readonly Pen DeleteGuidePen = new(
        new SolidColorBrush(Color.FromArgb(0x50, 0x0D, 0x94, 0x88)), 1, new DashStyle([2, 2], 0));

    // Snap feedback while dragging elements: pink, distinct from every other overlay.
    private static readonly Pen SnapPen = new(new SolidColorBrush(Color.FromRgb(0xEC, 0x48, 0x99)), 1);

    private readonly ElementBoundsCalculator _bounds = new();

    // Group drag.
    private bool _dragging;
    private Point _dragStartDots;
    private readonly List<(Element Element, int StartX, int StartY)> _dragItems = [];
    private int _dragDx;
    private int _dragDy;

    // Resize (single selection only). The gesture rect follows the pointer exactly
    // (anchored on the opposite side); the model snaps underneath it.
    private bool _resizing;
    private ResizeHandle _activeHandle;
    private DotRect _resizeStartBounds;
    private int _resizeStartX;
    private int _resizeStartY;
    private int _candidateWidth;
    private int _candidateHeight;
    private double _gestureX;
    private double _gestureY;
    private double _gestureW;
    private double _gestureH;

    // Rotation (single selection only). The outline rotates continuously with the
    // pointer, with magnetic snapping at the four orientations ZPL supports.
    private bool _rotating;
    private Point _rotateCenterDots;
    private double _rotateStartPointerDeg;
    private int _rotateStartDeg;
    private double _rotateVisualDeg;
    private DotRect _rotateStartBounds;
    private Orientation _rotateGestureStart;

    // Marquee selection.
    private bool _marquee;
    private Point _marqueeStart;
    private Point _marqueeCurrent;

    // Explicit view transform once the user zooms or pans; null means auto-fit.
    private double? _userScale;
    private Point _viewOrigin;
    private bool _panning;
    private Point _panLast;
    private BitmapInterpolationMode _interpolation = BitmapInterpolationMode.Unspecified;

    // Last pointer position over the canvas area, mirrored as markers in the rulers.
    private Point? _pointerPosition;

    // Guides. Holding the left button on a ruler shows a transient guide that follows
    // the pointer and vanishes on release; permanent guides (inserted from the ruler
    // context menu) live in the document, so undo and save cover them. Dragging a
    // permanent guide moves it; dropping it on its ruler deletes it.
    private enum GuideAxis
    {
        None,
        Vertical,
        Horizontal,
    }

    private GuideAxis _tempGuideAxis;
    private double _tempGuideDots;
    private GuideAxis _dragGuideAxis;
    private int _dragGuideIndex;
    private int _dragGuideStart;
    private bool _dragGuideDelete;

    // Element-drag snapping: the union of the dragged items' bounds at drag start,
    // the targets currently snapped to (for the highlight lines), and the target
    // positions collected at gesture start (guides, label edges and center, plus the
    // edges and centers of every element not taking part in the gesture).
    private DotRect _dragStartBounds;
    private int? _snapX;
    private int? _snapY;
    private readonly List<int> _snapTargetsX = [];
    private readonly List<int> _snapTargetsY = [];

    /// <summary>Screen distance within which a guide can be grabbed or snapped to.</summary>
    private const double GuideGrabPx = 5;
    private const double SnapPx = 6;

    public DesignerCanvas()
    {
        Focusable = true;

        // Pasteboard content and rulers can extend to the control edges; without an
        // explicit clip they would paint over sibling panels.
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    private bool IsDark => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

    /// <summary>The area around the label follows the app theme; the label itself stays
    /// white because it represents physical label stock (WYSIWYG).</summary>
    private IBrush SurfaceThemeBrush() => IsDark ? DarkSurfaceBrush : SurfaceBrush;

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

    /// <summary>True while an insert is armed: the next canvas click places the element.</summary>
    public bool IsPlacing
    {
        get => GetValue(IsPlacingProperty);
        set => SetValue(IsPlacingProperty, value);
    }

    /// <summary>Pasteboard margin baked into the underlay bitmap, in dots (see the
    /// view model); 0 means the underlay covers exactly the label.</summary>
    public int UnderlayMarginDots
    {
        get => GetValue(UnderlayMarginDotsProperty);
        set => SetValue(UnderlayMarginDotsProperty, value);
    }

    /// <summary>Raised whenever the view transform, document, or control size changes,
    /// so scrollbars can resync.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>Raised when the user clicks the canvas while an insert is armed (dot coordinates).</summary>
    public event Action<int, int>? PlaceRequested;

    /// <summary>Raised when the user presses Escape (cancels an armed insert).</summary>
    public event EventHandler? CancelRequested;

    /// <summary>Raised after the user commits an edit through the canvas (drag, resize, nudge).</summary>
    public event EventHandler? DocumentEdited;

    /// <summary>Raised continuously while a drag or resize is in progress. The model is
    /// already updated; listeners re-render the preview but must not record undo.</summary>
    public event EventHandler? LiveEdited;

    /// <summary>Raised when the user presses Delete with a selection.</summary>
    public event EventHandler? DeleteRequested;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DocumentProperty)
        {
            // A different document means a different size; go back to auto-fit.
            _userScale = null;
            ViewChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Auto-fit pins the label to the top-left corner, tight against the
    /// rulers, so the 0mm marks always line up with the label origin (Label Matrix
    /// style); zoom and pan then take over as an explicit transform.</summary>
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

        double scale = Math.Min(
            (Bounds.Width - RulerSize - 2 * FitGap) / doc.WidthDots,
            (Bounds.Height - RulerSize - 2 * FitGap) / doc.HeightDots);
        scale = Math.Max(scale, 0.01);

        return (scale, new Point(RulerSize + FitGap, RulerSize + FitGap));
    }

    /// <summary>Pasteboard margin in dots: the parking area kept around the label for
    /// dragging, scrolling, and the expanded preview.</summary>
    private static int PasteboardDots(LabelDocument doc) =>
        Units.MmToDots(ElementPlacement.PasteboardMarginMm, doc.Dpmm);

    /// <summary>Returns to the auto-fit view (Ctrl+0).</summary>
    public void ResetView()
    {
        _userScale = null;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Current zoom: screen pixels per printer dot (1 = 100%).</summary>
    public double GetZoom() => GetTransform().Scale;

    /// <summary>Multiplies the zoom, keeping the viewport center stationary.</summary>
    public void ZoomBy(double factor) => ZoomAt(ViewportCenter(), factor);

    /// <summary>Sets an absolute zoom, keeping the viewport center stationary.</summary>
    public void SetZoom(double scale)
    {
        double current = GetTransform().Scale;
        if (current > 0)
        {
            ZoomAt(ViewportCenter(), scale / current);
        }
    }

    private Point ViewportCenter() =>
        new((RulerSize + Bounds.Width) / 2, (RulerSize + Bounds.Height) / 2);

    /// <summary>Maps a dot-space position to control coordinates under the current
    /// view transform. Used by tooling and headless tests to aim pointer input.</summary>
    public Point DotsToView(double x, double y)
    {
        var (scale, origin) = GetTransform();
        return new Point(origin.X + x * scale, origin.Y + y * scale);
    }

    /// <summary>Extent, viewport, and offset of one scroll axis, in screen pixels.
    /// The extent is the label plus the pasteboard margin on both sides.</summary>
    public readonly record struct ScrollAxisInfo(double Extent, double Viewport, double Offset);

    public (ScrollAxisInfo Horizontal, ScrollAxisInfo Vertical) GetScrollInfo()
    {
        if (Document is not { } doc || doc.WidthDots <= 0 || doc.HeightDots <= 0)
        {
            return (default, default);
        }

        var (scale, origin) = GetTransform();
        double m = PasteboardDots(doc) * scale;

        var horizontal = new ScrollAxisInfo(
            doc.WidthDots * scale + 2 * m,
            Math.Max(Bounds.Width - RulerSize, 0),
            RulerSize - origin.X + m);
        var vertical = new ScrollAxisInfo(
            doc.HeightDots * scale + 2 * m,
            Math.Max(Bounds.Height - RulerSize, 0),
            RulerSize - origin.Y + m);
        return (horizontal, vertical);
    }

    /// <summary>Moves the view so the given offsets (same space as
    /// <see cref="GetScrollInfo"/>) land at the viewport start. Called by the scrollbars.</summary>
    public void SetScrollOffsets(double offsetX, double offsetY)
    {
        if (Document is not { } doc)
        {
            return;
        }

        EnsureExplicitTransform();
        double m = PasteboardDots(doc) * _userScale!.Value;
        _viewOrigin = new Point(RulerSize + m - offsetX, RulerSize + m - offsetY);
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ViewChanged?.Invoke(this, EventArgs.Empty);
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
        ViewChanged?.Invoke(this, EventArgs.Empty);
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
            ViewChanged?.Invoke(this, EventArgs.Empty);
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

        // Crisp printer dots when at or above 1:1; smooth only when downscaling.
        // (Bilinear upscaling of the 1-bit label is what makes text look blurry.)
        // Applying render options invalidates the visual, which is illegal inside the
        // render pass, so the change is posted to run right after it.
        BitmapInterpolationMode interpolation = scale >= 1
            ? BitmapInterpolationMode.None
            : BitmapInterpolationMode.HighQuality;
        if (interpolation != _interpolation)
        {
            _interpolation = interpolation;
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => RenderOptions.SetBitmapInterpolationMode(this, interpolation),
                Avalonia.Threading.DispatcherPriority.Background);
        }

        context.FillRectangle(Brushes.White, labelRect);

        if (Underlay is { } underlay)
        {
            // A pasteboard-expanded underlay carries off-label content; draw it over
            // the full expanded area, then wash everything outside the label with a
            // translucent surface tint so that content reads as "will not print".
            double um = UnderlayMarginDots * scale;
            Rect dest = um > 0 ? labelRect.Inflate(um) : labelRect;
            context.DrawImage(underlay, new Rect(underlay.Size), dest);

            if (um > 0)
            {
                IBrush dim = IsDark ? DarkDimBrush : DimBrush;
                context.FillRectangle(dim, new Rect(dest.X, dest.Y, dest.Width, labelRect.Y - dest.Y));
                context.FillRectangle(dim, new Rect(dest.X, labelRect.Bottom, dest.Width, dest.Bottom - labelRect.Bottom));
                context.FillRectangle(dim, new Rect(dest.X, labelRect.Y, labelRect.X - dest.X, labelRect.Height));
                context.FillRectangle(dim, new Rect(labelRect.Right, labelRect.Y, dest.Right - labelRect.Right, labelRect.Height));
            }
        }

        context.DrawRectangle(null, LabelBorderPen, labelRect.Inflate(0.5));

        // Amber dashed outline on anything that will not print exactly as drawn.
        foreach (Element element in doc.Elements.Where(el => el.IsVisible))
        {
            DotRect b = _bounds.GetBounds(element);
            if (ElementPlacement.Classify(element, b, doc.WidthDots, doc.HeightDots) ==
                PlacementStatus.Inside)
            {
                continue;
            }

            context.DrawRectangle(null, WarnPen, new Rect(
                origin.X + b.X * scale,
                origin.Y + b.Y * scale,
                Math.Max(b.Width * scale, 4),
                Math.Max(b.Height * scale, 4)));
        }

        if (Selection is { Count: > 0 } selection)
        {
            bool singleGesture = selection.Count == 1 && (_resizing || _rotating);

            foreach (Element element in selection.Items)
            {
                if (!doc.Elements.Contains(element))
                {
                    continue;
                }

                // During a resize the outline is the pointer-true gesture rect, so the
                // dragged handle stays under the cursor even while the content snaps.
                if (singleGesture && _resizing && element == selection.Primary)
                {
                    var gesture = new Rect(
                        origin.X + _gestureX * scale,
                        origin.Y + _gestureY * scale,
                        Math.Max(_gestureW * scale, 4),
                        Math.Max(_gestureH * scale, 4));

                    context.DrawRectangle(null, SelectionPen, gesture);
                    foreach ((_, Point center) in HandleCenters(gesture))
                    {
                        DrawHandle(context, center);
                    }

                    DrawReadout(context, $"{_candidateWidth} x {_candidateHeight}",
                        new Point(gesture.Right + 8, gesture.Bottom + 8));
                    continue;
                }

                // During a rotation the outline rotates continuously with the pointer.
                if (singleGesture && _rotating && element == selection.Primary)
                {
                    var start = new Rect(
                        origin.X + _rotateStartBounds.X * scale,
                        origin.Y + _rotateStartBounds.Y * scale,
                        Math.Max(_rotateStartBounds.Width * scale, 4),
                        Math.Max(_rotateStartBounds.Height * scale, 4));
                    var center = new Point(
                        origin.X + _rotateCenterDots.X * scale,
                        origin.Y + _rotateCenterDots.Y * scale);

                    double radians = (_rotateVisualDeg - _rotateStartDeg) * Math.PI / 180.0;
                    Matrix matrix =
                        Matrix.CreateTranslation(-center.X, -center.Y) *
                        Matrix.CreateRotation(radians) *
                        Matrix.CreateTranslation(center.X, center.Y);
                    using (context.PushTransform(matrix))
                    {
                        context.DrawRectangle(null, SelectionPen, start);
                    }

                    DrawReadout(context, $"{Math.Round(((_rotateVisualDeg % 360) + 360) % 360)}°",
                        new Point(center.X + 14, start.Top - 34));
                    continue;
                }

                DotRect bounds = _bounds.GetBounds(element);
                var rect = new Rect(
                    origin.X + bounds.X * scale,
                    origin.Y + bounds.Y * scale,
                    Math.Max(bounds.Width * scale, 4),
                    Math.Max(bounds.Height * scale, 4));

                context.DrawRectangle(null, SelectionPen, rect);

                if (selection.Count == 1 && !_dragging && !_resizing && !_rotating &&
                    !element.IsLocked)
                {
                    foreach ((_, Point center) in HandleCenters(rect))
                    {
                        DrawHandle(context, center);
                    }

                    // Rotation handle: a circle tethered above the selection.
                    Point rot = RotationHandleCenter(rect);
                    context.DrawLine(SelectionPen, new Point(rect.Center.X, rect.Top), rot);
                    context.DrawEllipse(Brushes.White, SelectionPen, rot, HandleSize / 2 + 1, HandleSize / 2 + 1);
                }
            }
        }

        if (_marquee)
        {
            context.DrawRectangle(null, GhostPen, MarqueeRect());
        }

        DrawGuides(context, doc, scale, origin);
        DrawRulers(context, doc, scale, origin);
    }

    /// <summary>Permanent guides, the snap highlight, and the transient ruler-press
    /// guide. Drawn over the content (guides are chrome) but under the rulers.</summary>
    private void DrawGuides(DrawingContext context, LabelDocument doc, double scale, Point origin)
    {
        for (int i = 0; i < doc.VerticalGuides.Count; i++)
        {
            bool deleting = _dragGuideAxis == GuideAxis.Vertical && _dragGuideIndex == i && _dragGuideDelete;
            double x = origin.X + doc.VerticalGuides[i] * scale;
            context.DrawLine(deleting ? DeleteGuidePen : GuidePen,
                new Point(x, RulerSize), new Point(x, Bounds.Height));
        }

        for (int i = 0; i < doc.HorizontalGuides.Count; i++)
        {
            bool deleting = _dragGuideAxis == GuideAxis.Horizontal && _dragGuideIndex == i && _dragGuideDelete;
            double y = origin.Y + doc.HorizontalGuides[i] * scale;
            context.DrawLine(deleting ? DeleteGuidePen : GuidePen,
                new Point(RulerSize, y), new Point(Bounds.Width, y));
        }

        // Snap feedback: the target line the dragged selection just aligned with.
        if (_snapX is { } snapX)
        {
            double x = origin.X + snapX * scale;
            context.DrawLine(SnapPen, new Point(x, RulerSize), new Point(x, Bounds.Height));
        }

        if (_snapY is { } snapY)
        {
            double y = origin.Y + snapY * scale;
            context.DrawLine(SnapPen, new Point(RulerSize, y), new Point(Bounds.Width, y));
        }

        if (_tempGuideAxis == GuideAxis.Vertical)
        {
            double x = origin.X + _tempGuideDots * scale;
            context.DrawLine(TempGuidePen, new Point(x, RulerSize), new Point(x, Bounds.Height));
            DrawReadout(context, MmText(_tempGuideDots, doc), new Point(x + 6, RulerSize + 6));
        }
        else if (_tempGuideAxis == GuideAxis.Horizontal)
        {
            double y = origin.Y + _tempGuideDots * scale;
            context.DrawLine(TempGuidePen, new Point(RulerSize, y), new Point(Bounds.Width, y));
            DrawReadout(context, MmText(_tempGuideDots, doc), new Point(RulerSize + 6, y + 6));
        }

        // A guide being repositioned gets the same mm readout at the pointer.
        if (_dragGuideAxis != GuideAxis.None && !_dragGuideDelete && _pointerPosition is { } p)
        {
            int dots = _dragGuideAxis == GuideAxis.Vertical
                ? doc.VerticalGuides[_dragGuideIndex]
                : doc.HorizontalGuides[_dragGuideIndex];
            DrawReadout(context, MmText(dots, doc), new Point(p.X + 12, p.Y + 12));
        }
    }

    private static string MmText(double dots, LabelDocument doc) =>
        (dots / doc.Dpmm).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " mm";

    /// <summary>Millimeter rulers pinned along the top and left edges. All mapping goes
    /// through dpmm (x = origin + mm * dpmm * scale) so a density change re-scales the
    /// ruler, never the other way around. The white band marks the label's extent and
    /// the thin accent lines mirror the pointer.</summary>
    private void DrawRulers(DrawingContext context, LabelDocument doc, double scale, Point origin)
    {
        IBrush band = IsDark ? DarkRulerBrush : RulerBrush;
        IBrush extent = IsDark ? DarkRulerExtentBrush : RulerExtentBrush;
        Pen tick = IsDark ? DarkRulerTickPen : RulerTickPen;
        IBrush text = IsDark ? DarkRulerTextBrush : RulerTextBrush;

        context.FillRectangle(band, new Rect(0, 0, Bounds.Width, RulerSize));
        context.FillRectangle(band, new Rect(0, 0, RulerSize, Bounds.Height));

        // Label extent highlight inside each band.
        double x0 = Math.Max(origin.X, RulerSize);
        double x1 = Math.Min(origin.X + doc.WidthDots * scale, Bounds.Width);
        if (x1 > x0)
        {
            context.FillRectangle(extent, new Rect(x0, 0, x1 - x0, RulerSize));
        }

        double y0 = Math.Max(origin.Y, RulerSize);
        double y1 = Math.Min(origin.Y + doc.HeightDots * scale, Bounds.Height);
        if (y1 > y0)
        {
            context.FillRectangle(extent, new Rect(0, y0, RulerSize, y1 - y0));
        }

        double pxPerMm = doc.Dpmm * scale;
        if (pxPerMm > 0)
        {
            (double major, double minor) = RulerSteps(pxPerMm);
            DrawRulerAxis(context, tick, text, major, minor, pxPerMm, origin.X, Bounds.Width, horizontal: true);
            DrawRulerAxis(context, tick, text, major, minor, pxPerMm, origin.Y, Bounds.Height, horizontal: false);
        }

        // Guide markers: a short teal tick at each guide position, so guides can be
        // found (and grabbed) even when scrolled out of view vertically.
        foreach (int guide in doc.VerticalGuides)
        {
            double gx = origin.X + guide * scale;
            if (gx >= RulerSize)
            {
                context.DrawLine(GuidePen, new Point(gx, RulerSize - 7), new Point(gx, RulerSize));
            }
        }

        foreach (int guide in doc.HorizontalGuides)
        {
            double gy = origin.Y + guide * scale;
            if (gy >= RulerSize)
            {
                context.DrawLine(GuidePen, new Point(RulerSize - 7, gy), new Point(RulerSize, gy));
            }
        }

        // Pointer markers.
        if (_pointerPosition is { } p)
        {
            if (p.X >= RulerSize)
            {
                context.DrawLine(CursorMarkerPen, new Point(p.X, 0), new Point(p.X, RulerSize));
            }

            if (p.Y >= RulerSize)
            {
                context.DrawLine(CursorMarkerPen, new Point(0, p.Y), new Point(RulerSize, p.Y));
            }
        }

        // Corner box with the unit, and hairlines separating the bands from the canvas.
        context.FillRectangle(band, new Rect(0, 0, RulerSize, RulerSize));
        var unit = new FormattedText(
            "mm", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 9, text);
        context.DrawText(unit, new Point(
            (RulerSize - unit.Width) / 2, (RulerSize - unit.Height) / 2));

        context.DrawLine(tick, new Point(0, RulerSize + 0.5), new Point(Bounds.Width, RulerSize + 0.5));
        context.DrawLine(tick, new Point(RulerSize + 0.5, 0), new Point(RulerSize + 0.5, Bounds.Height));
    }

    /// <summary>Tick steps in mm adapted to the zoom: labeled major ticks stay at least
    /// ~34px apart, minor ticks subdivide by 5 (or 2) while they stay readable.</summary>
    private static (double Major, double Minor) RulerSteps(double pxPerMm)
    {
        ReadOnlySpan<double> steps = [1, 2, 5, 10, 20, 50, 100, 200, 500];
        double major = steps[^1];
        foreach (double step in steps)
        {
            if (step * pxPerMm >= 34)
            {
                major = step;
                break;
            }
        }

        double minor = major * pxPerMm / 5 >= 6 ? major / 5
            : major * pxPerMm / 2 >= 6 ? major / 2
            : major;
        return (major, minor);
    }

    private static void DrawRulerAxis(
        DrawingContext context, Pen tick, IBrush text,
        double major, double minor, double pxPerMm,
        double originPx, double limitPx, bool horizontal)
    {
        double firstMm = Math.Floor((RulerSize - originPx) / pxPerMm / minor) * minor;
        for (double mm = firstMm; ; mm += minor)
        {
            double px = Math.Round(originPx + mm * pxPerMm) + 0.5;
            if (px > limitPx)
            {
                break;
            }

            if (px < RulerSize)
            {
                continue;
            }

            bool isMajor = Math.Abs(mm / major - Math.Round(mm / major)) < 1e-6;
            double len = isMajor ? 9 : 4;

            if (horizontal)
            {
                context.DrawLine(tick, new Point(px, RulerSize - len), new Point(px, RulerSize));
            }
            else
            {
                context.DrawLine(tick, new Point(RulerSize - len, px), new Point(RulerSize, px));
            }

            if (isMajor)
            {
                var label = new FormattedText(
                    Math.Round(mm).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 9, text);
                context.DrawText(label, horizontal
                    ? new Point(px + 2, 1)
                    : new Point(2, px + 1));
            }
        }
    }

    private Rect MarqueeRect() => new(
        Math.Min(_marqueeStart.X, _marqueeCurrent.X),
        Math.Min(_marqueeStart.Y, _marqueeCurrent.Y),
        Math.Abs(_marqueeCurrent.X - _marqueeStart.X),
        Math.Abs(_marqueeCurrent.Y - _marqueeStart.Y));

    /// <summary>The selection rectangle in screen space, only for a single unlocked selection.</summary>
    private Rect? SelectionScreenRect()
    {
        if (Selection is not { Count: 1 } selection ||
            selection.Primary is not { IsLocked: false } selected ||
            Document is not { } doc || !doc.Elements.Contains(selected))
        {
            return null;
        }

        var (scale, origin) = GetTransform();
        DotRect bounds = _bounds.GetBounds(selected);
        return new Rect(
            origin.X + bounds.X * scale,
            origin.Y + bounds.Y * scale,
            Math.Max(bounds.Width * scale, 4),
            Math.Max(bounds.Height * scale, 4));
    }

    /// <summary>Corner handles resize proportionally; edge-midpoint handles resize one axis.</summary>
    private static IEnumerable<(ResizeHandle Handle, Point Center)> HandleCenters(Rect r)
    {
        yield return (ResizeHandle.TopLeft, r.TopLeft);
        yield return (ResizeHandle.Top, new Point(r.Center.X, r.Top));
        yield return (ResizeHandle.TopRight, r.TopRight);
        yield return (ResizeHandle.Right, new Point(r.Right, r.Center.Y));
        yield return (ResizeHandle.BottomRight, r.BottomRight);
        yield return (ResizeHandle.Bottom, new Point(r.Center.X, r.Bottom));
        yield return (ResizeHandle.BottomLeft, r.BottomLeft);
        yield return (ResizeHandle.Left, new Point(r.Left, r.Center.Y));
    }

    /// <summary>Slightly larger than the drawn handle so it is easy to grab.</summary>
    private static Rect GrabRect(Point center)
    {
        const double grab = HandleSize + 6;
        return new Rect(center.X - grab / 2, center.Y - grab / 2, grab, grab);
    }

    private static Point RotationHandleCenter(Rect r) => new(r.Center.X, r.Top - 22);

    private static void DrawHandle(DrawingContext context, Point center)
    {
        var rect = new Rect(
            center.X - HandleSize / 2, center.Y - HandleSize / 2, HandleSize, HandleSize);
        context.DrawRectangle(Brushes.White, SelectionPen, rect);
    }

    /// <summary>Small blue readout (size or angle) next to the active gesture.</summary>
    private static void DrawReadout(DrawingContext context, string text, Point position)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            13,
            AccentBrush);
        context.DrawText(formatted, position);
    }

    private static StandardCursorType HandleCursor(ResizeHandle handle) => handle switch
    {
        ResizeHandle.TopLeft => StandardCursorType.TopLeftCorner,
        ResizeHandle.Top => StandardCursorType.TopSide,
        ResizeHandle.TopRight => StandardCursorType.TopRightCorner,
        ResizeHandle.Right => StandardCursorType.RightSide,
        ResizeHandle.BottomRight => StandardCursorType.BottomRightCorner,
        ResizeHandle.Bottom => StandardCursorType.BottomSide,
        ResizeHandle.BottomLeft => StandardCursorType.BottomLeftCorner,
        ResizeHandle.Left => StandardCursorType.LeftSide,
        _ => StandardCursorType.Arrow,
    };

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
        PointerPointProperties pointerProps = e.GetCurrentPoint(this).Properties;

        // Ruler presses: holding the left button shows a transient guide that follows
        // the pointer (gone on release); the right button opens the guide menu.
        bool onTopRuler = p.Y < RulerSize && p.X >= RulerSize;
        bool onLeftRuler = p.X < RulerSize && p.Y >= RulerSize;
        if (onTopRuler || onLeftRuler)
        {
            GuideAxis axis = onTopRuler ? GuideAxis.Vertical : GuideAxis.Horizontal;
            double dots = onTopRuler ? dotX : dotY;

            if (pointerProps.IsRightButtonPressed)
            {
                ShowRulerMenu(doc, axis, dots);
            }
            else if (pointerProps.IsLeftButtonPressed)
            {
                // Double click inserts a permanent guide right away; a single press
                // starts the transient guide that vanishes on release.
                if (e.ClickCount >= 2)
                {
                    InsertGuide(doc, axis, Math.Round(dots / doc.Dpmm));
                }
                else
                {
                    _tempGuideAxis = axis;
                    _tempGuideDots = dots;
                    e.Pointer.Capture(this);
                    InvalidateVisual();
                }
            }

            return;
        }

        // The ruler corner is dead space.
        if (p.X < RulerSize || p.Y < RulerSize)
        {
            return;
        }

        // Right-click on the canvas: its only meaning is removing a nearby guide.
        if (pointerProps.IsRightButtonPressed)
        {
            if (!IsPlacing && FindGuideAt(doc, p, scale, origin) is { } nearGuide)
            {
                ShowGuideMenu(doc, nearGuide.Axis, nearGuide.Index);
            }

            return;
        }

        // Armed insert: this click places the new element.
        if (IsPlacing)
        {
            PlaceRequested?.Invoke((int)Math.Round(dotX), (int)Math.Round(dotY));
            e.Handled = true;
            return;
        }

        // Handles win over element hit-testing, otherwise grabbing the corner of a
        // small element would just re-select it.
        if (SelectionScreenRect() is { } selRect && selection.Primary is { } primary)
        {
            if (GrabRect(RotationHandleCenter(selRect)).Contains(p))
            {
                DotRect b = _bounds.GetBounds(primary);
                _rotating = true;
                _rotateCenterDots = new Point(b.X + b.Width / 2.0, b.Y + b.Height / 2.0);
                _rotateStartPointerDeg = PointerAngleDeg(dotX, dotY);
                _rotateStartDeg = OrientationDegrees(primary.Orientation);
                _rotateVisualDeg = _rotateStartDeg;
                _rotateStartBounds = b;
                _rotateGestureStart = primary.Orientation;
                Cursor = new Cursor(StandardCursorType.Hand);
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }

            foreach ((ResizeHandle kind, Point center) in HandleCenters(selRect))
            {
                if (!GrabRect(center).Contains(p))
                {
                    continue;
                }

                _resizing = true;
                _activeHandle = kind;
                _dragStartDots = new Point(dotX, dotY);
                _resizeStartBounds = _bounds.GetBounds(primary);
                _resizeStartX = primary.X;
                _resizeStartY = primary.Y;
                BuildSnapTargets(doc, [primary]);
                _candidateWidth = _resizeStartBounds.Width;
                _candidateHeight = _resizeStartBounds.Height;
                _gestureX = _resizeStartBounds.X;
                _gestureY = _resizeStartBounds.Y;
                _gestureW = _resizeStartBounds.Width;
                _gestureH = _resizeStartBounds.Height;
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }
        }

        // Grabbing a permanent guide to move it (or drop it on a ruler to delete).
        // The threshold is tight so elements stay the primary interaction.
        if (FindGuideAt(doc, p, scale, origin) is { } grabbed)
        {
            _dragGuideAxis = grabbed.Axis;
            _dragGuideIndex = grabbed.Index;
            _dragGuideStart = grabbed.Axis == GuideAxis.Vertical
                ? doc.VerticalGuides[grabbed.Index]
                : doc.HorizontalGuides[grabbed.Index];
            _dragGuideDelete = false;
            Cursor = new Cursor(grabbed.Axis == GuideAxis.Vertical
                ? StandardCursorType.SizeWestEast
                : StandardCursorType.SizeNorthSouth);
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

            // Union of the dragged bounds, the box that snaps against guides.
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach ((Element element, _, _) in _dragItems)
            {
                DotRect b = _bounds.GetBounds(element);
                minX = Math.Min(minX, b.X);
                minY = Math.Min(minY, b.Y);
                maxX = Math.Max(maxX, b.X + b.Width);
                maxY = Math.Max(maxY, b.Y + b.Height);
            }

            _dragStartBounds = new DotRect(minX, minY, maxX - minX, maxY - minY);
            BuildSnapTargets(doc, _dragItems.Select(i => i.Element).ToHashSet());
            e.Pointer.Capture(this);
        }

        InvalidateVisual();
    }

    /// <summary>Collects the snap targets for a starting gesture: guides, label edges
    /// and center, and smart-guide targets (edges and centers of every visible element
    /// not taking part in the gesture). Positions cannot change mid-gesture, so this
    /// runs once at gesture start.</summary>
    private void BuildSnapTargets(LabelDocument doc, HashSet<Element> excluded)
    {
        _snapTargetsX.Clear();
        _snapTargetsY.Clear();

        _snapTargetsX.AddRange(doc.VerticalGuides);
        _snapTargetsX.AddRange([0, doc.WidthDots / 2, doc.WidthDots]);
        _snapTargetsY.AddRange(doc.HorizontalGuides);
        _snapTargetsY.AddRange([0, doc.HeightDots / 2, doc.HeightDots]);

        foreach (Element element in doc.Elements)
        {
            if (!element.IsVisible || excluded.Contains(element))
            {
                continue;
            }

            DotRect b = _bounds.GetBounds(element);
            _snapTargetsX.AddRange([b.X, b.X + b.Width / 2, b.X + b.Width]);
            _snapTargetsY.AddRange([b.Y, b.Y + b.Height / 2, b.Y + b.Height]);
        }
    }

    /// <summary>The permanent guide within grab range of the pointer, if any.</summary>
    private (GuideAxis Axis, int Index)? FindGuideAt(
        LabelDocument doc, Point p, double scale, Point origin)
    {
        double best = GuideGrabPx;
        (GuideAxis, int)? result = null;

        for (int i = 0; i < doc.VerticalGuides.Count; i++)
        {
            double distance = Math.Abs(origin.X + doc.VerticalGuides[i] * scale - p.X);
            if (distance <= best)
            {
                best = distance;
                result = (GuideAxis.Vertical, i);
            }
        }

        for (int i = 0; i < doc.HorizontalGuides.Count; i++)
        {
            double distance = Math.Abs(origin.Y + doc.HorizontalGuides[i] * scale - p.Y);
            if (distance <= best)
            {
                best = distance;
                result = (GuideAxis.Horizontal, i);
            }
        }

        return result;
    }

    /// <summary>Adds a permanent guide at the given millimeter position, clamped to
    /// the pasteboard. Shared by the ruler double click and the ruler menu.</summary>
    private void InsertGuide(LabelDocument doc, GuideAxis axis, double mm)
    {
        int margin = PasteboardDots(doc);
        int limit = axis == GuideAxis.Vertical ? doc.WidthDots : doc.HeightDots;
        int guideDots = Math.Clamp(Units.MmToDots(mm, doc.Dpmm), -margin, limit + margin);
        (axis == GuideAxis.Vertical ? doc.VerticalGuides : doc.HorizontalGuides).Add(guideDots);
        DocumentEdited?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Right-click on a ruler: insert a guide at the pointer (rounded to the
    /// nearest whole millimeter) or clear all guides.</summary>
    private void ShowRulerMenu(LabelDocument doc, GuideAxis axis, double dots)
    {
        double mm = Math.Round(dots / doc.Dpmm);

        var menu = new MenuFlyout();
        var insert = new MenuItem
        {
            Header = "Insert guide at " +
                mm.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " mm",
        };
        insert.Click += (_, _) => InsertGuide(doc, axis, mm);
        menu.Items.Add(insert);

        if (doc.VerticalGuides.Count + doc.HorizontalGuides.Count > 0)
        {
            var removeAll = new MenuItem { Header = "Remove all guides" };
            removeAll.Click += (_, _) =>
            {
                doc.VerticalGuides.Clear();
                doc.HorizontalGuides.Clear();
                DocumentEdited?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            };
            menu.Items.Add(removeAll);
        }

        menu.ShowAt(this, showAtPointer: true);
    }

    /// <summary>Right-click near a guide on the canvas: remove that guide.</summary>
    private void ShowGuideMenu(LabelDocument doc, GuideAxis axis, int index)
    {
        IList<int> guides = axis == GuideAxis.Vertical ? doc.VerticalGuides : doc.HorizontalGuides;
        var menu = new MenuFlyout();
        var remove = new MenuItem { Header = "Remove guide at " + MmText(guides[index], doc) };
        remove.Click += (_, _) =>
        {
            if (index < guides.Count)
            {
                guides.RemoveAt(index);
                DocumentEdited?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            }
        };
        menu.Items.Add(remove);
        menu.ShowAt(this, showAtPointer: true);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point p = e.GetPosition(this);
        _pointerPosition = p;

        if (_panning)
        {
            _viewOrigin = new Point(
                _viewOrigin.X + (p.X - _panLast.X),
                _viewOrigin.Y + (p.Y - _panLast.Y));
            _panLast = p;
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_tempGuideAxis != GuideAxis.None)
        {
            (double viewScale, Point viewOrigin) = GetTransform();
            _tempGuideDots = _tempGuideAxis == GuideAxis.Vertical
                ? (p.X - viewOrigin.X) / viewScale
                : (p.Y - viewOrigin.Y) / viewScale;
            InvalidateVisual();
            return;
        }

        if (_dragGuideAxis != GuideAxis.None && Document is { } guideDoc)
        {
            (double viewScale, Point viewOrigin) = GetTransform();
            int margin = PasteboardDots(guideDoc);
            if (_dragGuideAxis == GuideAxis.Vertical)
            {
                int value = (int)Math.Round((p.X - viewOrigin.X) / viewScale);
                guideDoc.VerticalGuides[_dragGuideIndex] =
                    Math.Clamp(value, -margin, guideDoc.WidthDots + margin);
                _dragGuideDelete = p.Y < RulerSize;
            }
            else
            {
                int value = (int)Math.Round((p.Y - viewOrigin.Y) / viewScale);
                guideDoc.HorizontalGuides[_dragGuideIndex] =
                    Math.Clamp(value, -margin, guideDoc.HeightDots + margin);
                _dragGuideDelete = p.X < RulerSize;
            }

            InvalidateVisual();
            return;
        }

        if (_marquee)
        {
            _marqueeCurrent = p;
            InvalidateVisual();
            return;
        }

        if (!_dragging && !_resizing && !_rotating)
        {
            UpdateHoverCursor(p);

            // Keep the ruler pointer markers tracking.
            InvalidateVisual();
            return;
        }

        if (Document is not { } doc)
        {
            return;
        }

        var (scale, origin) = GetTransform();
        double dotX = (p.X - origin.X) / scale;
        double dotY = (p.Y - origin.Y) / scale;

        if (_rotating)
        {
            if (Selection?.Primary is { } primary)
            {
                double delta = PointerAngleDeg(dotX, dotY) - _rotateStartPointerDeg;
                double raw = ((_rotateStartDeg + delta) % 360 + 360) % 360;

                // Magnetic snap: within 10 degrees of a legal orientation the outline
                // locks onto it; elsewhere it follows the pointer freely.
                double nearest = Math.Round(raw / 90.0) * 90 % 360;
                double distance = raw - Math.Round(raw / 90.0) * 90;
                _rotateVisualDeg = Math.Abs(distance) <= 10 ? nearest : raw;

                Orientation snapped = DegreesToOrientation(raw);
                if (primary.Orientation != snapped)
                {
                    primary.Orientation = snapped;
                    LiveEdited?.Invoke(this, EventArgs.Empty);
                }
            }

            InvalidateVisual();
            return;
        }

        if (_resizing)
        {
            ComputeGestureRect(dotX, dotY);
            _snapX = null;
            _snapY = null;
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                SnapResizeGesture(scale);
            }

            _candidateWidth = Math.Max((int)Math.Round(_gestureW), 4);
            _candidateHeight = Math.Max((int)Math.Round(_gestureH), 4);
            ApplyCandidateResize();
        }
        else
        {
            int dx = (int)Math.Round(dotX - _dragStartDots.X);
            int dy = (int)Math.Round(dotY - _dragStartDots.Y);

            // Snap the moved selection to the targets collected at drag start (guides,
            // label edges and center, other elements); Alt drags free. Snap first,
            // then clamp to the pasteboard.
            _snapX = null;
            _snapY = null;
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                int threshold = Math.Max((int)Math.Round(SnapPx / scale), 1);
                (int shiftX, _snapX) = GuideSnapper.Snap(
                    _dragStartBounds.X + dx,
                    _dragStartBounds.X + _dragStartBounds.Width + dx,
                    _snapTargetsX,
                    threshold);
                (int shiftY, _snapY) = GuideSnapper.Snap(
                    _dragStartBounds.Y + dy,
                    _dragStartBounds.Y + _dragStartBounds.Height + dy,
                    _snapTargetsY,
                    threshold);
                dx += shiftX;
                dy += shiftY;
            }

            (_dragDx, _dragDy) = ClampGroupDelta(doc, dx, dy);

            // Move the model live so the label content follows the pointer.
            foreach ((Element element, int startX, int startY) in _dragItems)
            {
                element.X = startX + _dragDx;
                element.Y = startY + _dragDy;
            }
        }

        LiveEdited?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerPosition = null;
        InvalidateVisual();
    }

    private void UpdateHoverCursor(Point p)
    {
        if (IsPlacing)
        {
            Cursor = new Cursor(StandardCursorType.Cross);
            return;
        }

        if (SelectionScreenRect() is { } selRect)
        {
            if (GrabRect(RotationHandleCenter(selRect)).Contains(p))
            {
                Cursor = new Cursor(StandardCursorType.Hand);
                return;
            }

            foreach ((ResizeHandle kind, Point center) in HandleCenters(selRect))
            {
                if (GrabRect(center).Contains(p))
                {
                    Cursor = new Cursor(HandleCursor(kind));
                    return;
                }
            }
        }

        if (Document is { } doc && p.X >= RulerSize && p.Y >= RulerSize)
        {
            var (scale, origin) = GetTransform();
            if (FindGuideAt(doc, p, scale, origin) is { } guide)
            {
                Cursor = new Cursor(guide.Axis == GuideAxis.Vertical
                    ? StandardCursorType.SizeWestEast
                    : StandardCursorType.SizeNorthSouth);
                return;
            }
        }

        Cursor = Cursor.Default;
    }

    /// <summary>The pointer-true gesture rect: the dragged edge or corner follows the
    /// pointer exactly, the opposite side stays anchored, and corners scale both axes
    /// by the smooth distance ratio from the anchor (no dominant-axis flip-flopping).</summary>
    private void ComputeGestureRect(double dotX, double dotY)
    {
        double startX = _resizeStartBounds.X;
        double startY = _resizeStartBounds.Y;
        double startW = Math.Max(_resizeStartBounds.Width, 1);
        double startH = Math.Max(_resizeStartBounds.Height, 1);
        double right = startX + startW;
        double bottom = startY + startH;

        bool left = _activeHandle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        bool top = _activeHandle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;

        switch (_activeHandle)
        {
            case ResizeHandle.Left:
                _gestureW = Math.Max(right - dotX, 4);
                _gestureX = right - _gestureW;
                break;

            case ResizeHandle.Right:
                _gestureW = Math.Max(dotX - startX, 4);
                break;

            case ResizeHandle.Top:
                _gestureH = Math.Max(bottom - dotY, 4);
                _gestureY = bottom - _gestureH;
                break;

            case ResizeHandle.Bottom:
                _gestureH = Math.Max(dotY - startY, 4);
                break;

            default:
            {
                // Corner: proportional scale by the distance ratio anchor -> pointer
                // over anchor -> original corner. Smooth and monotonic under the cursor.
                double anchorX = left ? right : startX;
                double anchorY = top ? bottom : startY;
                double cornerX = left ? startX : right;
                double cornerY = top ? startY : bottom;

                double startDist = Math.Max(
                    Math.Sqrt(Math.Pow(cornerX - anchorX, 2) + Math.Pow(cornerY - anchorY, 2)), 1);
                double nowDist = Math.Sqrt(
                    Math.Pow(dotX - anchorX, 2) + Math.Pow(dotY - anchorY, 2));
                double factor = Math.Max(nowDist / startDist, 0.02);

                _gestureW = Math.Max(startW * factor, 4);
                _gestureH = Math.Max(startH * factor, 4);
                _gestureX = left ? anchorX - _gestureW : anchorX;
                _gestureY = top ? anchorY - _gestureH : anchorY;
                break;
            }
        }
    }

    /// <summary>Snaps the moving edges of the resize gesture rect to the shared snap
    /// targets. Edge handles snap their single moving edge; corner handles snap
    /// whichever axis has the nearest target and keep the proportional factor, so a
    /// snapped corner resize still scales both axes together.</summary>
    private void SnapResizeGesture(double scale)
    {
        int threshold = Math.Max((int)Math.Round(SnapPx / scale), 1);
        bool left = _activeHandle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        bool top = _activeHandle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;

        if (_activeHandle is ResizeHandle.Left or ResizeHandle.Right)
        {
            int edge = (int)Math.Round(left ? _gestureX : _gestureX + _gestureW);
            (int shift, int? target) = GuideSnapper.Snap(edge, edge, _snapTargetsX, threshold);
            if (target is not null)
            {
                if (left)
                {
                    _gestureX += shift;
                    _gestureW = Math.Max(_gestureW - shift, 4);
                }
                else
                {
                    _gestureW = Math.Max(_gestureW + shift, 4);
                }

                _snapX = target;
            }

            return;
        }

        if (_activeHandle is ResizeHandle.Top or ResizeHandle.Bottom)
        {
            int edge = (int)Math.Round(top ? _gestureY : _gestureY + _gestureH);
            (int shift, int? target) = GuideSnapper.Snap(edge, edge, _snapTargetsY, threshold);
            if (target is not null)
            {
                if (top)
                {
                    _gestureY += shift;
                    _gestureH = Math.Max(_gestureH - shift, 4);
                }
                else
                {
                    _gestureH = Math.Max(_gestureH + shift, 4);
                }

                _snapY = target;
            }

            return;
        }

        // Corner: both edges move; snap the closer one and rescale proportionally
        // from the anchor so the aspect gesture stays intact.
        double startW = Math.Max(_resizeStartBounds.Width, 1);
        double startH = Math.Max(_resizeStartBounds.Height, 1);
        double anchorX = left ? _resizeStartBounds.X + startW : _resizeStartBounds.X;
        double anchorY = top ? _resizeStartBounds.Y + startH : _resizeStartBounds.Y;

        int movingX = (int)Math.Round(left ? _gestureX : _gestureX + _gestureW);
        int movingY = (int)Math.Round(top ? _gestureY : _gestureY + _gestureH);
        (int shiftX, int? targetX) = GuideSnapper.Snap(movingX, movingX, _snapTargetsX, threshold);
        (int shiftY, int? targetY) = GuideSnapper.Snap(movingY, movingY, _snapTargetsY, threshold);

        bool useX = targetX is not null && (targetY is null || Math.Abs(shiftX) <= Math.Abs(shiftY));
        double factor;
        if (useX)
        {
            factor = Math.Abs(targetX!.Value - anchorX) / startW;
            _snapX = targetX;
        }
        else if (targetY is not null)
        {
            factor = Math.Abs(targetY.Value - anchorY) / startH;
            _snapY = targetY;
        }
        else
        {
            return;
        }

        factor = Math.Max(factor, 0.02);
        _gestureW = Math.Max(startW * factor, 4);
        _gestureH = Math.Max(startH * factor, 4);
        _gestureX = left ? anchorX - _gestureW : anchorX;
        _gestureY = top ? anchorY - _gestureH : anchorY;
    }

    /// <summary>Puts the element's origin where the gesture rect says, compensating for
    /// bounds offsets (e.g. the QR vertical offset), so the anchored side never drifts
    /// even when the type-specific resize snaps.</summary>
    private void RepositionToGesture()
    {
        if (Selection?.Primary is not { } primary)
        {
            return;
        }

        DotRect bounds = _bounds.GetBounds(primary);
        int offX = bounds.X - primary.X;
        int offY = bounds.Y - primary.Y;

        bool left = _activeHandle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        bool top = _activeHandle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;

        // Anchored sides pin the real (possibly snapped) bounds to the anchor edge;
        // free sides keep the original origin.
        int anchorRight = _resizeStartBounds.X + _resizeStartBounds.Width;
        int anchorBottom = _resizeStartBounds.Y + _resizeStartBounds.Height;
        primary.X = left ? anchorRight - bounds.Width - offX : _resizeStartX;
        primary.Y = top ? anchorBottom - bounds.Height - offY : _resizeStartY;
    }

    private double PointerAngleDeg(double dotX, double dotY) =>
        Math.Atan2(dotY - _rotateCenterDots.Y, dotX - _rotateCenterDots.X) * 180.0 / Math.PI;

    private static int OrientationDegrees(Orientation orientation) => orientation switch
    {
        Orientation.Rotated90 => 90,
        Orientation.Rotated180 => 180,
        Orientation.Rotated270 => 270,
        _ => 0,
    };

    private static Orientation DegreesToOrientation(double degrees)
    {
        int quadrant = ((int)Math.Round(degrees / 90.0) % 4 + 4) % 4;
        return quadrant switch
        {
            1 => Orientation.Rotated90,
            2 => Orientation.Rotated180,
            3 => Orientation.Rotated270,
            _ => Orientation.Normal,
        };
    }

    /// <summary>Applies the resize gesture to the model. Targets derive from the start
    /// bounds plus the pointer delta, so repeated application is idempotent.</summary>
    private void ApplyCandidateResize()
    {
        if (Selection?.Primary is not { } primary)
        {
            return;
        }

        // The gesture targets the on-screen (rotated) footprint; the resizer
        // reasons in the element's intrinsic axes, so swap for 90/270.
        (int w, int h) = primary.Orientation is Orientation.Rotated90 or Orientation.Rotated270
            ? (_candidateHeight, _candidateWidth)
            : (_candidateWidth, _candidateHeight);
        ElementResizer.Resize(primary, w, h);
        RepositionToGesture();
    }

    /// <summary>Clamps a group-move delta so every dragged element stays on the
    /// pasteboard (the label plus its parking margin), preserving the elements'
    /// relative offsets. Leaving the label itself is allowed; the element then shows
    /// dimmed with a warning and is skipped at print time.</summary>
    private (int Dx, int Dy) ClampGroupDelta(LabelDocument doc, int dx, int dy)
    {
        int margin = PasteboardDots(doc);
        int dxLow = int.MinValue, dxHigh = int.MaxValue;
        int dyLow = int.MinValue, dyHigh = int.MaxValue;

        foreach ((Element _, int startX, int startY) in _dragItems)
        {
            dxLow = Math.Max(dxLow, -margin - startX);
            dxHigh = Math.Min(dxHigh, Math.Max(doc.WidthDots - 1, 0) + margin - startX);
            dyLow = Math.Max(dyLow, -margin - startY);
            dyHigh = Math.Min(dyHigh, Math.Max(doc.HeightDots - 1, 0) + margin - startY);
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

        // The transient ruler-press guide never survives the release.
        if (_tempGuideAxis != GuideAxis.None)
        {
            _tempGuideAxis = GuideAxis.None;
            e.Pointer.Capture(null);
            InvalidateVisual();
            return;
        }

        if (_dragGuideAxis != GuideAxis.None && Document is { } guideDoc)
        {
            bool guideChanged;
            if (_dragGuideDelete)
            {
                // Dropped back on its ruler: the guide is removed.
                if (_dragGuideAxis == GuideAxis.Vertical)
                {
                    guideDoc.VerticalGuides.RemoveAt(_dragGuideIndex);
                }
                else
                {
                    guideDoc.HorizontalGuides.RemoveAt(_dragGuideIndex);
                }

                guideChanged = true;
            }
            else
            {
                int now = _dragGuideAxis == GuideAxis.Vertical
                    ? guideDoc.VerticalGuides[_dragGuideIndex]
                    : guideDoc.HorizontalGuides[_dragGuideIndex];
                guideChanged = now != _dragGuideStart;
            }

            _dragGuideAxis = GuideAxis.None;
            _dragGuideDelete = false;
            Cursor = Cursor.Default;
            e.Pointer.Capture(null);
            if (guideChanged)
            {
                DocumentEdited?.Invoke(this, EventArgs.Empty);
            }

            InvalidateVisual();
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

        if (_rotating)
        {
            _rotating = false;
            Cursor = Cursor.Default;
            e.Pointer.Capture(null);
            if (Selection?.Primary is { } rotated && rotated.Orientation != _rotateGestureStart)
            {
                DocumentEdited?.Invoke(this, EventArgs.Empty);
            }

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
        _activeHandle = ResizeHandle.None;
        e.Pointer.Capture(null);

        // The model was updated live during the gesture; the release only decides
        // whether an undo step should be recorded.
        bool changed = wasResizing
            ? _candidateWidth != _resizeStartBounds.Width ||
              _candidateHeight != _resizeStartBounds.Height ||
              Selection?.Primary is { } r && (r.X != _resizeStartX || r.Y != _resizeStartY)
            : _dragDx != 0 || _dragDy != 0;
        if (changed)
        {
            DocumentEdited?.Invoke(this, EventArgs.Empty);
        }

        _dragItems.Clear();
        _dragDx = 0;
        _dragDy = 0;
        _snapX = null;
        _snapY = null;
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

        if (e.Key == Key.Escape && IsPlacing)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

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
