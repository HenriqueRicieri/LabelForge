using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelForge.App.Controls;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;

AppBuilder.Configure<LabelForge.App.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

if (args.Contains("dark"))
{
    Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
}

var vm = new MainViewModel();
var window = new MainWindow { DataContext = vm };
window.Show();

var tabs = window.FindControl<TabControl>("MainTabs")!;
string mode = args.Length > 0 ? args[0] : "designer";
if (mode == "viewer")
{
    tabs.SelectedIndex = 1;
    if (args.Length > 1)
    {
        vm.Viewer.LoadZpl(File.ReadAllText(args[1]));
    }
}
else
{
    tabs.SelectedIndex = 0;
    var d = vm.Designer;

    Console.WriteLine($"blank start: {d.Document.Elements.Count} elements (expected 0)");
    d.LoadSampleCommand.Execute(null);

    // Exercise undo/redo end to end and report each check.
    int baseline = d.Document.Elements.Count;
    d.AddTextCommand.Execute(null);
    Console.WriteLine($"arm insert: IsPlacing={d.IsPlacing} (expected True), count still {d.Document.Elements.Count}");
    d.PlaceAt(200, 100);
    Console.WriteLine($"add: {baseline} -> {d.Document.Elements.Count} (expected {baseline + 1}), placed at {d.SelectedElement!.X},{d.SelectedElement!.Y} (expected 200,100), IsPlacing={d.IsPlacing}");

    d.Selection.Set(d.Document.Elements[^1]);
    d.SelectionProperties!.X = 300;
    Console.WriteLine($"move via panel: X={d.Document.Elements[^1].X} (expected 300)");

    d.UndoCommand.Execute(null);
    d.UndoCommand.Execute(null);
    Console.WriteLine($"undo x2: {d.Document.Elements.Count} elements (expected {baseline}), CanRedo={d.CanRedo}");

    d.RedoCommand.Execute(null);
    Console.WriteLine($"redo: {d.Document.Elements.Count} elements (expected {baseline + 1})");

    d.UndoCommand.Execute(null);
    Console.WriteLine($"undo again: {d.Document.Elements.Count} elements (expected {baseline}), CanUndo={d.CanUndo}");

    // Select the barcode and edit type-specific properties through the panel.
    // Clipboard and z-order.
    d.Selection.Set(d.Document.Elements[1]); // Title text at X=50
    d.CopyCommand.Execute(null);
    d.PasteCommand.Execute(null);
    Console.WriteLine($"paste: {d.Document.Elements.Count} elements (expected 5), X={d.SelectedElement!.X} (expected 70)");
    d.DuplicateCommand.Execute(null);
    Console.WriteLine($"duplicate: {d.Document.Elements.Count} elements (expected 6), X={d.SelectedElement!.X} (expected 90)");
    d.SendToBackCommand.Execute(null);
    Console.WriteLine($"send to back: ZOrder={d.SelectedElement!.ZOrder} (expected -1)");
    d.BringToFrontCommand.Execute(null);
    Console.WriteLine($"bring to front: ZOrder={d.SelectedElement!.ZOrder} (expected 5)");
    d.UndoCommand.Execute(null);
    d.UndoCommand.Execute(null);
    d.UndoCommand.Execute(null);
    d.UndoCommand.Execute(null);
    Console.WriteLine($"undo x4 back to loaded: {d.Document.Elements.Count} elements (expected 4)");

    d.Selection.Set(d.Document.Elements[2]);
    var barcodePanel = (BarcodePropertiesViewModel)d.SelectionProperties!;
    barcodePanel.ModuleWidth = 4;
    barcodePanel.Interpretation = false;
    var barcodeModel = (LabelForge.Core.Model.BarcodeElement)d.Document.Elements[2];
    Console.WriteLine($"panel edits: module={barcodeModel.ModuleWidthDots} (expected 4), interp={barcodeModel.PrintInterpretationLine} (expected False)");

    // Undo is identity-based: two edits to different properties are two undo steps,
    // so the first undo reverts only the interpretation toggle.
    d.UndoCommand.Execute(null);
    var afterInterpUndo = (LabelForge.Core.Model.BarcodeElement)d.Document.Elements[2];
    Console.WriteLine($"undo interp edit: module={afterInterpUndo.ModuleWidthDots} (expected 4), interp={afterInterpUndo.PrintInterpretationLine} (expected True)");
    d.UndoCommand.Execute(null);
    Console.WriteLine($"undo module edit: module={((LabelForge.Core.Model.BarcodeElement)d.Document.Elements[2]).ModuleWidthDots} (expected 3)");

    // Save/load round trip through the VM (same path the dialogs use).
    string lfl = d.SerializeDocument();
    d.NewDocumentCommand.Execute(null);
    Console.WriteLine($"new: {d.Document.Elements.Count} elements (expected 0), CanUndo={d.CanUndo} (expected False)");
    d.LoadDocument(LabelForge.Core.Io.LabelDocumentJson.Deserialize(lfl), @"C:\tmp\test.lfl");
    Console.WriteLine($"load: {d.Document.Elements.Count} elements (expected 4), path={d.CurrentFilePath}");

    // Printer profile validation.
    d.SelectedPrinter = LabelForge.Core.Printers.PrinterCatalog.All[1]; // ZD421 203
    d.WidthMm = 120;
    Console.WriteLine($"warning @120mm: '{d.PrinterWarning}' (expect head warning)");
    d.WidthMm = 100;
    Console.WriteLine($"warning @100mm: '{d.PrinterWarning}' (expect empty)");

    // Multi-select: group delete + undo, then leave two selected for the capture.
    d.Selection.SetMany([d.Document.Elements[1], d.Document.Elements[2]]);
    Console.WriteLine($"multi: count={d.SelectionCount}, multi={d.HasMultiSelection}, single={d.IsSingleSelection} (expected 2/True/False)");
    d.DeleteSelectedCommand.Execute(null);
    Console.WriteLine($"group delete: {d.Document.Elements.Count} elements (expected 2), HasSelection={d.HasSelection}");
    d.UndoCommand.Execute(null);
    Console.WriteLine($"undo group delete: {d.Document.Elements.Count} elements (expected 4)");

    // Edge cascade: pasting near the border wraps back near the origin instead of
    // clamping, so repeated pastes never pile up on one spot (review finding).
    var edgeElement = d.Document.Elements[0];
    edgeElement.X = d.Document.WidthDots - 5;
    d.Selection.Set(edgeElement);
    d.CopyCommand.Execute(null);
    d.PasteCommand.Execute(null);
    int firstPasteX = d.SelectedElement!.X;
    d.PasteCommand.Execute(null);
    Console.WriteLine($"edge paste wrap: first X={firstPasteX} (expected 20), second X={d.SelectedElement!.X} (expected 40, not stacked)");

    // Off-label placement: park the QR past the right edge. The canvas should show
    // it dimmed on the pasteboard with an amber outline, the toolbar should warn,
    // and the export ZPL should skip it (checked after the render pump below).
    d.Document.Elements[3].X = d.Document.WidthDots + 40;

    // Guides: teal lines with ruler markers, saved with the document.
    d.Document.VerticalGuides.Add(200);
    d.Document.VerticalGuides.Add(d.Document.WidthDots / 2);
    d.Document.HorizontalGuides.Add(240);
    d.NotifyDocumentEdited();

    // Single selection for the capture: shows the 8 handles + rotation handle.
    d.Selection.Set(d.Document.Elements[2]);
}

Pump(2500);

if (mode == "designer")
{
    var d = vm.Designer;
    Console.WriteLine($"placement warning: '{d.PlacementWarning}' (expect QR outside, will not print)");
    Console.WriteLine($"underlay margin: {d.UnderlayMarginDots} dots (expected 160 at 8 dpmm)");
    Console.WriteLine($"export skips parked QR: {!d.GeneratedZpl.Contains("^BQ")} (expected True)");

    var reloaded = LabelForge.Core.Io.LabelDocumentJson.Deserialize(d.SerializeDocument());
    Console.WriteLine($"guides round trip: {reloaded.VerticalGuides.Count} vertical / {reloaded.HorizontalGuides.Count} horizontal (expected 2/1)");

    // Input-path checks through the headless window. Holding the left button on the
    // top ruler shows a transient guide (captured mid-hold); releasing removes it
    // without adding a permanent one. Right-clicking the ruler opens the guide menu.
    var canvas = window.GetVisualDescendants().OfType<DesignerCanvas>().First();
    Avalonia.Point onRuler = canvas.TranslatePoint(new Avalonia.Point(300, 13), window)!.Value;
    window.MouseDown(onRuler, MouseButton.Left);
    window.MouseMove(new Avalonia.Point(onRuler.X + 60, onRuler.Y));
    Pump(300);
    Capture("designer-ruler-hold.png");
    window.MouseUp(new Avalonia.Point(onRuler.X + 60, onRuler.Y), MouseButton.Left);
    Console.WriteLine($"ruler hold released: {d.Document.VerticalGuides.Count} vertical guides (expected 2, transient guide gone)");

    Avalonia.Point menuAt = canvas.TranslatePoint(new Avalonia.Point(500, 13), window)!.Value;
    window.MouseDown(menuAt, MouseButton.Right);
    window.MouseUp(menuAt, MouseButton.Right);
    Pump(400);
    Capture("designer-ruler-menu.png");

    // Click the "Insert guide at N mm" item (first row of the flyout, just under
    // the pointer) and confirm a permanent guide lands.
    var itemAt = new Avalonia.Point(menuAt.X + 60, menuAt.Y + 21);
    window.MouseDown(itemAt, MouseButton.Left);
    window.MouseUp(itemAt, MouseButton.Left);
    Pump(300);
    Console.WriteLine($"menu insert: {d.Document.VerticalGuides.Count} vertical guides (expected 3)");
    window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
    Pump(200);

    // Double click on the top ruler inserts a permanent guide without the menu.
    Avalonia.Point dbl = canvas.TranslatePoint(new Avalonia.Point(700, 13), window)!.Value;
    window.MouseDown(dbl, MouseButton.Left);
    window.MouseUp(dbl, MouseButton.Left);
    window.MouseDown(dbl, MouseButton.Left);
    window.MouseUp(dbl, MouseButton.Left);
    Pump(100);
    Console.WriteLine($"ruler double click: {d.Document.VerticalGuides.Count} vertical guides (expected 4)");

    // Zoom API + floating readout.
    canvas.SetZoom(2.0);
    Pump(200);
    var zoomLabel = window.GetVisualDescendants().OfType<Button>()
        .First(b => b.Name == "ZoomLevelButton");
    Console.WriteLine($"zoom: {canvas.GetZoom():0.##}x, label='{zoomLabel.Content}' (expected 2x / 200%)");
    canvas.ResetView();
    Pump(100);

    // mm position entry: 25 mm at 8 dpmm lands on 200 dots; the display reads mm.
    d.Selection.Set(d.Document.Elements[1]);
    var panel = d.SelectionProperties!;
    panel.UseMm = true;
    panel.X = 25;
    Console.WriteLine($"mm entry: X={d.Document.Elements[1].X} dots (expected 200), shown as {panel.X} mm");
    panel.UseMm = false;
    d.Selection.Set(d.Document.Elements[2]);
    Pump(200);

    // Smart-guide drag: grab the top box copy at (40,55) and move +157 dots right.
    // Its left edge lands 3 dots short of the vertical guide at 200 (snaps to 200)
    // and its top edge sits 5 dots below the Title's top at 50 (snaps to 50).
    var boxCopy = d.Document.Elements[5];
    Avalonia.Point dragFrom = canvas.TranslatePoint(canvas.DotsToView(600, 450), window)!.Value;
    Avalonia.Point dragTo = canvas.TranslatePoint(canvas.DotsToView(757, 450), window)!.Value;
    window.MouseDown(dragFrom, MouseButton.Left);
    window.MouseMove(dragTo);
    window.MouseUp(dragTo, MouseButton.Left);
    Pump(200);
    Console.WriteLine($"snap drag: box at {boxCopy.X},{boxCopy.Y} (expected 200,50: guide X, Title top Y)");

    // Alignment commands: with two elements, align-left pulls the Title (X=200) to
    // the Barcode's left edge (X=50); distribution stays disabled below three.
    d.Selection.SetMany([d.Document.Elements[1], d.Document.Elements[2]]);
    Console.WriteLine($"distribute gating with 2 selected: {d.DistributeHorizontalCommand.CanExecute(null)} (expected False)");
    d.AlignLeftCommand.Execute(null);
    Console.WriteLine($"align left: Title X={d.Document.Elements[1].X} (expected 50)");
    d.Selection.SetMany([d.Document.Elements[1], d.Document.Elements[2], d.Document.Elements[4]]);
    Console.WriteLine($"distribute gating with 3 selected: {d.DistributeHorizontalCommand.CanExecute(null)} (expected True)");

    // Armed tool highlight in the left bar, captured while the Box tool is armed.
    d.AddBoxCommand.Execute(null);
    Pump(150);
    Capture("designer-toolbar-armed.png");
    d.CancelInsert();
    d.Selection.Set(d.Document.Elements[2]);
    Pump(150);
}

Capture($"{mode}.png");

void Pump(int ms)
{
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < ms)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(50);
    }
}

void Capture(string name)
{
    var frame = window.CaptureRenderedFrame();
    if (frame is null)
    {
        Console.WriteLine($"{name}: CaptureRenderedFrame returned null");
        return;
    }

    string path = Path.Combine(AppContext.BaseDirectory, name);
    frame.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    Console.WriteLine(path);
}
