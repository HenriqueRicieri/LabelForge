using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LabelForge.App.ViewModels;

public partial class DesignerViewModel
{
    [ObservableProperty] private bool _showElementOutlines;
    [ObservableProperty] private bool _showPrinterDotGrid = true;

    public string SetupSummary =>
        $"{Document.WidthMm:0.##} x {Document.HeightMm:0.##} mm  |  {SelectedDensity}"
        + (Document.IsContinuous ? "  |  Continuous" : "")
        + (IsMultiAcross ? $"  |  {Document.LabelsAcross} across" : "");

    public IReadOnlyList<string> WorkspaceMessages => new[]
    {
        PrinterWarning, ValidationWarning, PlacementWarning,
        UnknownFieldWarning, VariableWarning, StatusText,
    }.Where(message => !string.IsNullOrWhiteSpace(message)).Distinct().ToArray();

    public bool HasWorkspaceMessages => WorkspaceMessages.Count > 0;
    public string WorkspaceStatus => WorkspaceMessages.FirstOrDefault() ?? "Ready";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Document) or nameof(WidthMm) or nameof(HeightMm)
            or nameof(SelectedDensity) or nameof(IsContinuous) or nameof(LabelsAcross)
            or nameof(CanvasRevision))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(SetupSummary)));
        }

        if (e.PropertyName is nameof(PrinterWarning) or nameof(ValidationWarning)
            or nameof(PlacementWarning) or nameof(UnknownFieldWarning)
            or nameof(VariableWarning) or nameof(StatusText))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(WorkspaceMessages)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(WorkspaceStatus)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasWorkspaceMessages)));
        }
    }
}
