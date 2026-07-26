namespace LabelForge.App.ViewModels;

/// <summary>The application shell: a designer tab and a raw ZPL viewer tab.</summary>
public partial class MainViewModel : ViewModelBase
{
    /// <param name="userMediaStore">Where the user's own media presets live.</param>
    /// <param name="fieldCatalogStore">Where the imported field catalogs live.</param>
    /// <param name="recoveryStore">Where crash snapshots live.</param>
    /// <remarks>
    /// All three are per-machine state and all three are injectable for the same reason:
    /// a harness run must not write to what the person using the app has saved. Leaving
    /// one of them out is not a smaller shortcut, it is the same mistake in one place.
    /// </remarks>
    public MainViewModel(
        LabelForge.Core.Media.UserMediaStore? userMediaStore = null,
        LabelForge.Core.Fields.FieldCatalogStore? fieldCatalogStore = null,
        LabelForge.Core.Io.RecoveryStore? recoveryStore = null)
    {
        Designer = new DesignerViewModel(userMediaStore, fieldCatalogStore, recoveryStore);
    }

    public DesignerViewModel Designer { get; }

    public ViewerViewModel Viewer { get; } = new();
}
