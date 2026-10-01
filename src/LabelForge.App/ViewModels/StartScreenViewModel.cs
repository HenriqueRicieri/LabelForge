using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabelForge.Core.Starters;

namespace LabelForge.App.ViewModels;

/// <summary>A recent label as the start screen lists it: the name to read, the folder
/// to tell two of the same name apart, and the path to open.</summary>
public sealed record RecentLabel(string Path, string Name, string Folder);

/// <summary>What the start screen was asked to do.</summary>
public enum StartChoiceKind
{
    Blank,
    Open,
    Recent,
    Starter,
}

/// <summary>The answer the start screen closes with. The designer acts on it, so the
/// screen never decides on its own how a label is opened or created.</summary>
public sealed record StartChoice(StartChoiceKind Kind, string? Path = null, StarterLabel? Starter = null);

/// <summary>
/// The screen the app opens with when there is nothing else to show: a blank label,
/// Open, the recent labels and the starters.
///
/// It reuses the starter gallery's cards, rendered the same way, so a starter looks the
/// same here as in New from Sample. Whether it opens at all is a per-machine setting the
/// screen itself can turn off.
/// </summary>
public sealed class StartScreenViewModel : ViewModelBase
{
    private readonly DesignerViewModel _designer;

    public StartScreenViewModel(DesignerViewModel designer)
    {
        _designer = designer;
        Recent = [.. designer.RecentFiles.Select(path => new RecentLabel(
            path, System.IO.Path.GetFileName(path), System.IO.Path.GetDirectoryName(path) ?? string.Empty))];
        Gallery = new StarterGalleryViewModel(designer.Document.Dpmm);
    }

    public IReadOnlyList<RecentLabel> Recent { get; }

    public bool HasRecent => Recent.Count > 0;

    public StarterGalleryViewModel Gallery { get; }

    public bool ShowAtStartup
    {
        get => _designer.ShowStartScreen;
        set
        {
            _designer.ShowStartScreen = value;
            OnPropertyChanged();
        }
    }
}
