using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LabelForge.App.Views;

/// <summary>
/// A short message with one or two answers. Closes with true for the primary button and
/// false for anything else, including Escape and the title bar, so the safe answer is
/// what an accidental close gives.
/// </summary>
public partial class MessageWindow : Window
{
    public MessageWindow()
        : this(string.Empty, string.Empty, "OK")
    {
    }

    /// <param name="secondary">The second button's caption, or null for a single OK.</param>
    public MessageWindow(string heading, string body, string primary, string? secondary = null)
    {
        InitializeComponent();
        this.FindControl<TextBlock>("HeadingText")!.Text = heading;
        var bodyText = this.FindControl<TextBlock>("BodyText")!;
        bodyText.Text = body;
        bodyText.IsVisible = !string.IsNullOrEmpty(body);
        this.FindControl<Button>("PrimaryButton")!.Content = primary;
        var second = this.FindControl<Button>("SecondaryButton")!;
        second.Content = secondary;
        second.IsVisible = secondary is not null;
        Opened += (_, _) => this.FindControl<Button>("PrimaryButton")?.Focus();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnPrimary(object? sender, RoutedEventArgs e) => Close(true);

    private void OnSecondary(object? sender, RoutedEventArgs e) => Close(false);
}
