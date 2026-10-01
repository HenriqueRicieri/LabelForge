using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelForge.App.Services;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Io;
using LabelForge.Core.Model;

/// <summary>
/// Help &gt; Check for Updates and About, against a stand-in update source: the real one
/// is the public release feed, and a test run must not call a third party. Covers what
/// each answer says, that an update never restarts over unsaved work without asking, and
/// that a restart leaves no recovery snapshot behind.
/// </summary>
internal static class UpdateChecks
{
    private sealed class FakeUpdates : IAppUpdates
    {
        public bool IsInstalled { get; set; }
        public string CurrentVersion => "0.4.0";
        public AppUpdate? Next { get; set; }
        public Exception? Failure { get; set; }
        public int Checks;
        public int Downloads;
        public int Applies;

        public Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken = default)
        {
            Checks++;
            return Failure is { } failure ? Task.FromException<AppUpdate?>(failure) : Task.FromResult(Next);
        }

        public Task DownloadAsync(AppUpdate update, Action<int>? progress, CancellationToken cancellationToken = default)
        {
            Downloads++;
            progress?.Invoke(100);
            return Task.CompletedTask;
        }

        public void ApplyAndRestart(AppUpdate update) => Applies++;
    }

    public static void Run(string scratch, Action<string, object?, object?> check)
    {
        string dir = Path.Combine(scratch, "updates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string recovery = Path.Combine(dir, "recovery");
        var fake = new FakeUpdates();
        var vm = new MainViewModel(
            new LabelForge.Core.Media.UserMediaStore(Path.Combine(dir, "media.json")),
            new LabelForge.Core.Fields.FieldCatalogStore(Path.Combine(dir, "fields.json")),
            new RecoveryStore(recovery, "updates"),
            () => new LabelForge.Core.Rendering.BinaryKitsRenderer(),
            new LabelForge.Core.Settings.UserSettingsStore(Path.Combine(dir, "settings.json")),
            recentFilesPath: Path.Combine(dir, "recent.json"),
            updates: fake);
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 760 };
        window.Show();
        Pump(300);
        var view = window.GetVisualDescendants().OfType<DesignerView>().Single();
        DesignerViewModel d = vm.Designer;

        check("updates: Help offers Check for Updates and About",
            window.GetLogicalDescendants().OfType<MenuItem>()
                .Where(m => m.Name is "CheckUpdatesMenu" or "AboutMenu").Select(m => m.Name).Distinct().Count(), 2);

        Task run = view.CheckForUpdatesAsync(window);
        Pump(300);
        check("updates: a build folder says updates come with the installed app",
            Heading(window), "Updates come with the installed app");
        check("updates: a build folder never asks the feed", fake.Checks, 0);
        Answer(window, primary: true);
        check("updates: the message closes", run.IsCompleted, true);

        fake.IsInstalled = true;
        run = view.CheckForUpdatesAsync(window);
        Pump(300);
        check("updates: no newer release says it is up to date", Heading(window), "LabelForge is up to date");
        Answer(window, primary: true);

        fake.Failure = new IOException("network unreachable");
        run = view.CheckForUpdatesAsync(window);
        Pump(300);
        check("updates: a failed check says why", Heading(window), "Could not check for updates");
        Answer(window, primary: true);
        fake.Failure = null;

        fake.Next = new AppUpdate("0.5.0", new object());
        run = view.CheckForUpdatesAsync(window);
        Pump(300);
        check("updates: a newer release is offered", Heading(window), "LabelForge 0.5.0 is available");
        Answer(window, primary: false);
        check("updates: Later downloads nothing", fake.Downloads, 0);

        // Unsaved work: Update and restart asks first, and Cancel stops everything.
        d.NewDocumentCommand.Execute(null);
        d.Document.Elements.Add(new TextElement { X = 20, Y = 20, Text = "unsaved", FontHeightDots = 30 });
        d.NotifyDocumentEdited();
        Pump(400);
        run = view.CheckForUpdatesAsync(window);
        Pump(300);
        Answer(window, primary: true);
        Window? unsaved = window.OwnedWindows.FirstOrDefault(w => w is UnsavedChangesWindow && w.IsVisible);
        check("updates: unsaved work is asked about before the restart", unsaved is not null, true);
        unsaved?.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "CancelButton")
            ?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(300);
        check("updates: Cancel keeps the work and does not update",
            fake.Downloads == 0 && fake.Applies == 0 && d.Document.Elements.Count == 1, true);

        // Clean label: it downloads, ends the session and restarts, leaving nothing to recover.
        d.NewDocumentCommand.Execute(null);
        Pump(400);
        run = view.CheckForUpdatesAsync(window);
        Pump(300);
        Answer(window, primary: true);
        Pump(300);
        check("updates: Update and restart downloads and applies once", $"{fake.Downloads},{fake.Applies}", "1,1");
        check("updates: the restart leaves no recovery snapshot",
            Directory.Exists(recovery) ? Directory.GetFiles(recovery, "*.recovery.json").Length : 0, 0);

        // About shows the version the update source reports.
        var about = new AboutWindow(fake.CurrentVersion);
        about.Show(window);
        Pump(200);
        check("updates: About shows the version",
            about.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "VersionText")?.Text, "Version 0.4.0");
        check("updates: About links the source and both licenses",
            about.GetLogicalDescendants().OfType<Button>().Count(b => b.Name is "GitHubButton" or "LicenseButton" or "NoticesButton"), 3);
        about.Close();
        window.Close();
        Pump(200);
    }

    private static string? Heading(Window owner) =>
        owner.OwnedWindows.OfType<MessageWindow>().FirstOrDefault(w => w.IsVisible)?
            .GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "HeadingText")?.Text;

    private static void Answer(Window owner, bool primary)
    {
        owner.OwnedWindows.OfType<MessageWindow>().FirstOrDefault(w => w.IsVisible)?
            .GetLogicalDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Name == (primary ? "PrimaryButton" : "SecondaryButton"))
            ?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(300);
    }

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
