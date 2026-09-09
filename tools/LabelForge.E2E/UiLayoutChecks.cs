using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using LabelForge.App.Controls;
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
                Pump(100);
                if (!baseline)
                {
                    view.FindControl<Button>("LabelSetupButton")!.RaiseEvent(
                        new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Pump(100);
                    var setupWindow = window.OwnedWindows.OfType<LabelSetupWindow>().Single();
                    Check("setup uses the active document", ReferenceEquals(setupWindow.DataContext, d));
                    Check("setup fits its owner", setupWindow.Width <= window.Width && setupWindow.Height <= window.Height);
                    var width = setupWindow.FindControl<NumericUpDown>("LabelWidthInput")!;
                    width.Value = 110;
                    Pump(60);
                    Check("setup width edits the document", d.WidthMm == 110);
                    Check("media picker remains available", setupWindow.FindControl<AutoCompleteBox>("MediaBox") is not null);
                    using var setupFrame = setupWindow.CaptureRenderedFrame();
                    setupFrame?.Save(Path.Combine(output, $"{theme}-{size.Item1}x{size.Item2}-setup.png"), PngBitmapEncoderOptions.Default);
                    setupWindow.Close();
                }
                foreach (string scene in new[] { "blank", "text", "barcode", "image", "multiple", "data" })
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
                        if (scene == "image")
                        {
                            using var bitmap = new SkiaSharp.SKBitmap(40, 20);
                            bitmap.Erase(SkiaSharp.SKColors.Black);
                            using var png = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                            d.Document.Elements.Add(new ImageElement
                            {
                                X = 100, Y = 100, WidthDots = 160, HeightDots = 80,
                                ImageData = png.ToArray(), SourcePixelWidth = 40, SourcePixelHeight = 20,
                            });
                        }
                        if (scene == "multiple")
                            d.Document.Elements.Add(new BoxElement { X = 180, Y = 120 });
                        if (scene == "data")
                        {
                            d.IsContinuous = true;
                            d.LabelsAcross = 3;
                            d.Document.Variables["SAMPLE_0"] = new() { Kind = VariableKind.Counter };
                            d.Document.Variables["SAMPLE_1"] = new() { Kind = VariableKind.Clock };
                        }
                        d.NotifyDocumentEdited();
                        if (scene == "multiple") d.Selection.SetMany(d.Document.Elements);
                        else d.Selection.Set(d.Document.Elements[^1]);
                    }
                    var inspectorTabs = view.FindControl<TabControl>("InspectorTabs");
                    if (inspectorTabs is not null) inspectorTabs.SelectedIndex = scene == "data" ? 2 : 0;
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
                    if (!baseline && scene == "data")
                    {
                        var variableEditors = view.GetVisualDescendants().OfType<Expander>()
                            .Where(e => e.DataContext is VariableSampleViewModel).ToArray();
                        var counter = variableEditors.Single(e => ((VariableSampleViewModel)e.DataContext!).Name == "SAMPLE_0");
                        var clock = variableEditors.Single(e => ((VariableSampleViewModel)e.DataContext!).Name == "SAMPLE_1");
                        counter.IsExpanded = true;
                        clock.IsExpanded = true;
                        Pump(100);
                        counter.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "CounterStartInput").Value = 42;
                        clock.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "ClockFormatInput").Text = "yyyy-MM-dd";
                        Pump(100);
                        Check($"{tag}: Data edits counter start", d.Document.Variables["SAMPLE_0"].CounterStart == 42);
                        Check($"{tag}: Data edits clock format", d.Document.Variables["SAMPLE_1"].ClockFormat == "yyyy-MM-dd");
                        string document = d.SerializeDocument();
                        bool undo = d.CanUndo;
                        inspectorTabs!.SelectedIndex = 1;
                        Pump(100);
                        var outline = view.FindControl<ListBox>("ElementsList")!;
                        outline.SelectedIndex = 1;
                        Check($"{tag}: outline selection keeps Elements open", inspectorTabs.SelectedIndex == 1 && d.HasSelection);
                        Check($"{tag}: outline toggles fit their row",
                            outline.GetVisualDescendants().OfType<CheckBox>()
                                .Where(c => c.Parent is Grid).All(c => Within(c, (Control)c.Parent!)));
                        using var outlineFrame = window.CaptureRenderedFrame();
                        outlineFrame?.Save(Path.Combine(output, $"{tag}-elements.png"), PngBitmapEncoderOptions.Default);
                        inspectorTabs.SelectedIndex = 0;
                        view.FindControl<Button>("HideInspectorButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        Pump(60);
                        Check($"{tag}: inspector hides", !inspector.IsVisible);
                        view.FindControl<Button>("ShowInspectorButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        Pump(60);
                        Check($"{tag}: inspector reopens", inspector.IsVisible);
                        Check($"{tag}: navigation preserves document and undo", d.SerializeDocument() == document && d.CanUndo == undo);
                    }
                }
            }
        }
        if (!baseline)
        {
            window.Width = 1200;
            window.Height = 760;
            Pump(100);
            d.NewDocumentCommand.Execute(null);
            var text = new TextElement { X = 150, Y = 140, Text = "Focus this text", FontHeightDots = 40 };
            d.Document.Elements.Add(text);
            d.NotifyDocumentEdited();
            Pump(400);
            var inspector = view.FindControl<Border>("InspectorPanel")!;
            var inspectorTabs = view.FindControl<TabControl>("InspectorTabs")!;
            inspectorTabs.SelectedIndex = 2;
            view.FindControl<Button>("HideInspectorButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump(100);
            var canvas = view.FindControl<DesignerCanvas>("Canvas")!;
            Point at = canvas.TranslatePoint(canvas.DotsToView(190, 155), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Pump(300);
            Check("double-click reveals Properties", inspector.IsVisible && inspectorTabs.SelectedIndex == 0);
            Check("double-click focuses the content editor",
                view.FindControl<ContentControl>("PropertiesContent")!.GetVisualDescendants()
                    .OfType<AutoCompleteBox>().Any(b => b.IsKeyboardFocusWithin));
            view.FindControl<NumericUpDown>("PositionXInput")!.Value = 175;
            view.FindControl<NumericUpDown>("PositionYInput")!.Value = 165;
            Pump(100);
            Check("Properties edits element position", text.X == 175 && text.Y == 165);
            string original = d.SerializeDocument();
            bool undo = d.CanUndo;
            var splitter = view.FindControl<GridSplitter>("InspectorSplitter")!;
            splitter.Focus();
            double widthBefore = inspector.Bounds.Width;
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            Pump(100);
            Check("keyboard resizes the inspector", Math.Abs(inspector.Bounds.Width - widthBefore) >= 1);
            double resizedWidth = inspector.Bounds.Width;
            window.Width = 700;
            window.Height = 480;
            Pump(150);
            Check("compact window starts with inspector collapsed", !inspector.IsVisible);
            Check("short window preserves canvas space",
                view.FindControl<Grid>("CanvasRegion")!.Bounds.Height >= 180);
            view.FindControl<Button>("ShowInspectorButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump(100);
            Check("compact inspector opens inside the window", inspector.IsVisible && Within(inspector, window));
            using var compactFrame = window.CaptureRenderedFrame();
            compactFrame?.Save(Path.Combine(output, "compact-inspector.png"), PngBitmapEncoderOptions.Default);
            view.FindControl<Button>("HideInspectorButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.Width = 1200;
            window.Height = 760;
            Pump(150);
            Check("leaving compact mode restores the session width", Math.Abs(inspector.Bounds.Width - resizedWidth) < 1);
            Check("resizing preserves document and undo", d.SerializeDocument() == original && d.CanUndo == undo);
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
