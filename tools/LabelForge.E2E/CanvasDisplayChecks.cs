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

            CheckDotGrid(window, designer, canvas, view, theme, check);

            bool ChangedAt(double x, double y) => DifferentNear(before, after,
                canvas.TranslatePoint(canvas.DotsToView(x, y), window)!.Value);
        }
        Application.Current.RequestedThemeVariant = originalTheme;
        designer.NewDocumentCommand.Execute(null);
        canvas.ResetView();
        Pump(200);
    }

    private static void CheckDotGrid(MainWindow window, DesignerViewModel designer, DesignerCanvas canvas,
        DesignerView view, ThemeVariant theme, Action<string, bool> check)
    {
        var menu = view.FindControl<MenuItem>("PrinterDotGridMenu")!;
        check($"{theme}: dot grid is enabled by default", canvas.ShowPrinterDotGrid && menu.IsChecked);
        foreach (int density in new[] { 8, 12, 24 })
        {
            designer.NewDocumentCommand.Execute(null);
            designer.Document.Dpmm = density;
            designer.Document.Elements.Add(new TextElement
            {
                X = designer.Document.WidthDots / 2 - 40,
                Y = designer.Document.HeightDots / 2 - 30,
                Text = "Aa", FontHeightDots = 60,
            });
            designer.NotifyDocumentEdited();
            Pump(500);
            string document = designer.SerializeDocument();
            string zpl = designer.GeneratedZpl;
            bool undo = designer.CanUndo;
            var underlay = designer.Underlay;
            foreach (double scale in new[] { 7.99, 8, 12.5, 40 })
            {
                canvas.ResetView();
                canvas.SetZoom(scale);
                var scroll = canvas.GetScrollInfo();
                canvas.SetScrollOffsets(scroll.Horizontal.Offset + 13.25, scroll.Vertical.Offset + 17.75);
                menu.IsChecked = false;
                Pump(100);
                using var before = Capture(window);
                menu.IsChecked = true;
                Pump(100);
                using var after = Capture(window);
                Point middle = canvas.ViewToLabel(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2))!.Value;
                double x = Math.Floor(middle.X), y = Math.Floor(middle.Y);
                string tag = $"{theme} {density} dpmm {scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}x";
                check($"{tag}: vertical dot boundaries follow zoom and pan", ChangedAt(x, y + 0.5) == (scale >= 8));
                check($"{tag}: horizontal dot boundaries follow zoom and pan", ChangedAt(x + 0.5, y) == (scale >= 8));
                check($"{tag}: dot interiors keep their original pixels", !ChangedAt(x + 0.5, y + 0.5));
                check($"{tag}: dot grid leaves rulers alone", !DifferentNear(before, after,
                    canvas.TranslatePoint(new Point(13, 100), window)!.Value));
                if (density == 8 && scale == 8)
                {
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(AppContext.BaseDirectory, $"designer-dot-grid-{theme}.png"), PngBitmapEncoderOptions.Default);
                }

                bool ChangedAt(double dx, double dy) => DifferentNear(before, after,
                    canvas.TranslatePoint(canvas.DotsToView(dx, dy), window)!.Value);
            }
            check($"{theme} {density} dpmm: dot grid preserves label and undo", designer.SerializeDocument() == document && designer.CanUndo == undo);
            check($"{theme} {density} dpmm: dot grid preserves ZPL and underlay", designer.GeneratedZpl == zpl && ReferenceEquals(underlay, designer.Underlay));
            menu.IsChecked = false;
            Pump(100);
            check($"{theme} {density} dpmm: View disables the dot grid", !canvas.ShowPrinterDotGrid);
            menu.IsChecked = true;
        }
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
