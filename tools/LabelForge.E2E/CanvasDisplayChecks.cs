using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelForge.App.Controls;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Model;
using SkiaSharp;

internal static class CanvasDisplayChecks
{
    public static void Run(MainWindow window, DesignerViewModel designer, Action<string, bool> check)
    {
        var view = window.GetVisualDescendants().OfType<DesignerView>().Single();
        var canvas = view.FindControl<DesignerCanvas>("Canvas")!;
        var outlines = view.FindControl<MenuItem>("ElementOutlinesMenu")!;
        var originalTheme = Application.Current!.RequestedThemeVariant;
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current.RequestedThemeVariant = theme;
            designer.NewDocumentCommand.Execute(null);
            Element[] elements = [
                new BoxElement { X = 80, Y = 80, WidthDots = 100, HeightDots = 60, IsWhite = true },
                new BoxElement { X = 250, Y = 80, WidthDots = 30, HeightDots = 30, ThicknessDots = 30, ZOrder = 1 },
                new BoxElement { X = 240, Y = 70, WidthDots = 60, HeightDots = 50, ThicknessDots = 50, IsWhite = true, ZOrder = 2 },
                new BoxElement { X = 400, Y = 80, WidthDots = 80, HeightDots = 60, IsVisible = false },
                new BoxElement { X = 80, Y = 200, WidthDots = 100, HeightDots = 60, IsWhite = true, IsLocked = true },
            ];
            foreach (var element in elements) designer.Document.Elements.Add(element);
            designer.NotifyDocumentEdited();
            designer.Selection.Clear();
            window.MouseMove(new Point(5, 5));
            canvas.ResetView();
            canvas.SetZoom(1);
            outlines.IsChecked = false;
            Pump(700);
            string document = designer.SerializeDocument();
            string zpl = designer.GeneratedZpl;
            bool undo = designer.CanUndo;
            var underlay = designer.Underlay;
            using var before = Capture(window);
            outlines.IsChecked = true;
            Pump(150);
            using var after = Capture(window);
            check($"{theme}: View toggles element outlines", canvas.ShowElementOutlines);
            check($"{theme}: outlines reveal white elements", ChangedAt(80, 110));
            check($"{theme}: outlines reveal covered elements", ChangedAt(250, 95));
            check($"{theme}: outlines reveal locked elements", ChangedAt(80, 230));
            check($"{theme}: hidden elements stay hidden", !ChangedAt(400, 110));
            check($"{theme}: outline does not fill the element", !ChangedAt(120, 110));
            check($"{theme}: outlines leave selection empty", designer.Selection.Count == 0);
            check($"{theme}: outlines preserve label and undo", designer.SerializeDocument() == document && designer.CanUndo == undo);
            check($"{theme}: outlines preserve ZPL and underlay", designer.GeneratedZpl == zpl && ReferenceEquals(underlay, designer.Underlay));
            using (var frame = window.CaptureRenderedFrame())
                frame!.Save(Path.Combine(AppContext.BaseDirectory, $"designer-element-outlines-{theme}.png"), PngBitmapEncoderOptions.Default);
            outlines.IsChecked = false;
            Pump(150);
            using var restored = Capture(window);
            var whiteEdge = canvas.TranslatePoint(canvas.DotsToView(80, 110), window)!.Value;
            check($"{theme}: disabling outlines restores the preview", !DifferentNear(before, restored, whiteEdge));

            bool ChangedAt(double x, double y) => DifferentNear(before, after,
                canvas.TranslatePoint(canvas.DotsToView(x, y), window)!.Value);
        }
        Application.Current.RequestedThemeVariant = originalTheme;
        designer.NewDocumentCommand.Execute(null);
        canvas.ResetView();
        Pump(200);
    }

    private static SKBitmap Capture(Window window)
    {
        using var frame = window.CaptureRenderedFrame();
        using var stream = new MemoryStream();
        frame!.Save(stream, PngBitmapEncoderOptions.Default);
        return SKBitmap.Decode(stream.ToArray());
    }

    private static bool DifferentNear(SKBitmap before, SKBitmap after, Point point)
    {
        int cx = (int)Math.Round(point.X), cy = (int)Math.Round(point.Y);
        if (cx < 3 || cy < 3 || cx >= before.Width - 3 || cy >= before.Height - 3)
            throw new InvalidOperationException($"Pixel check outside viewport: {point}");
        for (int y = cy - 2; y <= cy + 2; y++)
            for (int x = cx - 2; x <= cx + 2; x++)
                if (before.GetPixel(x, y) != after.GetPixel(x, y)) return true;
        return false;
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
