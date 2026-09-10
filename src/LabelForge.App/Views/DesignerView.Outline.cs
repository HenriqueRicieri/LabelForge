using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelForge.App.ViewModels;
using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.App.Views;

public partial class DesignerView
{
    private Element? _outlineSource;
    private Element? _outlineTarget;
    private LabelDocument? _outlineDocument;
    private IPointer? _outlinePointer;
    private Point _outlinePress;
    private Point _outlinePosition;
    private bool _outlineDragging;
    private bool _outlineInFront;
    private DispatcherTimer? _outlineScroll;

    private void InitializeOutlineDrag()
    {
        ElementsList.AddHandler(PointerPressedEvent, OnOutlinePressed, RoutingStrategies.Tunnel);
        ElementsList.AddHandler(PointerMovedEvent, OnOutlineMoved, RoutingStrategies.Tunnel);
        ElementsList.AddHandler(PointerReleasedEvent, OnOutlineReleased, RoutingStrategies.Tunnel);
        ElementsList.PointerCaptureLost += (_, e) =>
        {
            if (ReferenceEquals(e.Source, ElementsList)) CancelOutlineDrag();
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || _outlineSource is null) return;
            CancelOutlineDrag();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => CancelOutlineDrag();
        DetachedFromVisualTree += (_, _) => CancelOutlineDrag();
        InspectorTabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Source, InspectorTabs)) CancelOutlineDrag();
        };
    }

    private void OnOutlinePressed(object? sender, PointerPressedEventArgs e)
    {
        CancelOutlineDrag();
        if (!e.GetCurrentPoint(ElementsList).Properties.IsLeftButtonPressed || ViewModel is not { } vm ||
            e.Source is not Visual visual) return;
        var ancestors = visual.GetSelfAndVisualAncestors().ToList();
        if (ancestors.OfType<CheckBox>().Any()) return;
        var row = ancestors.OfType<ListBoxItem>().FirstOrDefault();
        if (row?.DataContext is not ElementOutlineViewModel item || Groups.IsHeld(vm.Document, item.Element)) return;
        _outlineSource = item.Element;
        _outlineDocument = vm.Document;
        _outlinePointer = e.Pointer;
        _outlinePress = e.GetPosition(ElementsList);
    }

    private void OnOutlineMoved(object? sender, PointerEventArgs e)
    {
        if (_outlineSource is null) return;
        if (!ReferenceEquals(ViewModel?.Document, _outlineDocument) ||
            !e.GetCurrentPoint(ElementsList).Properties.IsLeftButtonPressed)
        {
            CancelOutlineDrag();
            return;
        }
        _outlinePosition = e.GetPosition(ElementsList);
        if (!_outlineDragging)
        {
            double dx = _outlinePosition.X - _outlinePress.X, dy = _outlinePosition.Y - _outlinePress.Y;
            if (dx * dx + dy * dy < 16) return;
            _outlineDragging = true;
            e.Pointer.Capture(ElementsList);
            _outlineScroll = new DispatcherTimer(TimeSpan.FromMilliseconds(50),
                DispatcherPriority.Input, (_, _) => ScrollOutline());
            _outlineScroll.Start();
        }
        ScrollOutline();
        e.Handled = true;
    }

    private void ScrollOutline()
    {
        if (!_outlineDragging || !ReferenceEquals(ViewModel?.Document, _outlineDocument) || !ElementsList.IsEffectivelyVisible)
        {
            CancelOutlineDrag();
            return;
        }
        var viewport = new Rect(ElementsList.Bounds.Size);
        if (!viewport.Contains(_outlinePosition)) return;
        double delta = _outlinePosition.Y < 24 ? -16 : _outlinePosition.Y > viewport.Height - 24 ? 16 : 0;
        var scroll = ElementsList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (delta != 0 && scroll is not null)
        {
            scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + delta, 0,
                Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
            ElementsList.UpdateLayout();
        }
        UpdateOutlineTarget();
    }

    private void UpdateOutlineTarget()
    {
        _outlineTarget = null;
        OutlineDropMarker.IsVisible = false;
        if (!_outlineDragging || _outlineSource is null || ViewModel is not { } vm ||
            !new Rect(ElementsList.Bounds.Size).Contains(_outlinePosition)) return;
        var rows = ElementsList.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(r => r.IsVisible && r.DataContext is ElementOutlineViewModel)
            .Select(r => (Row: r, At: r.TranslatePoint(default, ElementsList)))
            .Where(r => r.At is not null).OrderBy(r => r.At!.Value.Y).ToList();
        if (rows.Count == 0) return;
        var hit = rows.FirstOrDefault(r => _outlinePosition.Y < r.At!.Value.Y + r.Row.Bounds.Height);
        if (hit.Row is null) hit = rows[^1];
        var target = ((ElementOutlineViewModel)hit.Row.DataContext!).Element;
        _outlineInFront = _outlinePosition.Y < hit.At!.Value.Y + hit.Row.Bounds.Height / 2;
        if (!ZOrder.CanMove(vm.Document, _outlineSource, target, _outlineInFront)) return;
        var members = Groups.Members(vm.Document, target);
        var groupRows = rows.Where(r => members.Contains(((ElementOutlineViewModel)r.Row.DataContext!).Element)).ToList();
        double line = _outlineInFront ? groupRows.Min(r => r.At!.Value.Y)
            : groupRows.Max(r => r.At!.Value.Y + r.Row.Bounds.Height);
        _outlineTarget = target;
        OutlineDropMarker.Margin = new Thickness(0, Math.Clamp(line - 1, 0, Math.Max(0, ElementsList.Bounds.Height - 2)), 0, 0);
        OutlineDropMarker.IsVisible = true;
    }

    private void OnOutlineReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (!_outlineDragging)
        {
            CancelOutlineDrag();
            return;
        }
        _outlinePosition = e.GetPosition(ElementsList);
        UpdateOutlineTarget();
        var source = _outlineSource;
        var target = _outlineTarget;
        bool inFront = _outlineInFront;
        bool sameDocument = ReferenceEquals(ViewModel?.Document, _outlineDocument);
        CancelOutlineDrag();
        if (sameDocument && source is not null && target is not null)
            ViewModel?.MoveOutlineElement(source, target, inFront);
        e.Handled = true;
    }

    private void CancelOutlineDrag()
    {
        var pointer = _outlinePointer;
        _outlinePointer = null;
        _outlineSource = _outlineTarget = null;
        _outlineDocument = null;
        _outlineDragging = false;
        _outlineScroll?.Stop();
        _outlineScroll = null;
        OutlineDropMarker.IsVisible = false;
        if (pointer?.Captured == ElementsList) pointer.Capture(null);
    }
}
