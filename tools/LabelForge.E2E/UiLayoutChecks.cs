using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
                    if (size.Item1 >= 1024)
                    {
                        Check("setup density visible without scrolling",
                            Within(setupWindow.FindControl<ComboBox>("DensityInput")!, setupWindow));
                        Check("setup printer visible without scrolling",
                            setupWindow.FindControl<ComboBox>("PrinterInput") is { } printer &&
                            Within(printer, setupWindow));
                    }
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
            foreach (var narrowWidth in new[] { 700, 500 })
            {
                window.Width = narrowWidth;
                window.Height = 480;
                Pump(100);
                view.FindControl<Button>("LabelSetupButton")!.RaiseEvent(
                    new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(100);
                var narrowSetup = window.OwnedWindows.OfType<LabelSetupWindow>().Single();
                var scroll = narrowSetup.FindControl<ScrollViewer>("SetupScroll")!;
                var printer = narrowSetup.FindControl<ComboBox>("PrinterInput")!;
                Check($"setup {narrowWidth}: fits owner",
                    narrowSetup.Width <= window.Width && narrowSetup.Height <= window.Height);
                Check($"setup {narrowWidth}: media search visible",
                    Within(narrowSetup.FindControl<AutoCompleteBox>("MediaBox")!, scroll));
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                Pump(100);
                Check($"setup {narrowWidth}: printer reachable by scrolling",
                    Within(printer, scroll));
                using var narrowFrame = narrowSetup.CaptureRenderedFrame();
                narrowFrame?.Save(Path.Combine(output, $"setup-{narrowWidth}x480-bottom.png"),
                    PngBitmapEncoderOptions.Default);
                narrowSetup.Close();
            }
            foreach (var helpWidth in new[] { 700, 500 })
            {
                window.Width = helpWidth;
                window.Height = 480;
                Pump(100);
                d.NewDocumentCommand.Execute(null);
                view.FindControl<Button>("ShortcutHelpButton")!.RaiseEvent(
                    new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(100);
                var help = window.OwnedWindows.OfType<ShortcutsWindow>().Single();
                Check($"help {helpWidth}: fits owner",
                    help.Width <= window.ClientSize.Width - 40 &&
                    help.Height <= window.ClientSize.Height - 40);
                var helpScroll = help.FindControl<ScrollViewer>("ShortcutsScroll")!;
                Check($"help {helpWidth}: no horizontal overflow",
                    helpScroll.Extent.Width <= helpScroll.Viewport.Width + 1);
                using var helpFrame = help.CaptureRenderedFrame();
                helpFrame?.Save(Path.Combine(output, $"help-{helpWidth}x480.png"),
                    PngBitmapEncoderOptions.Default);
                help.Close();
            }
            window.Width = 1200;
            window.Height = 760;
            Pump(100);
            d.NewDocumentCommand.Execute(null);
            var inspector = view.FindControl<Border>("InspectorPanel")!;
            var inspectorTabs = view.FindControl<TabControl>("InspectorTabs")!;
            var canvas = view.FindControl<DesignerCanvas>("Canvas")!;
            inspectorTabs.SelectedIndex = 1;
            view.FindControl<Button>("HideInspectorButton")!.RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            d.AddTextCommand.Execute(null);
            Pump(100);
            Point placement = canvas.TranslatePoint(canvas.DotsToView(150, 140), window)!.Value;
            window.MouseDown(placement, MouseButton.Left);
            window.MouseUp(placement, MouseButton.Left);
            Pump(400);
            Check("placing text creates a selected field",
                d.Document.Elements.Count == 1 && d.SelectedElement is TextElement);
            Check("placing text reveals Properties", inspector.IsVisible && inspectorTabs.SelectedIndex == 0);
            var contentBox = view.FindControl<ContentControl>("PropertiesContent")!
                .GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault();
            Check("placing text focuses content", contentBox?.IsKeyboardFocusWithin == true);
            var textInput = contentBox?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            Check("placing text selects starter content",
                textInput is not null && textInput.SelectionStart == 0 &&
                textInput.SelectionEnd == textInput.Text?.Length);
            window.KeyTextInput("a");
            Pump(100);
            Check("typing replaces starter text", ((TextElement)d.SelectedElement!).Text == "a");
            using (var placementFrame = window.CaptureRenderedFrame())
                placementFrame?.Save(Path.Combine(output, "place-and-type.png"), PngBitmapEncoderOptions.Default);

            d.NewDocumentCommand.Execute(null);
            var text = new TextElement { X = 150, Y = 140, Text = "Focus this text", FontHeightDots = 40 };
            d.Document.Elements.Add(text);
            d.NotifyDocumentEdited();
            Pump(400);

            inspectorTabs.SelectedIndex = 2;
            view.FindControl<Button>("HideInspectorButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump(100);
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

            d.NewDocumentCommand.Execute(null);
            inspectorTabs.SelectedIndex = 1;
            view.FindControl<Button>("HideInspectorButton")!.RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            d.AddBoxCommand.Execute(null);
            Pump(100);
            Point boxPoint = canvas.TranslatePoint(canvas.DotsToView(100, 100), window)!.Value;
            window.MouseDown(boxPoint, MouseButton.Left);
            window.MouseUp(boxPoint, MouseButton.Left);
            Pump(150);
            Check("placing a shape keeps the canvas wide", !inspector.IsVisible && inspectorTabs.SelectedIndex == 1);

            d.AddBarcodeCommand.Execute(null);
            Pump(100);
            Point barcodeStart = canvas.TranslatePoint(canvas.DotsToView(400, 300), window)!.Value;
            Point barcodeEnd = canvas.TranslatePoint(canvas.DotsToView(650, 400), window)!.Value;
            window.MouseDown(barcodeStart, MouseButton.Left, RawInputModifiers.Alt);
            window.MouseMove(barcodeEnd, RawInputModifiers.Alt);
            window.MouseUp(barcodeEnd, MouseButton.Left, RawInputModifiers.Alt);
            Pump(400);
            Check("drawing a barcode creates a selected field", d.SelectedElement is BarcodeElement);
            Check("drawing a barcode reveals Properties", inspector.IsVisible && inspectorTabs.SelectedIndex == 0);
            var barcodeBox = view.FindControl<ContentControl>("PropertiesContent")!
                .GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault();
            Check("drawing a barcode focuses data", barcodeBox?.IsKeyboardFocusWithin == true);
            window.KeyTextInput("987");
            Pump(500);
            Check("typing replaces starter barcode data",
                (d.SelectedElement as BarcodeElement)?.Data == "987" &&
                d.GeneratedZpl.Contains("^FD987", StringComparison.Ordinal));
            using (var barcodeFrame = window.CaptureRenderedFrame())
                barcodeFrame?.Save(Path.Combine(output, "barcode-place-and-edit.png"), PngBitmapEncoderOptions.Default);

            d.NewDocumentCommand.Execute(null);
            window.Width = 700;
            window.Height = 480;
            inspectorTabs.SelectedIndex = 1;
            Pump(150);
            d.AddTextCommand.Execute(null);
            Pump(100);
            Point compactPoint = canvas.TranslatePoint(canvas.DotsToView(100, 100), window)!.Value;
            window.MouseDown(compactPoint, MouseButton.Left);
            window.MouseUp(compactPoint, MouseButton.Left);
            Pump(400);
            Check("compact placement opens Properties", inspector.IsVisible && inspectorTabs.SelectedIndex == 0);
            Check("compact placement focuses content",
                view.FindControl<ContentControl>("PropertiesContent")!
                    .GetVisualDescendants().OfType<AutoCompleteBox>()
                    .Any(box => box.IsKeyboardFocusWithin));
            using (var compactPlacementFrame = window.CaptureRenderedFrame())
                compactPlacementFrame?.Save(Path.Combine(output, "compact-place-and-type.png"), PngBitmapEncoderOptions.Default);

            var repeatButton = view.FindControl<ToggleButton>("RepeatToolButton");
            Check("repeat placement control is available", repeatButton is not null);
            if (repeatButton is not null)
            {
                window.Width = 1200;
                window.Height = 760;
                d.NewDocumentCommand.Execute(null);
                Pump(150);
                Check("repeat placement starts off", !d.RepeatInsert && repeatButton.IsChecked != true);
                string beforeRepeat = d.SerializeDocument();
                bool undoBeforeRepeat = d.CanUndo;
                Point toggle = repeatButton.TranslatePoint(
                    new Point(repeatButton.Bounds.Width / 2, repeatButton.Bounds.Height / 2), window)!.Value;
                window.MouseDown(toggle, MouseButton.Left);
                window.MouseUp(toggle, MouseButton.Left);
                Pump(100);
                Check("repeat button turns on session mode", d.RepeatInsert && repeatButton.IsChecked == true);
                Check("repeat button leaves label and undo alone",
                    d.SerializeDocument() == beforeRepeat && d.CanUndo == undoBeforeRepeat);
                var insertMenu = view.FindControl<MenuItem>("InsertOptionsMenu")!;
                var repeatMenu = view.FindControl<MenuItem>("RepeatPlacementMenu")!;
                Check("Insert menu reflects repeat button", repeatMenu.IsChecked);
                insertMenu.Open();
                Pump(100);
                repeatMenu.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                Pump(100);
                insertMenu.Close();
                Check("Insert menu turns repeat off", !d.RepeatInsert && repeatButton.IsChecked != true);
                insertMenu.Open();
                Pump(100);
                repeatMenu.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                Pump(100);
                insertMenu.Close();
                Check("Insert menu turns repeat back on", d.RepeatInsert && repeatButton.IsChecked == true);
                Check("Insert menu leaves label and undo alone",
                    d.SerializeDocument() == beforeRepeat && d.CanUndo == undoBeforeRepeat);

                d.AddBoxCommand.Execute(null);
                Point firstBox = canvas.TranslatePoint(canvas.DotsToView(100, 100), window)!.Value;
                window.MouseDown(firstBox, MouseButton.Left);
                window.MouseUp(firstBox, MouseButton.Left);
                Pump(150);
                Check("repeat keeps box tool after first click",
                    d.Document.Elements.Count == 1 && d.IsPlacing && d.ArmedTool == "Box");
                Point secondBox = canvas.TranslatePoint(canvas.DotsToView(400, 300), window)!.Value;
                window.MouseDown(secondBox, MouseButton.Left);
                window.MouseUp(secondBox, MouseButton.Left);
                Pump(150);
                Check("repeat places another box with same tool",
                    d.Document.Elements.Count == 2 && d.Document.Elements.All(e => e is BoxElement) &&
                    d.IsPlacing && d.ArmedTool == "Box");
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Pump(100);
                Check("Escape stops tool but keeps repeat preference", !d.IsPlacing && d.RepeatInsert);
                d.UndoCommand.Execute(null);
                Check("first undo removes only second placement", d.Document.Elements.Count == 1);
                d.UndoCommand.Execute(null);
                Check("second undo removes first placement", d.Document.Elements.Count == 0);

                d.AddTextCommand.Execute(null);
                Point firstText = canvas.TranslatePoint(canvas.DotsToView(100, 100), window)!.Value;
                window.MouseDown(firstText, MouseButton.Left);
                window.MouseUp(firstText, MouseButton.Left);
                Pump(400);
                window.KeyTextInput("First");
                Pump(150);
                Point secondText = canvas.TranslatePoint(canvas.DotsToView(450, 300), window)!.Value;
                window.MouseDown(secondText, MouseButton.Left);
                window.MouseUp(secondText, MouseButton.Left);
                Pump(400);
                window.KeyTextInput("Second");
                Pump(500);
                Check("repeat text can be typed after each placement",
                    d.Document.Elements.OfType<TextElement>().Select(e => e.Text)
                        .SequenceEqual(new[] { "First", "Second" }) && d.IsPlacing);
                using (var repeatFrame = window.CaptureRenderedFrame())
                    repeatFrame?.Save(Path.Combine(output, "repeat-place-and-type.png"), PngBitmapEncoderOptions.Default);

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                d.InsertAt(d.AddBoxCommand, 600, 350);
                Pump(150);
                Check("Insert Here remains one-shot in repeat mode",
                    d.Document.Elements.Count == 3 && d.Document.Elements[^1] is BoxElement &&
                    !d.IsPlacing && d.RepeatInsert);

                d.NewDocumentCommand.Execute(null);
                d.AddBoxCommand.Execute(null);
                Point drawStart = canvas.TranslatePoint(canvas.DotsToView(120, 120), window)!.Value;
                Point drawEnd = canvas.TranslatePoint(canvas.DotsToView(330, 230), window)!.Value;
                window.MouseDown(drawStart, MouseButton.Left);
                window.MouseMove(drawEnd);
                window.MouseUp(drawEnd, MouseButton.Left);
                Pump(150);
                Check("repeat keeps tool after drawing",
                    d.Document.Elements.Count == 1 && d.Document.Elements[0] is BoxElement &&
                    d.IsPlacing && d.ArmedTool == "Box");
                window.MouseDown(drawStart, MouseButton.Left);
                window.MouseMove(drawEnd);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.MouseUp(drawEnd, MouseButton.Left);
                Pump(150);
                Check("Escape discards unfinished repeat draw",
                    d.Document.Elements.Count == 1 && !d.IsPlacing && d.RepeatInsert);
                window.Width = 700;
                window.Height = 480;
                d.NewDocumentCommand.Execute(null);
                Pump(150);
                d.AddBoxCommand.Execute(null);
                Point compactFirst = canvas.TranslatePoint(canvas.DotsToView(80, 80), window)!.Value;
                window.MouseDown(compactFirst, MouseButton.Left);
                window.MouseUp(compactFirst, MouseButton.Left);
                Pump(150);
                Point compactSecond = canvas.TranslatePoint(canvas.DotsToView(420, 250), window)!.Value;
                window.MouseDown(compactSecond, MouseButton.Left);
                window.MouseUp(compactSecond, MouseButton.Left);
                Pump(150);
                Check("repeat placement works in compact window",
                    Within(repeatButton, view.FindControl<Border>("CreationRail")!) &&
                    d.Document.Elements.Count == 2 && d.IsPlacing);
                using (var compactRepeatFrame = window.CaptureRenderedFrame())
                    compactRepeatFrame?.Save(Path.Combine(output, "compact-repeat.png"), PngBitmapEncoderOptions.Default);
                d.NewDocumentCommand.Execute(null);
                Check("new label cancels armed tool but keeps repeat preference",
                    !d.IsPlacing && d.Document.Elements.Count == 0 && d.RepeatInsert);
                d.AddBoxCommand.Execute(null);
                repeatButton.IsChecked = false;
                Pump(100);
                Point oneShot = canvas.TranslatePoint(canvas.DotsToView(200, 150), window)!.Value;
                window.MouseDown(oneShot, MouseButton.Left);
                window.MouseUp(oneShot, MouseButton.Left);
                Pump(150);
                Check("turning repeat off makes armed tool one-shot",
                    !d.RepeatInsert && !d.IsPlacing && d.Document.Elements.Count == 1);
            }
            var findBox = view.FindControl<TextBox>("OutlineFindBox");
            Check("Elements has a field finder", findBox is not null);
            if (findBox is not null)
            {
                d.NewDocumentCommand.Execute(null);
                window.Width = 1200;
                window.Height = 760;
                Pump(150);
                TextElement? firstMatch = null, secondMatch = null;
                for (int i = 0; i < 30; i++)
                {
                    var field = new TextElement
                    {
                        X = 20, Y = 10 + i * 15, FontHeightDots = 12, ZOrder = 30 - i,
                        Name = i == 4 ? "Shipping field" : i == 22 ? "Backup field" : $"Item {i}",
                        Text = i == 4 ? "ZX-410" : i == 22 ? "ZX-410\nspare" : $"Value {i}",
                    };
                    d.Document.Elements.Add(field);
                    if (i == 4) firstMatch = field;
                    if (i == 22) secondMatch = field;
                }
                var namedBarcode = new BarcodeElement
                {
                    X = 300, Y = 30, ZOrder = 0, Name = "Tracking code", Data = "INV-999",
                };
                d.Document.Elements.Add(namedBarcode);
                d.NotifyDocumentEdited();
                inspectorTabs.SelectedIndex = 1;
                if (!inspector.IsVisible)
                {
                    view.FindControl<Button>("ShowInspectorButton")!.RaiseEvent(
                        new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                }
                Pump(400);
                string beforeFind = d.SerializeDocument();
                string zplBeforeFind = d.GeneratedZpl;
                bool undoBeforeFind = d.CanUndo;
                int rowsBeforeFind = d.Outline.Count;
                findBox.Focus();
                window.KeyTextInput("ZX-410");
                Pump(250);
                Check("finder searches full content behind names",
                    ReferenceEquals(d.SelectedElement, firstMatch) && d.OutlineFindStatus == "1/2");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                Pump(300);
                Check("Enter advances to the next matching field",
                    ReferenceEquals(d.SelectedElement, secondMatch) && d.OutlineFindStatus == "2/2");
                var outline = view.FindControl<ListBox>("ElementsList")!;
                Check("finder scrolls the matching row into view",
                    outline.GetVisualDescendants().OfType<ListBoxItem>().Any(item =>
                        ReferenceEquals(item.DataContext, d.SelectedOutlineRow) && Within(item, outline)));
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                Pump(150);
                Check("finder wraps after the last match",
                    ReferenceEquals(d.SelectedElement, firstMatch) && d.OutlineFindStatus == "1/2");
                findBox.Text = "shipping";
                Pump(150);
                Check("finder matches user names", ReferenceEquals(d.SelectedElement, firstMatch) &&
                    d.OutlineFindStatus == "1/1");
                findBox.Text = "410 spare";
                Pump(150);
                Check("finder matches across content line breaks",
                    ReferenceEquals(d.SelectedElement, secondMatch) && d.OutlineFindStatus == "1/1");
                findBox.Text = "shipping";
                Pump(100);
                findBox.Text = "zzmissing";
                Pump(150);
                Check("missing query keeps the selected field", d.OutlineFindStatus == "No matches" &&
                    ReferenceEquals(d.SelectedElement, firstMatch));
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Pump(150);
                Check("Escape clears find without dropping selection",
                    findBox.Text == string.Empty && d.OutlineFindStatus == string.Empty &&
                    ReferenceEquals(d.SelectedElement, firstMatch));
                Check("finding keeps all stacking rows", d.Outline.Count == rowsBeforeFind);
                Check("finding leaves label, ZPL and undo alone",
                    d.SerializeDocument() == beforeFind && d.GeneratedZpl == zplBeforeFind &&
                    d.CanUndo == undoBeforeFind);
                findBox.Text = "INV-999";
                Pump(150);
                Check("finder searches barcode data behind its name",
                    ReferenceEquals(d.SelectedElement, namedBarcode) && d.OutlineFindStatus == "1/1");

                window.Width = 700;
                window.Height = 480;
                Pump(150);
                if (!inspector.IsVisible)
                {
                    view.FindControl<Button>("ShowInspectorButton")!.RaiseEvent(
                        new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Pump(100);
                }
                findBox.Text = "Backup";
                Pump(200);
                Check("compact finder stays visible and selects the field",
                    inspector.IsVisible && Within(findBox, inspector) &&
                    ReferenceEquals(d.SelectedElement, secondMatch));
                using (var findFrame = window.CaptureRenderedFrame())
                    findFrame?.Save(Path.Combine(output, "compact-elements-find.png"), PngBitmapEncoderOptions.Default);
                view.FindControl<Button>("OutlineFindClearButton")!.RaiseEvent(
                    new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(100);
                Check("Clear button returns focus to empty search", d.OutlineFindText == string.Empty &&
                    findBox.IsKeyboardFocusWithin);
                findBox.Text = "Backup";
                d.NewDocumentCommand.Execute(null);
                Pump(150);
                Check("new label clears finder", findBox.Text == string.Empty &&
                    d.OutlineFindStatus == string.Empty);
            }
            var editButton = view.FindControl<Button>("EditSelectedButton");
            Check("Elements has an Edit action", editButton is not null);
            if (editButton is not null && findBox is not null)
            {
                var editMenu = view.FindControl<MenuItem>("EditSelectedMenu")!;
                Check("Edit is disabled with no selection", !editButton.IsEnabled && !editMenu.IsEnabled);
                int tabWithoutSelection = inspectorTabs.SelectedIndex;
                window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
                Pump(100);
                Check("F2 without selection leaves the inspector alone",
                    inspectorTabs.SelectedIndex == tabWithoutSelection && !d.HasSelection);

                window.Width = 1200;
                window.Height = 760;
                d.NewDocumentCommand.Execute(null);
                var parcel = new TextElement { X = 120, Y = 100, Text = "Old value", Name = "Parcel ID" };
                d.Document.Elements.Add(parcel);
                d.NotifyDocumentEdited();
                inspectorTabs.SelectedIndex = 1;
                if (!inspector.IsVisible)
                {
                    view.FindControl<Button>("ShowInspectorButton")!.RaiseEvent(
                        new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                }
                Pump(350);
                findBox.Text = "Parcel ID";
                findBox.Focus();
                Pump(150);
                Check("finder selection enables Edit", editButton.IsEnabled && editMenu.IsEnabled &&
                    ReferenceEquals(d.SelectedElement, parcel));
                string beforeEdit = d.SerializeDocument();
                bool undoBeforeEdit = d.CanUndo;
                window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
                Pump(400);
                var contentEditor = view.FindControl<ContentControl>("PropertiesContent")!
                    .GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault();
                var innerEditor = contentEditor?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                Check("F2 opens Properties from finder and focuses content",
                    inspector.IsVisible && inspectorTabs.SelectedIndex == 0 &&
                    contentEditor?.IsKeyboardFocusWithin == true);
                Check("F2 selects existing content without editing it",
                    innerEditor is not null && innerEditor.SelectionStart == 0 &&
                    innerEditor.SelectionEnd == innerEditor.Text?.Length &&
                    d.SerializeDocument() == beforeEdit && d.CanUndo == undoBeforeEdit);
                window.KeyTextInput("SHIP");
                Pump(500);
                Check("typing after F2 replaces the selected field's content",
                    parcel.Text == "SHIP" && d.GeneratedZpl.Contains("^FDSHIP", StringComparison.Ordinal));

                inspectorTabs.SelectedIndex = 1;
                findBox.Focus();
                view.FindControl<MenuItem>("EditOptionsMenu")!.Open();
                Pump(100);
                editMenu.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                Pump(350);
                Check("Edit menu opens the selected field's content editor",
                    inspectorTabs.SelectedIndex == 0 &&
                    view.FindControl<ContentControl>("PropertiesContent")!
                        .GetVisualDescendants().OfType<AutoCompleteBox>()
                        .Any(box => box.IsKeyboardFocusWithin));
                view.FindControl<MenuItem>("EditOptionsMenu")!.Close();

                d.NewDocumentCommand.Execute(null);
                var shape = new BoxElement { X = 80, Y = 80, Name = "Old box" };
                d.Document.Elements.Add(shape);
                d.NotifyDocumentEdited();
                d.Selection.Set(shape);
                inspectorTabs.SelectedIndex = 1;
                Pump(300);
                string shapeZpl = d.GeneratedZpl;
                editButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(350);
                var nameEditor = view.FindControl<TextBox>("ElementNameInput")!;
                Check("Edit button focuses shape name",
                    inspectorTabs.SelectedIndex == 0 && nameEditor.IsKeyboardFocusWithin &&
                    nameEditor.SelectionStart == 0 && nameEditor.SelectionEnd == nameEditor.Text?.Length);
                window.KeyTextInput("Frame");
                Pump(500);
                Check("renaming a shape leaves printable ZPL alone",
                    shape.Name == "Frame" && d.GeneratedZpl == shapeZpl);

                d.NewDocumentCommand.Execute(null);
                var compactField = new TextElement { X = 100, Y = 100, Text = "Compact" };
                d.Document.Elements.Add(compactField);
                d.NotifyDocumentEdited();
                d.Selection.Set(compactField);
                inspectorTabs.SelectedIndex = 1;
                window.Width = 700;
                window.Height = 480;
                Pump(150);
                if (inspector.IsVisible)
                {
                    view.FindControl<Button>("HideInspectorButton")!.RaiseEvent(
                        new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                }
                canvas.Focus();
                window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
                Pump(400);
                Check("F2 reveals compact inspector and content editor",
                    inspector.IsVisible && Within(inspector, window) && inspectorTabs.SelectedIndex == 0 &&
                    view.FindControl<ContentControl>("PropertiesContent")!
                        .GetVisualDescendants().OfType<AutoCompleteBox>()
                        .Any(box => box.IsKeyboardFocusWithin));
                using (var editFrame = window.CaptureRenderedFrame())
                    editFrame?.Save(Path.Combine(output, "compact-edit-selected.png"), PngBitmapEncoderOptions.Default);
                Check("keyboard help documents F2",
                    new ShortcutsViewModel().Groups.SelectMany(g => g.Entries)
                        .Any(entry => entry.Keys == "F2"));
            }
            var quickEditor = view.FindControl<ContentControl>("QuickContentEditor");
            Check("Elements has a quick content editor", quickEditor is not null);
            if (quickEditor is not null && findBox is not null)
            {
                window.Width = 1200;
                window.Height = 760;
                d.NewDocumentCommand.Execute(null);
                var job = new TextElement { X = 100, Y = 100, Text = "Old job", Name = "Job" };
                var sku = new BarcodeElement { X = 100, Y = 180, Data = "ABC123", Name = "SKU" };
                d.Document.Elements.Add(job);
                d.Document.Elements.Add(sku);
                d.NotifyDocumentEdited();
                Pump(250);
                inspectorTabs.SelectedIndex = 1;
                if (!inspector.IsVisible)
                {
                    view.FindControl<Button>("ShowInspectorButton")!.RaiseEvent(
                        new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                }
                string beforeQuickNavigation = d.SerializeDocument();
                bool undoBeforeQuickNavigation = d.CanUndo;
                findBox.Text = "Job";
                Pump(350);
                var jobBox = quickEditor.GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault();
                Check("text selection shows quick content while Elements stays open",
                    quickEditor.IsEffectivelyVisible && jobBox?.Text == "Old job" &&
                    inspectorTabs.SelectedIndex == 1 && ReferenceEquals(d.SelectedElement, job));
                Check("quick editor navigation leaves document and undo alone",
                    d.SerializeDocument() == beforeQuickNavigation && d.CanUndo == undoBeforeQuickNavigation);
                Check("quick editor filters marker suggestions",
                    jobBox?.FilterMode == AutoCompleteFilterMode.Custom &&
                    jobBox.ItemFilter?.Invoke("##CO", "##CODIGO##") == true &&
                    jobBox.ItemFilter?.Invoke("plain", "##CODIGO##") == false);
                jobBox?.Focus();
                var jobInner = jobBox?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                jobInner?.SelectAll();
                window.KeyTextInput("PACKED");
                Pump(450);
                Check("quick text edit writes ZPL without leaving Elements",
                    job.Text == "PACKED" && d.GeneratedZpl.Contains("^FDPACKED", StringComparison.Ordinal) &&
                    inspectorTabs.SelectedIndex == 1 && jobBox?.IsKeyboardFocusWithin == true);
                Check("quick edit changes the document",
                    d.SerializeDocument() != beforeQuickNavigation);

                findBox.Text = "SKU";
                Pump(350);
                var skuBox = quickEditor.GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault();
                Check("finding another field updates quick content",
                    ReferenceEquals(d.SelectedElement, sku) && quickEditor.IsVisible &&
                    skuBox?.Text == "ABC123" && inspectorTabs.SelectedIndex == 1 &&
                    job.Text == "PACKED");
                skuBox?.Focus();
                skuBox?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.SelectAll();
                window.KeyTextInput("ZX900");
                Pump(450);
                Check("quick barcode edit writes data without changing the chosen row",
                    sku.Data == "ZX900" && d.GeneratedZpl.Contains("^FDZX900", StringComparison.Ordinal) &&
                    ReferenceEquals(d.SelectedElement, sku) && inspectorTabs.SelectedIndex == 1);
                d.UndoCommand.Execute(null);
                Pump(350);
                Check("undo restores quick barcode data",
                    d.Document.Elements.OfType<BarcodeElement>().FirstOrDefault(e => e.Name == "SKU")?.Data == "ABC123" &&
                    d.Document.Elements.OfType<TextElement>().FirstOrDefault(e => e.Name == "Job")?.Text == "PACKED");
                d.RedoCommand.Execute(null);
                Pump(350);
                Check("redo restores quick barcode data",
                    d.Document.Elements.OfType<BarcodeElement>().FirstOrDefault(e => e.Name == "SKU")?.Data == "ZX900");
                job = d.Document.Elements.OfType<TextElement>().FirstOrDefault(e => e.Name == "Job") ?? job;
                var unnamed = new TextElement { X = 100, Y = 250, Text = "Draft" };
                d.Document.Elements.Add(unnamed);
                d.NotifyDocumentEdited();
                Pump(250);
                d.Selection.Set(unnamed);
                Pump(250);
                var unnamedBox = quickEditor.GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault();
                unnamedBox?.Focus();
                unnamedBox?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.SelectAll();
                window.KeyTextInput("Ready");
                Pump(450);
                Check("editing an unnamed field keeps its row and focus",
                    unnamed.Text == "Ready" && ReferenceEquals(d.SelectedElement, unnamed) &&
                    unnamedBox?.IsKeyboardFocusWithin == true &&
                    d.SelectedOutlineRow?.Display.Contains("Ready", StringComparison.Ordinal) == true);

                var qr = new QrCodeElement { X = 250, Y = 180, Data = "OLD2D", Name = "Route" };
                d.Document.Elements.Add(qr);
                d.NotifyDocumentEdited();
                Pump(250);
                d.Selection.Set(qr);
                Pump(250);
                var qrBox = quickEditor.GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault();
                qrBox?.Focus();
                qrBox?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.SelectAll();
                window.KeyTextInput("NEW2D");
                Pump(450);
                Check("quick editor writes 2D data",
                    qrBox?.IsKeyboardFocusWithin == true && qr.Data == "NEW2D" &&
                    d.GeneratedZpl.Contains("NEW2D", StringComparison.Ordinal));

                var frame = new BoxElement { X = 250, Y = 80, Name = "Frame" };
                d.Document.Elements.Add(frame);
                d.NotifyDocumentEdited();
                d.Selection.Set(frame);
                Pump(250);
                Check("shape selection hides quick content", !d.HasQuickContent && !quickEditor.IsEffectivelyVisible);
                d.Selection.SetMany(new Element[] { unnamed, qr });
                Pump(150);
                Check("multi-selection hides quick content", !d.HasQuickContent && !quickEditor.IsEffectivelyVisible);
                d.Selection.Clear();
                Pump(150);
                Check("empty selection hides quick content", !d.HasQuickContent && !quickEditor.IsEffectivelyVisible);

                d.Selection.Set(job);
                window.Width = 700;
                window.Height = 480;
                Pump(150);
                view.FindControl<Button>("ShowInspectorButton")!.RaiseEvent(
                    new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(350);
                Check("quick content fits the compact inspector",
                    inspector.IsEffectivelyVisible && quickEditor.IsEffectivelyVisible &&
                    Within(quickEditor, window) && inspectorTabs.SelectedIndex == 1 &&
                    quickEditor.GetVisualDescendants().OfType<AutoCompleteBox>().Any());
                using (var quickFrame = window.CaptureRenderedFrame())
                    quickFrame?.Save(Path.Combine(output, "compact-quick-content.png"), PngBitmapEncoderOptions.Default);
            }
            var nextQuick = view.FindControl<Button>("NextQuickFieldButton");
            var previousQuick = view.FindControl<Button>("PreviousQuickFieldButton");
            Check("Elements offers Previous and Next fields",
                nextQuick is not null && previousQuick is not null);
            if (nextQuick is not null && previousQuick is not null && findBox is not null)
            {
                window.Width = 1200;
                window.Height = 760;
                d.NewDocumentCommand.Execute(null);
                var firstQuick = new TextElement { X = 80, Y = 80, Text = "First value", Name = "First" };
                var boxBetween = new BoxElement { X = 180, Y = 80 };
                var groupId = Guid.NewGuid();
                var groupedA = new TextElement { X = 80, Y = 130, Text = "Group A", GroupId = groupId };
                var groupedB = new TextElement { X = 80, Y = 170, Text = "Group B", GroupId = groupId };
                var secondQuick = new BarcodeElement { X = 80, Y = 210, Data = "ABC123", Name = "Second" };
                var thirdQuick = new QrCodeElement { X = 80, Y = 300, Data = "Route", Name = "Third" };
                foreach (var element in new Element[] { firstQuick, boxBetween, groupedA, groupedB, secondQuick, thirdQuick })
                    d.Document.Elements.Add(element);
                d.NotifyDocumentEdited();
                Pump(300);
                inspectorTabs.SelectedIndex = 1;
                if (!inspector.IsVisible)
                {
                    view.FindControl<Button>("ShowInspectorButton")!.RaiseEvent(
                        new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                }
                findBox.Text = "First";
                Pump(300);
                Check("field navigation is enabled for selected content",
                    nextQuick.IsEnabled && previousQuick.IsEnabled &&
                    ReferenceEquals(d.SelectedElement, firstQuick));
                string beforeQuickStep = d.SerializeDocument();
                bool undoBeforeQuickStep = d.CanUndo;
                nextQuick.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(350);
                var quickValue = view.FindControl<ContentControl>("QuickContentEditor")!;
                var stepBox = quickValue.GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault();
                var stepInner = stepBox?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                Check("Next skips shapes and grouped fields, then selects the value",
                    ReferenceEquals(d.SelectedElement, secondQuick) &&
                    d.OutlineFindText == string.Empty && inspectorTabs.SelectedIndex == 1 &&
                    stepBox?.IsKeyboardFocusWithin == true &&
                    stepInner?.SelectionStart == 0 && stepInner.SelectionEnd == stepInner.Text?.Length);
                Check("field navigation leaves document and undo alone",
                    d.SerializeDocument() == beforeQuickStep && d.CanUndo == undoBeforeQuickStep);
                window.KeyTextInput("SKU9");
                Pump(450);
                Check("typing after Next edits the selected field",
                    secondQuick.Data == "SKU9" && d.GeneratedZpl.Contains("^FDSKU9", StringComparison.Ordinal));

                window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "\r");
                Pump(350);
                Check("Ctrl+Enter advances to the next editable field",
                    ReferenceEquals(d.SelectedElement, thirdQuick) &&
                    quickValue.GetVisualDescendants().OfType<AutoCompleteBox>()
                        .Any(box => box.IsKeyboardFocusWithin));
                window.KeyPress(Key.Enter, RawInputModifiers.Control | RawInputModifiers.Shift,
                    PhysicalKey.Enter, "\r");
                Pump(350);
                Check("Ctrl+Shift+Enter returns to the previous editable field",
                    ReferenceEquals(d.SelectedElement, secondQuick) &&
                    quickValue.GetVisualDescendants().OfType<AutoCompleteBox>()
                        .Any(box => box.IsKeyboardFocusWithin));
                previousQuick.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(300);
                Check("Previous selects the earlier field", ReferenceEquals(d.SelectedElement, firstQuick));
                previousQuick.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(300);
                Check("Previous wraps to the last editable field",
                    ReferenceEquals(d.SelectedElement, thirdQuick));
                nextQuick.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(300);
                Check("Next wraps to the first editable field",
                    ReferenceEquals(d.SelectedElement, firstQuick));

                window.Width = 700;
                window.Height = 480;
                Pump(150);
                view.FindControl<Button>("ShowInspectorButton")!.RaiseEvent(
                    new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(200);
                previousQuick.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(350);
                var outlineList = view.FindControl<ListBox>("ElementsList")!;
                Check("compact navigation keeps the selected row visible",
                    inspector.IsEffectivelyVisible && Within(previousQuick, inspector) &&
                    Within(nextQuick, inspector) && ReferenceEquals(d.SelectedElement, thirdQuick) &&
                    outlineList.GetVisualDescendants().OfType<ListBoxItem>().Any(item =>
                        ReferenceEquals(item.DataContext, d.SelectedOutlineRow) && Within(item, outlineList)));
                using (var navigationFrame = window.CaptureRenderedFrame())
                    navigationFrame?.Save(Path.Combine(output, "compact-quick-navigation.png"), PngBitmapEncoderOptions.Default);
                d.Selection.Clear();
                Pump(150);
                Check("field navigation disables without a quick value",
                    !nextQuick.IsEnabled && !previousQuick.IsEnabled);
                var shortcutEntries = new ShortcutsViewModel().Groups.SelectMany(g => g.Entries).ToArray();
                Check("keyboard help documents quick field navigation",
                    shortcutEntries.Any(entry => entry.Keys == "Ctrl + Enter") &&
                    shortcutEntries.Any(entry => entry.Keys == "Ctrl + Shift + Enter"));
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
