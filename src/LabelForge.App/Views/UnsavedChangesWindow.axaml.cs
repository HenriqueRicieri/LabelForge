using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LabelForge.App.Views;

/// <summary>What to do with unsaved changes before the label is replaced or closed.</summary>
public enum UnsavedChangesChoice
{
    Cancel,
    Save,
    DontSave,
}

/// <summary>
/// Asks whether to save the label before it goes. Closing the window any other way
/// (Escape, the title bar) is Cancel, because the one answer that must never happen by
/// accident is losing the work.
/// </summary>
public partial class UnsavedChangesWindow : Window
{
    public UnsavedChangesWindow()
        : this("Untitled")
    {
    }

    public UnsavedChangesWindow(string documentName)
    {
        InitializeComponent();
        this.FindControl<TextBlock>("QuestionText")!.Text = $"Save changes to {documentName}?";
        Opened += (_, _) => this.FindControl<Button>("SaveButton")?.Focus();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnSave(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Save);

    private void OnDontSave(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.DontSave);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Cancel);
}
