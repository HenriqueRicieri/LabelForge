using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LabelForge.Core.Io;

namespace LabelForge.App.ViewModels;

/// <summary>The application shell: a designer tab and a raw ZPL viewer tab.</summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>Tab indices, in the order the shell declares them.</summary>
    public const int DesignerTab = 0;

    public const int ViewerTab = 1;

    /// <param name="userMediaStore">Where the user's own media presets live.</param>
    /// <param name="fieldCatalogStore">Where the imported field catalogs live.</param>
    /// <param name="recoveryStore">Where crash snapshots live.</param>
    /// <param name="comparisonRenderer">What the viewer's compare mode measures against.</param>
    /// <remarks>
    /// All four are injectable for one reason: a harness run must not touch what belongs to
    /// the person using the app. Three of them are per-machine files it would otherwise
    /// overwrite; the fourth would otherwise send their label to a third party over the
    /// internet. Leaving one out is not a smaller shortcut, it is the same mistake in one
    /// place, which F2 already learned the hard way with the field catalog.
    /// </remarks>
    public MainViewModel(
        LabelForge.Core.Media.UserMediaStore? userMediaStore = null,
        LabelForge.Core.Fields.FieldCatalogStore? fieldCatalogStore = null,
        LabelForge.Core.Io.RecoveryStore? recoveryStore = null,
        Func<LabelForge.Core.Rendering.IZplRenderer>? comparisonRenderer = null)
    {
        Designer = new DesignerViewModel(userMediaStore, fieldCatalogStore, recoveryStore);
        Viewer = new ViewerViewModel(comparisonRenderer);
    }

    public DesignerViewModel Designer { get; }

    public ViewerViewModel Viewer { get; }

    /// <summary>Which tab is showing. A property rather than a view concern because
    /// opening a file decides where it belongs, and only the shell knows both halves.</summary>
    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    /// <summary>
    /// Opens a file the app was started with: a double-clicked .lfl, a file dropped on the
    /// executable, or "Open with".
    ///
    /// The kind decides the tab, because the two are genuinely different acts. A label is
    /// the thing the designer edits. A ZPL file goes to the viewer rather than through the
    /// designer's ZPL import, which has choices attached (which block, which density) that
    /// nobody asked to make by double-clicking something.
    ///
    /// A crash-recovery offer already on screen is deliberately left alone. It is an offer,
    /// so it changes nothing until it is taken, and discarding somebody's unsaved work
    /// because they opened an unrelated file would be the worse mistake by far.
    /// </summary>
    /// <returns>True when the file was opened.</returns>
    public bool OpenStartupFile(StartupFile file)
    {
        switch (file.Kind)
        {
            case StartupFileKind.Label:
                SelectedTab = DesignerTab;
                return Designer.OpenLabelFile(file.Path);

            case StartupFileKind.Zpl:
                SelectedTab = ViewerTab;
                return Viewer.OpenFile(file.Path) is not null;

            default:
                // Named, not ignored. A file handed to the app and silently dropped looks
                // exactly like the app failing to start.
                SelectedTab = DesignerTab;
                Designer.StatusText =
                    $"LabelForge does not open {System.IO.Path.GetExtension(file.Path)} files. "
                    + $"It opens {StartupFile.LabelExtension} labels and ZPL files.";
                return false;
        }
    }
}
