namespace LabelForge.App.ViewModels;

/// <summary>The application shell: a designer tab and a raw ZPL viewer tab.</summary>
public partial class MainViewModel : ViewModelBase
{
    public DesignerViewModel Designer { get; } = new();

    public ViewerViewModel Viewer { get; } = new();
}
