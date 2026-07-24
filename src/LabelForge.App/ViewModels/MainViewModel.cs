namespace LabelForge.App.ViewModels;

/// <summary>The application shell: a designer tab and a raw ZPL viewer tab.</summary>
public partial class MainViewModel : ViewModelBase
{
    public MainViewModel(LabelForge.Core.Media.UserMediaStore? userMediaStore = null)
    {
        Designer = new DesignerViewModel(userMediaStore);
    }

    /// <param name="userMediaStore">Where the user's own media presets live. Passed in
    /// so a harness can exercise them without writing to the real per-user file.</param>
    public DesignerViewModel Designer { get; }

    public ViewerViewModel Viewer { get; } = new();
}
