using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LabelForge.App.Services;

namespace LabelForge.App.Views;

/// <summary>
/// Help &gt; About: the version, where the source lives and the licenses. The license
/// texts open from the copies shipped beside the executable, which are the ones that
/// apply to this build, rather than from the web.
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
        : this(AppVersion.Display)
    {
    }

    public AboutWindow(string version)
    {
        InitializeComponent();
        this.FindControl<TextBlock>("VersionText")!.Text = $"Version {version}";
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnGitHub(object? sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(VelopackAppUpdates.RepositoryUrl));

    private void OnLicense(object? sender, RoutedEventArgs e) => OpenShipped("LICENSE");

    private void OnNotices(object? sender, RoutedEventArgs e) => OpenShipped("THIRD-PARTY-NOTICES.md");

    private async void OpenShipped(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(path))
        {
            await Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
