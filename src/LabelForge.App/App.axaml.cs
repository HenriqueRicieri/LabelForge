using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Io;

namespace LabelForge.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = new MainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = main };

            // A path on the command line is how the shell opens a double-clicked label,
            // and how "Open with" and a file dropped on the executable arrive too. Done
            // after the window exists so the status line and the tab it selects have
            // somewhere to show.
            if (StartupFile.FromArguments(desktop.Args) is { } file)
            {
                main.OpenStartupFile(file);
            }

            // Ending the session removes its crash snapshot, which is the only thing that
            // distinguishes a shutdown from a crash on the next start.
            desktop.ShutdownRequested += (_, _) => main.Designer.ShutDown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}