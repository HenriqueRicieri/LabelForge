using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using SkiaSharp;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelForge.App.Controls;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Model;

internal static class CanvasPaintChecks
{
    public static void Run(MainWindow window, DesignerViewModel designer, Action<string, bool> check, bool reportTimings = false)
    {
        var view = window.GetVisualDescendants().OfType<DesignerView>().Single();
        var canvas = view.FindControl<DesignerCanvas>("Canvas")!;
        var menu = view.FindControl<MenuItem>("CanvasPerformanceMenu")!;
        var label = new LabelDocument { WidthMm = 120, HeightMm = 100, Dpmm = 8 };
        for (int i = 0; i < 78; i++)
        {
            Element element = (i % 3) switch
            {
                0 => new TextElement { Text = $"Product {i}", FontHeightDots = 20 },
                1 => new BarcodeElement { Data = $"ABC{i:000}", ModuleWidthDots = 1, HeightDots = 30, PrintInterpretationLine = false },
                _ => new QrCodeElement { Data = $"Item {i}", Magnification = 1 },
            };
            element.X = 30 + i % 6 * 150;
            element.Y = 30 + i / 6 * 55;
            label.Elements.Add(element);
        }
        designer.LoadDocument(label, path: null);
        designer.Selection.Set(label.Elements[1]);
        Pump(900);
        string json = designer.SerializeDocument();
        string zpl = designer.GeneratedZpl;
        bool undo = designer.CanUndo;
        var underlay = designer.Underlay;
        check("Canvas performance starts disabled", !canvas.ShowCanvasPerformance && !menu.IsChecked);
        menu.IsChecked = true;
        Pump(100);
        check("View toggles canvas performance", canvas.ShowCanvasPerformance);
        void Paint()
        {
            canvas.InvalidateVisual();
            Pump(20);
        }
        for (int i = 0; i < 80; i++) Paint();
        foreach (double zoom in new[] { 0.05, 0.1, 0.25, 0.5, 1, 2, 8, 40 })
        {
            canvas.SetZoom(zoom);
            Pump(60);
            for (int i = 0; i < 5; i++) Paint();
            var times = new List<double>();
            var allocations = new List<long>();
            long paints = canvas.MeasuredPaintCount;
            for (int i = 0; i < 40; i++)
            {
                Paint();
                times.Add(canvas.LastPaintMilliseconds);
                allocations.Add(canvas.LastPaintAllocatedBytes);
            }
            times.Sort();
            allocations.Sort();
            if (reportTimings) Console.WriteLine(FormattableString.Invariant($"Paint 78 elements at {zoom}x: median={times[20]:0.000} ms, p95={times[37]:0.000} ms, allocated={allocations[20]} B"));
            if (zoom == 1)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"Paint allocation sample: canvas={canvas.Bounds.Width:0.##}x{canvas.Bounds.Height:0.##} DIPs, theme={Application.Current!.ActualThemeVariant}, outlines={canvas.ShowElementOutlines}, dot-grid={canvas.ShowPrinterDotGrid}, median={allocations[20]} B, p95={allocations[37]} B, max={allocations[^1]} B, samples={allocations.Count}"));
                check("78-element paint at 1x allocates under 70 KB", allocations[20] < 70_000);
            }
            check(FormattableString.Invariant($"Paint at {zoom}x measures each frame"), canvas.MeasuredPaintCount == paints + 40);
        }
        check("Canvas measurements preserve document and undo", json == designer.SerializeDocument() && undo == designer.CanUndo);
        check("Canvas measurements preserve ZPL and underlay", zpl == designer.GeneratedZpl && ReferenceEquals(underlay, designer.Underlay));
        canvas.ResetView();
        canvas.SetZoom(1);
        Pump(100);
        using (var frame = window.CaptureRenderedFrame())
            frame!.Save(Path.Combine(AppContext.BaseDirectory, "designer-canvas-performance.png"), PngBitmapEncoderOptions.Default);
        menu.IsChecked = false;
        Pump(100);
        long stopped = canvas.MeasuredPaintCount;
        Paint();
        check("Disabling canvas performance stops measurements", !canvas.ShowCanvasPerformance && canvas.MeasuredPaintCount == stopped);
        var originalTheme = Application.Current!.RequestedThemeVariant;
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current.RequestedThemeVariant = theme;
            foreach (int density in new[] { 8, 24 })
            {
                label.Dpmm = density;
                foreach (double zoom in new[] { 0.05, 1, 8, 40 })
                {
                    canvas.ResetView();
                    canvas.SetZoom(zoom);
                    var scroll = canvas.GetScrollInfo();
                    canvas.SetScrollOffsets(scroll.Horizontal.Offset + 13.25, scroll.Vertical.Offset + 17.75);
                    Pump(60);
                    var reference = new DesignerCanvas
                    {
                        Document = label, Selection = canvas.Selection, Underlay = canvas.Underlay,
                        UnderlayMarginDots = canvas.UnderlayMarginDots,
                    };
                    var scope = new ThemeVariantScope { RequestedThemeVariant = theme, Child = reference };
                    scope.Measure(canvas.Bounds.Size);
                    scope.Arrange(new Rect(canvas.Bounds.Size));
                    reference.ResetView();
                    reference.SetZoom(zoom);
                    var referenceScroll = reference.GetScrollInfo();
                    reference.SetScrollOffsets(referenceScroll.Horizontal.Offset + 13.25, referenceScroll.Vertical.Offset + 17.75);
                    using var actual = Capture(canvas);
                    using var expected = Capture(reference);
                    check(FormattableString.Invariant($"{theme} {density} dpmm {zoom}x: cached rulers match a fresh canvas after pan"), SameRulers(actual, expected));
                }
            }
        }
        Application.Current.RequestedThemeVariant = originalTheme;
        designer.NewDocumentCommand.Execute(null);
        canvas.ResetView();
        Pump(200);
    }

    private static SKBitmap Capture(DesignerCanvas canvas)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)canvas.Bounds.Width, (int)canvas.Bounds.Height));
        bitmap.Render(canvas);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return SKBitmap.Decode(stream.ToArray());
    }

    private static bool SameRulers(SKBitmap actual, SKBitmap expected)
    {
        if (actual.Width != expected.Width || actual.Height != expected.Height) return false;
        for (int y = 0; y < actual.Height; y++)
            for (int x = 0; x < (y < 26 ? actual.Width : 26); x++)
                if (actual.GetPixel(x, y) != expected.GetPixel(x, y)) return false;
        return true;
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
