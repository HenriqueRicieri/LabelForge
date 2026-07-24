using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelForge.Core.Media;

namespace LabelForge.App.ViewModels;

/// <summary>
/// One row of the "my media" list. It carries its own remove command so the flyout's
/// item template binds to itself and needs no ancestor lookup, which is unreliable
/// inside popups (the recent-files menu is built in code for the same reason).
/// </summary>
public sealed partial class UserMediaEntryViewModel : ObservableObject
{
    private readonly Action<StockMedia> _remove;

    public UserMediaEntryViewModel(StockMedia media, Action<StockMedia> remove)
    {
        Media = media;
        _remove = remove;
    }

    public StockMedia Media { get; }

    public string Display => Media.ToString();

    [RelayCommand]
    private void Remove() => _remove(Media);
}
