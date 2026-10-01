using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LabelForge.App.ViewModels;

namespace LabelForge.App.Views;

/// <summary>
/// The start screen, shown as a dialog that answers with a <see cref="StartChoice"/>, or
/// with nothing when it is closed. Like the starter gallery it hands back a choice rather
/// than acting on one: the designer is what opens and creates labels.
/// </summary>
public partial class StartWindow : Window
{
    public StartWindow()
    {
        InitializeComponent();

        // The starter pictures are engine work; they start once the window is up.
        Opened += async (_, _) =>
        {
            if (DataContext is StartScreenViewModel start)
            {
                await start.Gallery.LoadPreviewsAsync();
            }
        };

        Closed += (_, _) => (DataContext as StartScreenViewModel)?.Gallery.ReleasePreviews();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnBlank(object? sender, RoutedEventArgs e) => Close(new StartChoice(StartChoiceKind.Blank));

    private void OnOpen(object? sender, RoutedEventArgs e) => Close(new StartChoice(StartChoiceKind.Open));

    private void OnClose(object? sender, RoutedEventArgs e) => Close(null);

    private void OnRecent(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is RecentLabel recent)
        {
            Close(new StartChoice(StartChoiceKind.Recent, Path: recent.Path));
        }
    }

    private void OnStarter(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is StarterCardViewModel card)
        {
            Close(new StartChoice(StartChoiceKind.Starter, Starter: card.Starter));
        }
    }
}
