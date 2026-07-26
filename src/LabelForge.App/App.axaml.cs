using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;

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

            // Ending the session removes its crash snapshot, which is the only thing that
            // distinguishes a shutdown from a crash on the next start.
            desktop.ShutdownRequested += (_, _) => main.Designer.ShutDown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}