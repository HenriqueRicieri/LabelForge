using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelForge.Core.Fields;

namespace LabelForge.App.ViewModels;

/// <summary>
/// One row of the field-catalog list. It carries its own remove command so the flyout's
/// item template binds to itself and needs no ancestor lookup, which is unreliable
/// inside popups; the media list is built the same way for the same reason.
/// </summary>
public sealed partial class FieldCatalogEntryViewModel : ObservableObject
{
    private readonly Action<FieldCatalog> _remove;

    public FieldCatalogEntryViewModel(FieldCatalog catalog, Action<FieldCatalog> remove)
    {
        Catalog = catalog;
        _remove = remove;
    }

    public FieldCatalog Catalog { get; }

    public string Display => Catalog.ToString();

    [RelayCommand]
    private void Remove() => _remove(Catalog);
}
