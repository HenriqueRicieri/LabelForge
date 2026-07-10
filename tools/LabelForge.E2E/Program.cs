using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
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

    d.UndoCommand.Execute(null);
    Console.WriteLine($"undo panel edits: module={((LabelForge.Core.Model.BarcodeElement)d.Document.Elements[2]).ModuleWidthDots} (expected 3)");

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

    // Single selection for the capture: shows the 8 handles + rotation handle.
    d.Selection.Set(d.Document.Elements[2]);
}

var sw = Stopwatch.StartNew();
while (sw.ElapsedMilliseconds < 2500)
{
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Thread.Sleep(50);
}

string outPath = Path.Combine(AppContext.BaseDirectory, $"{mode}.png");
var frame = window.CaptureRenderedFrame();
if (frame is null)
{
    Console.WriteLine("CaptureRenderedFrame returned null");
    return;
}

frame.Save(outPath);
Console.WriteLine(outPath);
