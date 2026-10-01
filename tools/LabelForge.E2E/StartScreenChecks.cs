using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Io;
using LabelForge.Core.Model;

/// <summary>
/// The start screen opens with the app when there is nothing else to show, lists the
/// recent labels and the starters, acts through the designer's own paths, and can be
/// turned off from itself. Its own window and scratch stores, like the unsaved-changes
/// checks, so the settings file it writes is never the user's.
/// </summary>
internal static class StartScreenChecks
{
    public static void Run(string scratch, Action<string, object?, object?> check)
    {
        string dir = Path.Combine(scratch, "start-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string recovery = Path.Combine(dir, "recovery");
        string settings = Path.Combine(dir, "settings.json");
        string recent = Path.Combine(dir, "recent.json");

        MainViewModel NewShell(string session) => new(
            new LabelForge.Core.Media.UserMediaStore(Path.Combine(dir, "media.json")),
            new LabelForge.Core.Fields.FieldCatalogStore(Path.Combine(dir, "fields.json")),
            new RecoveryStore(recovery, session),
            () => new LabelForge.Core.Rendering.BinaryKitsRenderer(),
            new LabelForge.Core.Settings.UserSettingsStore(settings),
            recentFilesPath: recent);

        // Two labels in the recent list, written the way a save would.
        var seed = NewShell("seed");
        string[] labels = ["first-start.lfl", "second-start.lfl"];
        foreach (string name in labels)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, seed.Designer.SerializeDocument());
            seed.Designer.RegisterRecentFile(path);
        }

        seed.Designer.ShutDown();

        var vm = NewShell("start");
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 760 };
        window.Show();
        Pump(300);

        Task<bool> shown = window.ShowStartScreenAtLaunchAsync();
        Pump(600);
        Window? start = Start(window);
        check("start: opens with the app by default", start is not null, true);
        check("start: lists the recent labels, newest first",
            string.Join(",", Items(start, "RecentList").Select(i => (i as RecentLabel)?.Name)),
            "second-start.lfl,first-start.lfl");
        check("start: offers every starter", Items(start, "StarterList").Count, LabelForge.Core.Starters.StarterCatalog.All.Count);

        // A starter is created through the designer, the same as New from Sample.
        Button? tour = start?.GetLogicalDescendants().OfType<Button>()
            .FirstOrDefault(b => b.DataContext is StarterCardViewModel card && card.Starter == LabelForge.Core.Starters.StarterCatalog.Tour);
        tour?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(600);
        check("start: a starter closes the screen", Start(window) is null && shown.IsCompleted, true);
        check("start: a starter becomes the label", vm.Designer.Document.Elements.Count,
            LabelForge.Core.Starters.StarterCatalog.Tour.Create(8).Elements.Count);
        check("start: the new label is unmodified", window.Title, "Untitled - LabelForge");

        // A recent label opens through the same path as Open Recent.
        _ = window.ShowStartScreenAtLaunchAsync();
        Pump(600);
        Button? second = Start(window)?.GetLogicalDescendants().OfType<Button>()
            .FirstOrDefault(b => b.DataContext is RecentLabel r && r.Name == "second-start.lfl");
        second?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(600);
        check("start: a recent label opens", vm.Designer.CurrentFilePath is { } opened && opened.EndsWith("second-start.lfl"), true);

        // Turned off from the screen itself, it stays off and is still reachable.
        _ = window.ShowStartScreenAtLaunchAsync();
        Pump(600);
        var box = Start(window)?.GetLogicalDescendants().OfType<CheckBox>().FirstOrDefault(c => c.Name == "ShowAtStartupInput");
        box?.SetCurrentValue(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty, false);
        Pump(100);
        Start(window)?.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "CloseStartButton")
            ?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(300);
        check("start: Close leaves the label alone",
            vm.Designer.CurrentFilePath is { } still && still.EndsWith("second-start.lfl"), true);
        check("start: turning it off is saved for this machine",
            new LabelForge.Core.Settings.UserSettingsStore(settings).Load().ShowStartScreen, false);
        Task<bool> off = window.ShowStartScreenAtLaunchAsync();
        Pump(300);
        check("start: turned off, it stays closed at launch", off.IsCompleted && !off.Result && Start(window) is null, true);
        check("start: File offers it anyway",
            window.GetLogicalDescendants().OfType<MenuItem>().Any(m => m.Name == "StartScreenMenu"), true);
        vm.Designer.ShowStartScreen = true;

        // Work waiting to be recovered is the first thing to decide, so the screen waits.
        vm.Designer.NewDocumentCommand.Execute(null);
        vm.Designer.Document.Elements.Add(new TextElement { X = 20, Y = 20, Text = "crashed", FontHeightDots = 30 });
        vm.Designer.NotifyDocumentEdited();
        Pump(500);
        string snapshot = Directory.GetFiles(recovery, "start.recovery.json").Single();
        File.Copy(snapshot, Path.Combine(recovery, "crashed.recovery.json"));
        var afterCrash = NewShell("after-crash");
        var crashWindow = new MainWindow { DataContext = afterCrash, Width = 1200, Height = 760 };
        crashWindow.Show();
        Pump(300);
        Task<bool> withOffer = crashWindow.ShowStartScreenAtLaunchAsync();
        Pump(300);
        check("start: stays closed while recovered work is on offer",
            afterCrash.Designer.HasRecoveryOffer && withOffer.IsCompleted && !withOffer.Result && Start(crashWindow) is null, true);

        afterCrash.Designer.DismissRecoveryCommand.Execute(null);
        afterCrash.Designer.ShutDown();
        vm.Designer.AcceptDiscard();
        vm.Designer.ShutDown();
        crashWindow.Close();
        window.Close();
        Pump(200);
    }

    private static Window? Start(Window owner) =>
        owner.OwnedWindows.FirstOrDefault(w => w is StartWindow && w.IsVisible);

    private static IReadOnlyList<object?> Items(Window? start, string name) =>
        start?.GetLogicalDescendants().OfType<ItemsControl>().FirstOrDefault(i => i.Name == name)?.Items.Cast<object?>().ToArray()
        ?? [];

    private static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(20);
        }
    }
}
