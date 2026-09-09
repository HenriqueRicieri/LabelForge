using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Model;

internal static class UiLayoutChecks
{
    public static int Run(MainWindow window, MainViewModel main, bool baseline)
    {
        int checks = 0, failures = 0;
        var view = window.GetVisualDescendants().OfType<DesignerView>().Single();
        var d = main.Designer;
        string output = Path.Combine(AppContext.BaseDirectory, baseline ? "ui-before" : "ui-after");
        Directory.CreateDirectory(output);

        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current!.RequestedThemeVariant = theme;
            foreach (var size in new[] { (1024, 640), (1200, 760), (1440, 900) })
            {
                window.Width = size.Item1;
                window.Height = size.Item2;
                foreach (string scene in new[] { "blank", "text", "barcode", "data" })
                {
                    d.NewDocumentCommand.Execute(null);
                    if (scene != "blank")
                    {
                        var group = Guid.NewGuid();
                        for (int i = 0; i < (scene == "data" ? 18 : 1); i++)
                        {
                            d.Document.Elements.Add(new TextElement
                            {
                                X = 30, Y = 30 + i * 25, FontHeightDots = 20,
                                Text = scene == "data" ? $"Field ##SAMPLE_{i}##" : "Label content",
                                Name = "A long element name that must leave room for the visibility and lock controls",
                                BlockWidthDots = 280, BlockMaxLines = 3,
                                GroupId = scene == "data" ? group : null,
                            });
                        }
                        if (scene == "barcode")
                            d.Document.Elements.Add(new BarcodeElement { X = 50, Y = 110, Data = "ABC123" });
                        if (scene == "data")
                        {
                            d.IsContinuous = true;
                            d.LabelsAcross = 3;
                        }
                        d.NotifyDocumentEdited();
                        d.Selection.Set(d.Document.Elements[^1]);
                    }
                    var zpl = view.FindControl<Expander>("ZplPanel")!;
                    zpl.IsExpanded = scene == "data";
                    Pump(400);
                    string tag = $"{theme}-{size.Item1}x{size.Item2}-{scene}";
                    var rail = view.FindControl<Border>("CreationRail")!;
                    var setup = view.FindControl<Border>("LabelSetupBar")!;
                    var inspector = view.FindControl<Border>("InspectorPanel")!;
                    var region = view.FindControl<Grid>("CanvasRegion")!;
                    var tools = rail.GetVisualDescendants().OfType<Button>()
                        .Where(b => b.Classes.Contains("tool")).ToArray();
                    bool railScrollable = rail.GetVisualDescendants().OfType<ScrollViewer>()
                        .Any(s => s.Extent.Height <= s.Viewport.Height || s.VerticalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
                    Check($"{tag}: creation tools contained or scrollable",
                        railScrollable || tools.All(b => Within(b, rail)));
                    Check($"{tag}: setup controls contained",
                        setup.GetVisualDescendants().OfType<Control>()
                            .Where(c => c is NumericUpDown or ComboBox or AutoCompleteBox)
                            .Where(c => c.IsEffectivelyVisible).All(c => Within(c, setup)));
                    Check($"{tag}: canvas has usable area", region.Bounds.Width >= 360 && region.Bounds.Height >= 180);
                    Check($"{tag}: inspector stays in window", Within(inspector, window));
                    using var frame = window.CaptureRenderedFrame();
                    Check($"{tag}: screenshot available", frame is not null);
                    frame?.Save(Path.Combine(output, $"{tag}.png"), PngBitmapEncoderOptions.Default);
                }
            }
        }
        d.ShutDown();
        window.Close();
        Console.WriteLine($"{checks} layout checks graded, {failures} disagreed");
        if (baseline)
            Console.WriteLine("Baseline failures describe the old layout; they are not passing checks.");
        return failures == 0 ? 0 : 1;

        void Check(string label, bool found)
        {
            checks++;
            if (!found) failures++;
            Console.WriteLine($"{label}: {found} (expected True)");
        }
    }

    private static bool Within(Control child, Control parent)
    {
        if (child.TranslatePoint(default, parent) is not { } point) return false;
        return point.X >= -1 && point.Y >= -1 &&
            point.X + child.Bounds.Width <= parent.Bounds.Width + 1 &&
            point.Y + child.Bounds.Height <= parent.Bounds.Height + 1;
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
