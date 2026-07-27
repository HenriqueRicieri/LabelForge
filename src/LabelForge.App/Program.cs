using Avalonia;
using System;
using LabelForge.App.Services;
using Velopack;

namespace LabelForge.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack installer hooks (install/update/uninstall) must run first;
        // on a normal launch none of these fire. UpdateManager integration comes
        // once the app has a public distribution feed.
        //
        // The hooks own the .lfl shell association, because that is per-machine state the
        // app itself has no business writing on an ordinary launch. Registration is
        // repeated after an update rather than only after an install: it costs nothing and
        // it repairs a registration something else removed, while a choice the user made
        // themselves still wins over it (see FileAssociation).
        //
        // Each hook has 30 seconds before Velopack terminates it, which a handful of
        // registry writes is never close to, and each returns false rather than throwing,
        // because a file association is not worth failing an install over.
        //
        // The hooks themselves exist only on Windows, so the whole chain is guarded rather
        // than each callback: on another platform there is no installer to hook into.
        VelopackApp velopack = VelopackApp.Build();
        if (OperatingSystem.IsWindows())
        {
            velopack = velopack
                .OnAfterInstallFastCallback(_ => RegisterFileTypes())
                .OnAfterUpdateFastCallback(_ => RegisterFileTypes())
                .OnBeforeUninstallFastCallback(_ => UnregisterFileTypes());
        }

        velopack.Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Points .lfl at this copy of the app. Velopack keeps the installed
    /// executable at one path across updates, so the running process is the path to
    /// register.</summary>
    private static void RegisterFileTypes()
    {
        if (OperatingSystem.IsWindows() && Environment.ProcessPath is { } exe)
        {
            new FileAssociation().Register(exe);
        }
    }

    private static void UnregisterFileTypes()
    {
        if (OperatingSystem.IsWindows())
        {
            new FileAssociation().Unregister();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
