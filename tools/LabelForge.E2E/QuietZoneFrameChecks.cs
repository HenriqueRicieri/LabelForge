using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Model;

internal static class QuietZoneFrameChecks
{
    public static void Run(MainWindow window, DesignerViewModel designer, Action<string, bool> check)
    {
        var code = new BarcodeElement
        {
            X = 200, Y = 120, Data = "LF-000123", HeightDots = 80, ModuleWidthDots = 2,
            PrintInterpretationLine = false, Name = "Product barcode",
        };
        var frame = new BoxElement
        {
            X = 20, Y = 20, WidthDots = 760, HeightDots = 440, ThicknessDots = 4, Name = "Perimeter frame",
        };
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        document.Elements.Add(code);
        document.Elements.Add(frame);
        designer.LoadDocument(document, path: null);
        designer.Selection.Set(code);
        Pump(700);
        check("Hollow frame leaves the barcode warning clear", designer.ValidationWarning.Length == 0);
        check("Hollow frame leaves diagnostics clear of crowding", !designer.WorkspaceMessages.Any(m => m.Contains("crowded")));
        using (var image = window.CaptureRenderedFrame())
            image!.Save(Path.Combine(AppContext.BaseDirectory, "designer-quiet-frame.png"), PngBitmapEncoderOptions.Default);
        frame.X = 185;
        frame.WidthDots = 595;
        designer.NotifyDocumentEdited();
        Pump(500);
        check("Moving a frame edge into the margin reports crowding", designer.ValidationWarning.Contains("crowded"));
        check("The warning identifies the frame", designer.ValidationWarning.Contains("Perimeter frame"));
        designer.UndoCommand.Execute(null);
        Pump(500);
        check("Undoing the frame move clears crowding", designer.ValidationWarning.Length == 0);
        designer.RedoCommand.Execute(null);
        Pump(500);
        check("Redoing the frame move restores crowding", designer.ValidationWarning.Contains("crowded"));
        designer.CheckQuietZones = false;
        Pump(500);
        check("Quiet zones can still be disabled", designer.ValidationWarning.Length == 0);
        designer.NewDocumentCommand.Execute(null);
        Pump(200);
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
