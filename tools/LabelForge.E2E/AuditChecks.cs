using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Threading;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Io;
using LabelForge.Core.Model;

/// <summary>
/// Designer behaviours fixed after the October 2026 audit, each graded against a label of
/// its own so one cannot disturb another. Runs as the "audit" mode and inside the full
/// designer pass.
/// </summary>
internal static class AuditChecks
{
    public static void Run(string scratch, Action<string, object?, object?> check)
    {
        string dir = Path.Combine(scratch, "audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var vm = new MainViewModel(
            new LabelForge.Core.Media.UserMediaStore(Path.Combine(dir, "media.json")),
            new LabelForge.Core.Fields.FieldCatalogStore(Path.Combine(dir, "fields.json")),
            new RecoveryStore(Path.Combine(dir, "recovery"), "audit"),
            () => new LabelForge.Core.Rendering.BinaryKitsRenderer(),
            new LabelForge.Core.Settings.UserSettingsStore(Path.Combine(dir, "settings.json")),
            recentFilesPath: Path.Combine(dir, "recent.json"));
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 760 };
        window.Show();
        Pump(400);
        DesignerViewModel d = vm.Designer;

        PreviewHonoursTheLabelsOwnMarkers(d, check);

        d.AcceptDiscard();
        window.Close();
        Pump(200);
        d.ShutDown();
    }

    /// <summary>A label whose system writes {{NAME}} gets its samples on the canvas too,
    /// not only in the generator: an EAN-13 holding a marker cannot be drawn literally.</summary>
    private static void PreviewHonoursTheLabelsOwnMarkers(
        DesignerViewModel d, Action<string, object?, object?> check)
    {
        var document = new LabelDocument
        {
            WidthMm = 80, HeightMm = 50, Dpmm = 8,
            Markers = new MarkerSyntax("{{", "}}", '@'),
        };
        document.Elements.Add(new BarcodeElement
        {
            X = 40, Y = 40, Data = "{{EAN}}", HeightDots = 100, Symbology = BarcodeSymbology.Ean13,
        });
        document.SampleValues["EAN"] = "789123456789";
        d.LoadDocument(document, null);
        Pump(800);

        check("audit: the canvas substitutes the label's own markers",
            d.WorkspaceMessages.Any(m => m.Contains("digits", StringComparison.OrdinalIgnoreCase)), false);
        check("audit: the canvas draws that barcode", d.Underlay is not null, true);
    }

    private static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(10);
        }
    }
}
