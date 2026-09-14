using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace LabelForge.App.Views;

public partial class DesignerView
{
    private bool _arrangingInspector;
    private bool _shortWorkspace;
    private double _inspectorWidth = 320;
    private bool _inspectorCollapsed;
    private bool _compactInspector;
    private bool _compactInspectorOpen;

    private void InitializeWorkspace()
    {
        SizeChanged += (_, _) =>
        {
            bool shortWorkspace = Bounds.Height < 520;
            if (shortWorkspace && !_shortWorkspace) ZplPanel.IsExpanded = false;
            _shortWorkspace = shortWorkspace;
            ZplText.Height = Math.Clamp(Bounds.Height * 0.22, 50, 140);
            UpdateWorkspaceLayout();
        };
        WorkspaceGrid.SizeChanged += (_, _) => UpdateWorkspaceLayout();
        WorkspaceGrid.ColumnDefinitions[3].PropertyChanged += (_, change) =>
        {
            if (change.Property == ColumnDefinition.WidthProperty && !_arrangingInspector &&
                !_compactInspector && !_inspectorCollapsed)
                _inspectorWidth = WorkspaceGrid.ColumnDefinitions[3].Width.Value;
        };
    }

    private void OnHideInspector(object? sender, RoutedEventArgs e)
    {
        if (_compactInspector) _compactInspectorOpen = false;
        else _inspectorCollapsed = true;
        UpdateWorkspaceLayout();
    }

    private void OnToggleInspector(object? sender, RoutedEventArgs e)
    {
        if (_compactInspector) _compactInspectorOpen = !_compactInspectorOpen;
        else _inspectorCollapsed = !_inspectorCollapsed;
        UpdateWorkspaceLayout();
    }

    private void RevealInspector()
    {
        if (_compactInspector) _compactInspectorOpen = true;
        else _inspectorCollapsed = false;
        UpdateWorkspaceLayout();
    }

    private void UpdateWorkspaceLayout()
    {
        double available = WorkspaceGrid.Bounds.Width;
        if (available <= 0) return;

        bool compact = available < 708; // 64 rail + 4 divider + 280 inspector + 360 canvas.
        if (compact != _compactInspector)
        {
            _compactInspector = compact;
            _compactInspectorOpen = false;
        }

        bool docked = !compact && !_inspectorCollapsed;
        var column = WorkspaceGrid.ColumnDefinitions[3];
        _arrangingInspector = true;
        try
        {
            column.MinWidth = docked ? 280 : 0;
            column.MaxWidth = docked ? Math.Min(420, available - 428) : double.PositiveInfinity;
            column.Width = new GridLength(docked ? Math.Clamp(_inspectorWidth, 280, column.MaxWidth) : 0);
            WorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(docked ? 4 : 0);
        }
        finally
        {
            _arrangingInspector = false;
        }
        InspectorSplitter.IsVisible = docked;

        Grid.SetColumn(InspectorPanel, compact ? 0 : 3);
        Grid.SetColumnSpan(InspectorPanel, compact ? 4 : 1);
        InspectorPanel.ZIndex = compact ? 10 : 0;
        InspectorPanel.Width = compact ? Math.Max(0, Math.Min(_inspectorWidth, available - 16)) : double.NaN;
        InspectorPanel.HorizontalAlignment = compact ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        InspectorPanel.IsVisible = compact ? _compactInspectorOpen : !_inspectorCollapsed;
    }
}
