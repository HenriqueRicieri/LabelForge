using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LabelForge.App.ViewModels;

public partial class DesignerViewModel
{
    [ObservableProperty] private bool _showCanvasPerformance;
    [ObservableProperty] private bool _showElementOutlines;
    [ObservableProperty] private bool _showPrinterDotGrid = true;

    public string SetupSummary =>
        $"{WidthCm:0.###} x {HeightCm:0.###} cm  |  {SelectedDensity}"
        + (Document.IsContinuous ? "  |  Continuous" : "")
        + (IsMultiAcross ? $"  |  {Document.LabelsAcross} across" : "");

    /// <summary>What the bar under the canvas reports, first one shown. A message a
    /// command just held (a failed save, an export) comes before the standing warnings,
    /// which would otherwise hide it for as long as they stand; a render diagnosis or
    /// a released message goes last, as before.</summary>
    public IReadOnlyList<string> WorkspaceMessages => (_statusHeld
        ? new[]
        {
            StatusText, PrinterWarning, ValidationWarning, PlacementWarning,
            UnknownFieldWarning, VariableWarning,
        }
        : new[]
        {
            PrinterWarning, ValidationWarning, PlacementWarning,
            UnknownFieldWarning, VariableWarning, StatusText,
        }).Where(message => !string.IsNullOrWhiteSpace(message)).Distinct().ToArray();

    public bool HasWorkspaceMessages => WorkspaceMessages.Count > 0;
    public string WorkspaceStatus => WorkspaceMessages.FirstOrDefault() ?? "Ready";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        string? centimeters = e.PropertyName switch
        {
            nameof(WidthMm) => nameof(WidthCm),
            nameof(HeightMm) => nameof(HeightCm),
            nameof(ContinuousMarginMm) => nameof(ContinuousMarginCm),
            nameof(CornerRadiusMm) => nameof(CornerRadiusCm),
            nameof(AcrossGapMm) => nameof(AcrossGapCm),
            nameof(NewMediaRadiusMm) => nameof(NewMediaRadiusCm),
            nameof(GridPitchMm) => nameof(GridPitchCm),
            _ => null,
        };
        if (centimeters is not null)
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(centimeters));
        }

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
