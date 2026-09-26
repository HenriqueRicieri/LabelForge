using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using LabelForge.App.Services;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Io;

namespace LabelForge.NativeQA;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            NativeQaApp.IsSelfCheck = args.Contains("--self-check");
            var builder = AppBuilder.Configure<NativeQaApp>().WithInterFont();
            if (!NativeQaApp.IsSelfCheck) return builder.UsePlatformDetect().StartWithClassicDesktopLifetime([]);
            return builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .StartWithClassicDesktopLifetime([], lifetime =>
                    lifetime.Startup += (_, _) => Dispatcher.UIThread.Post(() =>
                        lifetime.Shutdown(((NativeQaApp)Application.Current!).SelfCheck())));
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "native-qa-error.txt"), error.ToString());
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}

internal sealed class NativeQaApp : LabelForge.App.App
{
    internal static bool IsSelfCheck { get; set; }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _session = Path.Combine(AppContext.BaseDirectory, "native-qa", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
    private MainWindow _window = null!;
    private MainViewModel _main = null!;
    private TextBlock _reading = null!;
    private int _record;
    private bool _closed;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        Directory.CreateDirectory(_session);
        _window = new MainWindow();
        _main = new MainViewModel(
            new LabelForge.Core.Media.UserMediaStore(Path.Combine(_session, "media.json")),
            new LabelForge.Core.Fields.FieldCatalogStore(Path.Combine(_session, "catalogs.json")),
            new RecoveryStore(Path.Combine(_session, "recovery")),
            () => new LabelForge.Core.Rendering.BinaryKitsRenderer(),
            new LabelForge.Core.Settings.UserSettingsStore(Path.Combine(_session, "settings.json")),
            new ElementClipboard(() => _window.Clipboard),
            Path.Combine(_session, "recent.json"));
        _window.DataContext = _main;
        desktop.MainWindow = _window;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        desktop.ShutdownRequested += (_, _) => _main.Designer.ShutDown();

        _reading = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var controls = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        controls.Children.Add(_reading);
        AddButton("Light", () => { RequestedThemeVariant = ThemeVariant.Light; Record("theme"); });
        AddButton("Dark", () => { RequestedThemeVariant = ThemeVariant.Dark; Record("theme"); });
        AddButton("Regular", () => Resize(1200, 780));
        AddButton("Compact", () => Resize(720, 630));
        AddButton("Record state", () => Record("manual"));
        var panel = new Window { Title = "LabelForge native QA measurements", Width = 340, Height = 350, Content = controls };
        _window.Opened += (_, _) => { panel.Show(); Record("opened"); };
        _window.Closed += (_, _) => _closed = true;
        _window.ScalingChanged += (_, _) => Dispatcher.UIThread.Post(() => Record("scaling"));
        _window.SizeChanged += (_, _) => Dispatcher.UIThread.Post(() => Record("resize"));
        RequestedThemeVariant = ThemeVariant.Light;

        void AddButton(string label, Action action)
        {
            var button = new Button { Content = label, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            button.Click += (_, _) => action();
            controls.Children.Add(button);
        }
    }

    private void Resize(double width, double height)
    {
        _window.WindowState = WindowState.Normal;
        _window.Width = width;
        _window.Height = height;
    }

    private void Record(string reason)
    {
        if (_closed) return;
        var screen = _window.Screens.ScreenFromWindow(_window);
        string name = (++_record).ToString("D4");
        string model = LabelDocumentJson.Serialize(_main.Designer.Document);
        File.WriteAllText(Path.Combine(_session, name + ".lfl"), model);
        File.WriteAllText(Path.Combine(_session, name + ".zpl"), _main.Designer.GeneratedZpl);
        var state = new
        {
            utc = DateTime.UtcNow, reason,
            platform = IsSelfCheck ? "headless-self-check" : "native",
            source = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            os = Environment.OSVersion.VersionString,
            renderScaling = _window.RenderScaling,
            screenScaling = screen?.Scaling,
            screenBounds = screen is null ? null : screen.Bounds.ToString(),
            clientWidth = _window.ClientSize.Width, clientHeight = _window.ClientSize.Height,
            theme = ActualThemeVariant.ToString(),
            elements = _main.Designer.Document.Elements.Count,
            selected = _main.Designer.Selection.Count,
            record = name
        };
        File.WriteAllText(Path.Combine(_session, name + ".json"), JsonSerializer.Serialize(state, Json));
        File.WriteAllText(Path.Combine(_session, "latest.json"), JsonSerializer.Serialize(state, Json));
        _reading.Text = $"RenderScaling: {_window.RenderScaling:F2}\nScreen scaling: {screen?.Scaling:F2}\nClient: {_window.ClientSize.Width:F0} x {_window.ClientSize.Height:F0} DIPs\nRecord: {name}";
    }

    internal int SelfCheck()
    {
        int checks = 0, failed = 0;
        string userRecent = UserDataPaths.FilePath("recent-files.json");
        string? before = Hash(userRecent);
        string store = Path.Combine(_session, "recent.json");
        string fixture = Path.Combine(_session, "synthetic.lfl");
        File.WriteAllText(fixture, LabelDocumentJson.Serialize(_main.Designer.Document));
        Check("production window initialized", _window.FindControl<TabControl>("MainTabs") is not null);
        Check("production theme resources initialized", Resources.TryGetResource("AppPanelBackground", ThemeVariant.Light, out var brush) && brush is not null);
        Check("recent list starts in scratch storage", _main.Designer.RecentFiles.Count == 0);
        Check("synthetic label opens", _main.Designer.OpenLabelFile(fixture));
        Check("opening label writes scratch recent file", File.Exists(store));
        Check("recent menu contains opened fixture", _main.Designer.RecentFiles.SequenceEqual([fixture]));
        _main.Designer.RegisterRecentFile(fixture.ToUpperInvariant());
        Check("case-insensitive recent deduplication", _main.Designer.RecentFiles.Count == 1);
        string missing = Path.Combine(_session, "missing.lfl");
        _main.Designer.RegisterRecentFile(missing);
        Check("missing label reports failure", !_main.Designer.OpenLabelFile(missing));
        Check("failed open removes only missing recent entry", _main.Designer.RecentFiles.Count == 1);
        for (int i = 0; i < 12; i++) _main.Designer.RegisterRecentFile(Path.Combine(_session, $"entry-{i}.lfl"));
        Check("recent entries remain capped at ten", _main.Designer.RecentFiles.Count == 10);
        Check("scratch recent entries persist", RecentFilesStore.Load(store).SequenceEqual(_main.Designer.RecentFiles));
        Check("separate recent store is untouched", RecentFilesStore.Load(Path.Combine(_session, "other.json")).Count == 0);
        Record("self-check");
        Check("measurement record produced", File.Exists(Path.Combine(_session, "latest.json")));
        Check("self-check record is explicitly headless", File.ReadAllText(Path.Combine(_session, "latest.json")).Contains("headless-self-check"));
        Check("user recent file is unchanged", Hash(userRecent) == before);
        _main.Designer.ShutDown();
        Console.WriteLine($"{checks} native QA bootstrap/storage checks, {failed} failed. These are headless checks, not native DPI evidence.");
        return failed == 0 ? 0 : 1;

        void Check(string name, bool passed)
        {
            checks++;
            if (!passed) failed++;
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
        }
        static string? Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
    }
}
