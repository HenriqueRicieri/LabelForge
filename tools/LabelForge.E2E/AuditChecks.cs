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
        PasteAtTheEdgeKeepsTheGroupsLayout(d, check);

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

    /// <summary>Pasting a copied group near the edge moves it back as one, rather than
    /// clamping each element on its own and stacking them against the edge.</summary>
    private static void PasteAtTheEdgeKeepsTheGroupsLayout(
        DesignerViewModel d, Action<string, object?, object?> check)
    {
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        for (int i = 0; i < 3; i++)
        {
            document.Elements.Add(new TextElement
            {
                X = 10 + 200 * i, Y = 20, Text = $"item {i}", FontHeightDots = 30, ZOrder = i,
            });
        }

        d.LoadDocument(document, null);
        Pump(300);
        d.Selection.SetMany(d.Document.Elements.ToList());
        Wait(d.CopyCommand.ExecuteAsync(null));
        Wait(d.PasteAt(d.Document.WidthDots - 50, 300));
        Pump(300);

        int[] pasted = [.. d.Document.Elements.Skip(3).Select(e => e.X).Order()];
        check("audit: paste near the edge keeps the copies' spacing",
            string.Join(",", pasted.Zip(pasted.Skip(1), (a, b) => b - a)), "200,200");
        check("audit: paste near the edge keeps every copy on the label",
            pasted.All(x => x >= 0 && x < d.Document.WidthDots), true);
    }

    private static void Wait(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.ElapsedMilliseconds < 5000)
        {
            Pump(20);
        }
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
