using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LabelForge.App.ViewModels;

namespace LabelForge.App.Views;

/// <summary>
/// The starter gallery, shown as a dialog and answering with the starter that was picked
/// or with nothing.
///
/// It hands back the starter rather than a document on purpose: the designer is what
/// creates labels, and a dialog that built one would be a second place that decides what
/// density and what file path a new label starts with.
/// </summary>
public partial class StarterGalleryWindow : Window
{
    public StarterGalleryWindow()
    {
        InitializeComponent();

        // Started here rather than in the constructor of the view model: rendering five
        // labels is engine work, and it belongs after the window is on screen.
        Opened += async (_, _) =>
        {
            if (DataContext is StarterGalleryViewModel gallery)
            {
                await gallery.LoadPreviewsAsync();
            }
        };

        Closed += (_, _) => (DataContext as StarterGalleryViewModel)?.ReleasePreviews();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnCreate(object? sender, RoutedEventArgs e) => Accept();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnCardDoubleTapped(object? sender, TappedEventArgs e) => Accept();

    private void Accept()
    {
        if (DataContext is StarterGalleryViewModel { Selected: { } card })
        {
            Close(card.Starter);
        }
    }
}
