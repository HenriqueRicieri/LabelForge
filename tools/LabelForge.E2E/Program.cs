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

// Media presets, field catalogs and crash snapshots all live per machine. Point every
// one of them at scratch locations, so a harness run never touches what the person using
// the app has saved.
string presetsPath = Path.Combine(AppContext.BaseDirectory, "e2e-user-media.json");
string catalogsPath = Path.Combine(AppContext.BaseDirectory, "e2e-field-catalogs.json");
string recoveryDir = Path.Combine(AppContext.BaseDirectory, "e2e-recovery");
File.Delete(presetsPath);
File.Delete(catalogsPath);
if (Directory.Exists(recoveryDir))
{
    Directory.Delete(recoveryDir, recursive: true);
}

var vm = new MainViewModel(
    new LabelForge.Core.Media.UserMediaStore(presetsPath),
    new LabelForge.Core.Fields.FieldCatalogStore(catalogsPath),
    new LabelForge.Core.Io.RecoveryStore(recoveryDir, "e2e"));
var window = new MainWindow { DataContext = vm };
window.Show();

var tabs = window.FindControl<TabControl>("MainTabs")!;
string mode = args.Length > 0 ? args[0] : "designer";
if (mode == "viewer")
{
    tabs.SelectedIndex = 1;
    if (args.Length > 1)
    {
        // Same reader the file picker uses, so the harness exercises encoding
        // detection rather than a lenient decode that only the harness would do.
        var read = LabelForge.Core.Io.ZplTextFile.Read(File.ReadAllBytes(args[1]));
        Console.WriteLine(
            $"opened as {read.EncodingName}, inferred={read.Recovered}, "
            + $"replacement chars={read.Text.Contains('�')} (expected False)");
        vm.Viewer.LoadZpl(
            read.Text,
            read.Recovered ? $"Not valid UTF-8; read as {read.EncodingName}. Saving writes UTF-8." : "");
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

    // Media catalog: applying a stock sets both dimensions as ONE undo step, and a
    // manual size edit clears the picked media so the field never lies.
    var media = LabelForge.Core.Media.StockCatalog.Search("3007301-T")[0];
    d.SelectedMedia = media;
    Console.WriteLine(FormattableString.Invariant(
        $"media apply: {d.WidthMm}x{d.HeightMm} mm (expected 75.4x101.6)"));
    d.UndoCommand.Execute(null);
    Console.WriteLine(FormattableString.Invariant(
        $"media undo: {d.WidthMm}x{d.HeightMm} mm (expected 100x60, one step)"));
    d.SelectedMedia = media;
    d.WidthMm = 80;
    Console.WriteLine($"manual edit clears media: {(d.SelectedMedia is null ? "null" : "still set")} (expected null)");
    d.WidthMm = 100;

    // User media presets: save the current size under a name, find it in the same
    // picker as the Zebra catalog, apply it, and remove it again.
    d.WidthMm = 50.8m;
    d.HeightMm = 30m;
    d.NewMediaName = "Etiqueta Filial";
    d.NewMediaMaterial = "Couche";
    Console.WriteLine($"preset size preview: '{d.NewMediaSizeText}' (expected 50.8mm x 30mm)");
    d.SaveUserMediaCommand.Execute(null);
    Console.WriteLine(
        $"save preset: {d.UserMedia.Count} saved, name cleared={d.NewMediaName.Length == 0}, "
        + $"display='{(d.UserMedia.Count > 0 ? d.UserMedia[0].Display : "none")}' "
        + "(expected 1/True/Etiqueta Filial - Couche (50.8mm x 30mm) - my media)");
    Console.WriteLine(
        $"preset leads the picker: {d.MediaCatalog.Count > 0 && d.MediaCatalog[0].IsUserDefined}, "
        + $"entries={d.MediaCatalog.Count} (expected True/798)");
    Console.WriteLine(
        $"preset survives a reload: {new LabelForge.Core.Media.UserMediaStore(presetsPath).Load().Count} "
        + "on disk (expected 1)");

    d.WidthMm = 100m;
    d.HeightMm = 60m;
    d.SelectedMedia = d.MediaCatalog[0];
    Console.WriteLine(FormattableString.Invariant(
        $"apply preset: {d.WidthMm}x{d.HeightMm} mm (expected 50.8x30)"));

    d.UserMedia[0].RemoveCommand.Execute(null);
    Console.WriteLine(
        $"remove preset: {d.UserMedia.Count} saved, picker back to {d.MediaCatalog.Count}, "
        + $"selection cleared={d.SelectedMedia is null} (expected 0/797/True)");
    d.WidthMm = 100m;
    d.HeightMm = 60m;

    // Corner radius: a picked media brings its own die-cut radius onto the document.
    var roundedMedia = LabelForge.Core.Media.StockCatalog.Search("02D102102400K")[0];
    d.SelectedMedia = roundedMedia;
    Console.WriteLine(FormattableString.Invariant(
        $"media brings its die-cut radius: {d.CornerRadiusMm} mm (catalog says {roundedMedia.RadiusMm}), {d.Document.CornerRadiusDots} dots"));
    d.SelectedMedia = null;
    d.WidthMm = 100m;
    d.HeightMm = 60m;
    d.CornerRadiusMm = 0m;

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

    // Newer features: Data Matrix insert, template variables, job settings.
    int beforeDm = d.Document.Elements.Count;
    d.AddDataMatrixCommand.Execute(null);
    d.PlaceAt(100, 300);
    Console.WriteLine(
        $"datamatrix add: {d.Document.Elements.Count - beforeDm} added, "
        + $"selected type ok={d.SelectedElement is LabelForge.Core.Model.DataMatrixElement} (expected 1/True)");

    // PDF417: inserted from the toolbar, sized by its column count, and drawn by the
    // offline renderer (the canvas underlay is the render of this ZPL).
    int beforePdf = d.Document.Elements.Count;
    d.AddPdf417Command.Execute(null);
    d.PlaceAt(360, 300);
    Pump(700);
    Console.WriteLine(
        $"pdf417 add: {d.Document.Elements.Count - beforePdf} added, "
        + $"selected type ok={d.SelectedElement is LabelForge.Core.Model.Pdf417Element} (expected 1/True)");
    Console.WriteLine($"pdf417 in ZPL: {d.GeneratedZpl.Contains("^B7N,8,2,5,,N")} (expected True)");

    if (d.SelectionProperties is Pdf417PropertiesViewModel pdf)
    {
        Console.WriteLine($"pdf417 shape: '{pdf.ShapeInfo}', warning='{pdf.Warning}' (expect no warning)");

        // Automatic columns hand the shape to the printer, so the panel has to say the
        // preview is one plausible layout rather than the one that will print.
        pdf.Columns = 0;
        Pump(700);
        Console.WriteLine($"pdf417 automatic: '{pdf.ShapeInfo}', warns={pdf.HasWarning} (expected True)");
        Console.WriteLine($"pdf417 automatic in ZPL: {d.GeneratedZpl.Contains("^B7N,8,2,,,N")} (expected True)");
        Capture("designer-pdf417.png");

        pdf.Columns = 5;
        Pump(500);
    }
    else
    {
        Console.WriteLine("pdf417 panel: no Pdf417PropertiesViewModel (expected one)");
    }

    d.Document.Elements.Add(new LabelForge.Core.Model.TextElement
    {
        X = 20, Y = 20, Text = "Lot ##LOTE##", FontHeightDots = 30,
    });
    d.PrintCopies = 3;
    d.NotifyDocumentEdited();
    Pump(700);
    Console.WriteLine(
        $"variables panel: {d.Variables.Count} found, "
        + $"first='{(d.Variables.Count > 0 ? d.Variables[0].Name : "none")}' (expected 1/LOTE)");
    Console.WriteLine($"job settings in ZPL: {d.GeneratedZpl.Contains("^PQ3")} (expected True)");
    Console.WriteLine($"markers stay in export: {d.GeneratedZpl.Contains("##LOTE##")} (expected True)");

    // Counters: switching LOTE to a counter hands the run to the printer (^SN), and
    // turning that off turns the same run into one block per copy.
    var lote = d.Variables[0];
    lote.SelectedKind = VariableKindOption.All[1];
    lote.CounterStart = 41;
    lote.CounterPadding = 4;
    Pump(700);
    Console.WriteLine(
        $"counter kind: {lote.Kind}, preview='{lote.PreviewValue}' "
        + "(expected Counter / 0041, 0042, 0043, ...)");
    Console.WriteLine($"printer counter in ZPL: {d.GeneratedZpl.Contains("^SNLot 0041,1,Y")} (expected True)");

    var printerJob = LabelForge.Core.Zpl.PrintJob.Build(d.Document, DateTime.Now);
    Console.WriteLine(
        $"printer-counted job: {Blocks(printerJob.Zpl)} block(s), {printerJob.Labels} labels, "
        + $"byPrinter={printerJob.CountedByPrinter} (expected 1/3/True)");

    lote.UsePrinterCounter = false;
    Pump(700);
    var pcJob = LabelForge.Core.Zpl.PrintJob.Build(d.Document, DateTime.Now);
    Console.WriteLine(
        $"pc-counted job: {Blocks(pcJob.Zpl)} block(s), {pcJob.Labels} labels, "
        + $"0043 present={pcJob.Zpl.Contains("Lot 0043")} (expected 3/3/True)");

    d.UndoCommand.Execute(null);
    Pump(300);
    Console.WriteLine(
        $"undo the printer-counter toggle: byPrinter={d.Variables[0].UsePrinterCounter} (expected True)");

    // Dates: the printer's own clock becomes ^FC placeholders, and a format it cannot
    // express falls back to this PC's clock with a stated reason.
    d.Document.Elements.Add(new LabelForge.Core.Model.TextElement
    {
        X = 20, Y = 60, Text = "##EMISSAO##", FontHeightDots = 24,
    });
    d.NotifyDocumentEdited();
    Pump(700);
    var emissao = d.Variables.First(v => v.Name == "EMISSAO");
    emissao.SelectedKind = VariableKindOption.All[2];
    emissao.UsePrinterClock = true;
    Pump(700);
    Console.WriteLine(
        $"printer clock in ZPL: {d.GeneratedZpl.Contains("^FC%^FD%d/%m/%Y^FS")} (expected True), "
        + $"warning='{d.VariableWarning}' (expect empty)");

    // Capture the Variables panel: clearing the selection collapses the per-element
    // editor so the counter and date rows are the ones on screen.
    d.Selection.Clear();
    Pump(500);
    Capture("designer-variables.png");

    emissao.ClockFormat = "dd MMM yyyy";
    Pump(700);
    Console.WriteLine($"clock fallback: warning='{d.VariableWarning}' (expect PC clock reason)");
    emissao.SelectedKind = VariableKindOption.All[0];
    Pump(400);

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

    // A rounded label: the canvas shows the die-cut shape while the ZPL, which has no
    // notion of the label outline, stays exactly as it was.
    // Let the pending render from the alignment above land first, or the comparison
    // would be against a stale ZPL and blame the radius for someone else's edit.
    Pump(600);
    string zplBeforeRadius = d.GeneratedZpl;
    d.CornerRadiusMm = 6m;
    Pump(600);
    Console.WriteLine(
        $"radius on the canvas: {d.Document.CornerRadiusDots} dots (expected 48 at 8 dpmm), "
        + $"ZPL unchanged={d.GeneratedZpl == zplBeforeRadius} (expected True)");
    Capture("designer-rounded.png");
    d.CornerRadiusMm = 0m;
    Pump(300);

    // Continuous stock: no die cut, so the label is exactly as long as its content and
    // the height box becomes a readout rather than something to type into.
    decimal dieCutHeight = d.HeightMm;
    d.IsContinuous = true;
    Pump(600);
    Console.WriteLine(
        $"continuous: height {d.HeightMm} mm, editable={d.HasFixedLength} "
        + $"(expected the content length, False)");
    Console.WriteLine(
        $"continuous in ZPL: ^MNN={d.GeneratedZpl.Contains("^MNN")} "
        + $"^LL{d.Document.HeightDots} present={d.GeneratedZpl.Contains("^LL" + d.Document.HeightDots)} "
        + "(expected True/True)");
    Console.WriteLine(
        $"no rounded corners on a roll: {d.Document.EffectiveCornerRadiusMm} mm (expected 0)");
    Capture("designer-continuous.png");

    // Pushing the bottom-most element down makes the roll longer, which is the whole
    // feature. Measured from the ink, not from the origin: the lowest origin is not
    // necessarily the lowest footprint.
    var bounds = new LabelForge.Core.Model.ElementBoundsCalculator();
    var lowest = d.Document.Elements
        .OrderByDescending(e => bounds.GetBounds(e).Y + bounds.GetBounds(e).Height).First();
    decimal lengthBefore = d.HeightMm;
    lowest.Y += 200;
    d.NotifyDocumentEdited();
    Pump(600);
    Console.WriteLine(
        $"length follows content: {lengthBefore} -> {d.HeightMm} mm "
        + "(expected +25 mm, the 200 dots it moved at 8 dpmm)");

    d.IsContinuous = false;
    Pump(400);
    Console.WriteLine(
        $"back to die cut: height {d.HeightMm} mm (expected {dieCutHeight}, the stored one)");
    lowest.Y -= 200;
    d.NotifyDocumentEdited();
    Pump(300);

    // My media flyout, captured with a preset saved so the list is not empty.
    d.NewMediaName = "Etiqueta Filial";
    d.NewMediaMaterial = "Couche";
    d.SaveUserMediaCommand.Execute(null);
    var myMediaButton = window.GetVisualDescendants().OfType<Button>()
        .First(b => b.Content as string == "My media...");
    myMediaButton.Flyout?.ShowAt(myMediaButton);
    Pump(500);
    Capture("designer-my-media.png");
    myMediaButton.Flyout?.Hide();
    d.UserMedia[0].RemoveCommand.Execute(null);
    Pump(200);

    // Armed tool highlight in the left bar, captured while the Box tool is armed.
    d.AddBoxCommand.Execute(null);
    Pump(150);
    Capture("designer-toolbar-armed.png");
    d.CancelInsert();
    d.Selection.Set(d.Document.Elements[2]);
    Pump(150);

    // Graphic import: pull the logos out of an existing label and check they come back
    // as ordinary images that generate, share one download, and survive a save.
    string graphicSource = FindGraphicSource();
    d.NewDocumentCommand.Execute(null);
    d.WidthMm = 100m;
    d.HeightMm = 150m;
    Pump(200);
    d.ImportGraphicsFromZpl(
        LabelForge.Core.Io.ZplTextFile.ReadFile(graphicSource).Text,
        Path.GetFileName(graphicSource));
    Pump(700);
    Console.WriteLine(
        $"import graphics from {Path.GetFileName(graphicSource)}: {d.Document.Elements.Count} elements, "
        + $"all images={d.Document.Elements.All(e => e is LabelForge.Core.Model.ImageElement)}, "
        + $"status='{d.StatusText}'");

    var firstGraphic = (LabelForge.Core.Model.ImageElement)d.Document.Elements[0];
    Console.WriteLine(
        $"first graphic: '{firstGraphic.Name}' {firstGraphic.WidthDots}x{firstGraphic.HeightDots} dots "
        + $"at {firstGraphic.X},{firstGraphic.Y}, {firstGraphic.ImageData.Length} PNG bytes");
    Console.WriteLine(
        $"import is one undo step: CanUndo={d.CanUndo}, "
        + $"undo empties it={UndoLeavesNothing()} (expected True/True)");

    // Place the same stamp a second time: that one should be downloaded once and
    // recalled twice, while the graphics used once each stay inline.
    var twin = (LabelForge.Core.Model.ImageElement)LabelForge.Core.Io.LabelDocumentJson
        .DeserializeElements(LabelForge.Core.Io.LabelDocumentJson.SerializeElements([firstGraphic]))[0];
    twin.Id = Guid.NewGuid();
    twin.Y = firstGraphic.Y + firstGraphic.HeightDots + 20;
    d.Document.Elements.Add(twin);
    d.NotifyDocumentEdited();
    Pump(900);
    Console.WriteLine(
        $"repeated stamp: ~DG count={Count(d.GeneratedZpl, "~DG")}, "
        + $"^XG count={Count(d.GeneratedZpl, "^XG")}, inline ^GF count={Count(d.GeneratedZpl, "^GFA,")} "
        + "(expected 1/2/2: the doubled stamp shares, the two single ones stay inline)");

    var reimported = LabelForge.Core.Io.LabelDocumentJson.Deserialize(d.SerializeDocument());
    Console.WriteLine(
        $"imported graphics survive a save: {reimported.Elements.Count} elements, "
        + $"bytes kept={((LabelForge.Core.Model.ImageElement)reimported.Elements[0]).ImageData.Length}");

    d.Selection.Clear();
    Pump(400);
    Capture("designer-imported-graphics.png");

    // Whole-label import: read a real ZPL file back into the model, then confirm the
    // round trip by regenerating it and comparing against a re-import of that output.
    d.ImportZplDocument(
        LabelForge.Core.Io.ZplTextFile.ReadFile(graphicSource).Text,
        Path.GetFileName(graphicSource));
    Pump(900);
    Console.WriteLine(
        $"import label from {Path.GetFileName(graphicSource)}: {d.Document.Elements.Count} elements "
        + $"[{string.Join(" ", d.Document.Elements.GroupBy(e => e.GetType().Name).Select(g => $"{g.Key}:{g.Count()}"))}]");
    Console.WriteLine($"import status: '{d.StatusText}'");
    Console.WriteLine(
        $"no file path after a ZPL import: {d.CurrentFilePath is null} (expected True, "
        + "saving must ask where the .lfl goes)");

    string generated = new LabelForge.Core.Zpl.ZplGenerator().Generate(d.Document);
    var again = LabelForge.Core.Io.ZplDocumentImport.FromZpl(generated, d.Document.Dpmm);
    Console.WriteLine(
        $"regenerate and re-import is stable: "
        + $"{generated == new LabelForge.Core.Zpl.ZplGenerator().Generate(again.Document)} (expected True)");

    d.Selection.Clear();
    Pump(500);
    Capture("designer-imported-label.png");

    // Element flags: a locked element resists canvas gestures, a "do not print" one stays
    // on the canvas with its own outline and leaves the exported ZPL.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    d.Document.Elements.Add(new LabelForge.Core.Model.TextElement
    {
        X = 60, Y = 60, Text = "prints normally", FontHeightDots = 40,
    });
    d.Document.Elements.Add(new LabelForge.Core.Model.TextElement
    {
        X = 60, Y = 160, Text = "internal note", FontHeightDots = 40, DoNotPrint = true,
    });
    d.Document.Elements.Add(new LabelForge.Core.Model.BoxElement
    {
        X = 50, Y = 40, WidthDots = 500, HeightDots = 220, ThicknessDots = 3, IsLocked = true,
    });
    d.NotifyDocumentEdited();
    Pump(900);
    Console.WriteLine(
        $"do-not-print stays off the ZPL: {!d.GeneratedZpl.Contains("internal note")} "
        + $"and on the canvas: {d.PlacementWarning.Contains("set not to print")} (expected True/True)");

    var lockedBox = d.Document.Elements[2];
    int lockedX = lockedBox.X;
    d.Selection.SetMany([d.Document.Elements[0], lockedBox]);
    d.AlignLeftCommand.Execute(null);
    Console.WriteLine(
        $"locked element resists alignment: {lockedBox.X == lockedX} (expected True, still at {lockedX})");

    d.Selection.Set(d.Document.Elements[1]);
    Pump(500);
    Capture("designer-element-flags.png");
    d.Selection.Clear();
    Pump(200);

    // Quiet zone: the blank a barcode needs to scan. A neighbour that never touches the
    // ink can still sit in it, which is exactly the mistake that looks like tidy layout.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    var scanned = new LabelForge.Core.Model.BarcodeElement
    {
        X = 300, Y = 120, Data = "LF-000123", HeightDots = 120, ModuleWidthDots = 3,
    };
    var neighbour = new LabelForge.Core.Model.BoxElement
    {
        X = 120, Y = 120, WidthDots = 160, HeightDots = 120, ThicknessDots = 3,
    };
    d.Document.Elements.Add(scanned);
    d.Document.Elements.Add(neighbour);
    d.Selection.Set(scanned);
    d.NotifyDocumentEdited();
    Pump(900);
    Console.WriteLine(
        $"quiet zone crowded: '{d.ValidationWarning}' (expect the box named as crowding it)");
    Capture("designer-quiet-zone.png");

    neighbour.X = 60;
    d.NotifyDocumentEdited();
    Pump(900);
    Console.WriteLine(
        $"quiet zone cleared by moving 60 dots: '{d.ValidationWarning}' (expect empty)");

    scanned.X = 0;
    d.NotifyDocumentEdited();
    Pump(900);
    Console.WriteLine(
        $"flush with the stock edge: {d.ValidationWarning.Contains("runs off the label")} (expected True)");

    d.CheckQuietZones = false;
    Pump(900);
    Console.WriteLine(
        $"check turned off: '{d.ValidationWarning}' (expect empty), "
        + $"ZPL untouched={d.GeneratedZpl.Contains("^FO0,120")} (expected True)");
    d.CheckQuietZones = true;
    d.Selection.Clear();
    Pump(200);

    // Field catalog: import a field list, bind the label to it, and check that a marker
    // the catalog does not list is named rather than printed silently.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    d.NewCatalogName = "Etiqueta externa (caixaria)";
    d.ImportFieldCatalog(
        "- ##CODIGO_BARRAS##\t Tipo: String\t Origem: tbVolume.codigo\r\n"
        + "- ##DATA_PRODUCAO##\t Tipo: DateTime\r\n"
        + "- ##TABELA_NUTRICIONAL##\t Tipo: List<ProdutoTabelaNutricionalPrint>\r\n",
        "TodasMarkupsVolumePrint");
    Pump(400);
    Console.WriteLine(
        $"catalog import: '{d.SelectedFieldCatalog?.Name}' with "
        + $"{d.SelectedFieldCatalog?.Fields.Count} fields, bound={d.Document.FieldCatalog.Length > 0} "
        + "(expected the typed name, 3, True)");
    Console.WriteLine(
        $"completion offers: {string.Join(" ", d.FieldSuggestions)} (expected full markers)");

    var good = new LabelForge.Core.Model.TextElement
    {
        X = 40, Y = 40, Text = "##CODIGO_BARRAS##", FontHeightDots = 30,
    };
    var typo = new LabelForge.Core.Model.TextElement
    {
        X = 40, Y = 100, Text = "##CODIGO_BARAS##", FontHeightDots = 30,
    };
    d.Document.Elements.Add(good);
    d.Document.Elements.Add(typo);
    d.NotifyDocumentEdited();
    Pump(900);
    Console.WriteLine($"unknown marker: '{d.UnknownFieldWarning}' (expect the typo and a suggestion)");
    d.Selection.Set(typo);
    Pump(400);
    Capture("designer-field-catalog.png");

    // A script imported beside the field list adds its calls without wiping the fields,
    // and completion offers them ready to paste.
    d.NewCatalogName = "Etiqueta externa (caixaria)";
    d.ImportFieldCatalog(
        """
        public class Abate
        {
            public string maturidade(string COD_MATURIDADE)
            {
                return "M";
            }
        }
        """,
        "Abate");
    Pump(400);
    Console.WriteLine(
        $"script import: '{d.SelectedFieldCatalog}' "
        + "(expected 3 fields kept, 1 function added)");
    Console.WriteLine(
        $"call offered: {d.FieldSuggestions.Contains("##@Abate.maturidade(COD_MATURIDADE)##")} (expected True)");

    // A call is a directive rather than a variable, so it is never checked against the
    // field list; nor is a directive the catalog could not possibly list.
    typo.Text = "##@Abate.maturidade(COD_MATURIDADE)## ##@SET_PRINTER(2)##";
    d.NotifyDocumentEdited();
    Pump(900);
    Console.WriteLine($"calls and directives: '{d.UnknownFieldWarning}' (expect empty)");

    // An indexed list field is addressed with [n].Member and must not be flagged.
    typo.Text = "##TABELA_NUTRICIONAL[2].QUANTIDADE##";
    d.NotifyDocumentEdited();
    Pump(900);
    Console.WriteLine($"indexed list field: '{d.UnknownFieldWarning}' (expect empty)");

    // Unbinding the catalog turns the check off; the ZPL never had anything to do with it.
    string zplWithCatalog = d.GeneratedZpl;
    d.SelectedFieldCatalog = null;
    Pump(900);
    Console.WriteLine(
        $"no catalog: '{d.UnknownFieldWarning}' (expect empty), "
        + $"ZPL unchanged={d.GeneratedZpl == zplWithCatalog} (expected True)");

    // GS1-128: the payload is shown broken into its identifiers, and a variable-length
    // field with nothing after it to end it is named, because that does not fail to scan,
    // it scans as one value with the wrong contents.
    d.NewDocumentCommand.Execute(null);
    d.WidthMm = 150m;
    Pump(200);
    var gs1 = new LabelForge.Core.Model.BarcodeElement
    {
        X = 40, Y = 60, Data = ">;>801078912345678953102001234",
        HeightDots = 80, ModuleWidthDots = 2, PrintInterpretationLine = false,
    };
    d.Document.Elements.Add(gs1);
    d.Selection.Set(gs1);
    d.NotifyDocumentEdited();
    Pump(900);
    var gs1Panel = (BarcodePropertiesViewModel)d.SelectionProperties!;
    Console.WriteLine(
        $"gs1 breakdown: '{gs1Panel.Gs1Breakdown}' (expected (01) and (3102) named)");
    Console.WriteLine(
        $"gs1 width honours subset C: {new LabelForge.Core.Model.ElementBoundsCalculator().GetBounds(gs1).Width} dots "
        + "(expected 378, not the 708 a character count would give)");
    Capture("designer-gs1.png");

    gs1Panel.Data = ">;>810LOTE42>:01078912345678953102001234";
    Pump(900);
    Console.WriteLine($"gs1 problem: '{gs1Panel.Gs1Warning}' (expect a separator warning)");

    gs1Panel.Data = "PLAIN12345";
    Pump(900);
    Console.WriteLine(
        $"not gs1: shown={gs1Panel.IsGs1} (expected False), warning='{d.ValidationWarning}' (expect empty)");
    d.Selection.Clear();
    Pump(200);

    // Render caching. Observable without a test hook: a skipped render leaves the very
    // bitmap that is already on screen, so the reference is unchanged.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    var cached = new LabelForge.Core.Model.TextElement
    {
        X = 60, Y = 60, Text = "cache me", FontHeightDots = 40,
    };
    d.Document.Elements.Add(cached);
    d.NotifyDocumentEdited();
    Pump(900);
    var firstBitmap = d.Underlay;

    // A name and a lock change the document but nothing the renderer is given.
    d.Selection.Set(cached);
    Pump(300);
    d.SelectionProperties!.Name = "named, not redrawn";
    Pump(900);
    Console.WriteLine(
        $"naming skips the render: {ReferenceEquals(d.Underlay, firstBitmap)} (expected True)");

    cached.IsLocked = true;
    d.NotifyDocumentEdited();
    Pump(900);
    Console.WriteLine(
        $"locking skips the render: {ReferenceEquals(d.Underlay, firstBitmap)} (expected True)");

    // Moving it does change what the renderer is given, so the bitmap must be new.
    cached.IsLocked = false;
    cached.X = 200;
    d.NotifyDocumentEdited();
    Pump(900);
    Console.WriteLine(
        $"moving redraws: {!ReferenceEquals(d.Underlay, firstBitmap) && d.Underlay is not null} "
        + "(expected True)");
    d.Selection.Clear();
    Pump(200);

    // Zoom to selection: framing one field out of a dense label, which is the other half
    // of being able to find it in a list.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    var far = new LabelForge.Core.Model.TextElement
    {
        X = 600, Y = 380, Text = "over here", FontHeightDots = 24,
    };
    d.Document.Elements.Add(far);
    d.NotifyDocumentEdited();
    Pump(700);
    canvas.ResetView();
    Pump(300);
    double fitted = canvas.GetZoom();
    d.Selection.Set(far);
    canvas.ZoomToSelection();
    Pump(400);
    double framed = canvas.GetZoom();
    Console.WriteLine(
        FormattableString.Invariant(
            $"zoom to selection: {fitted:0.00}x -> {framed:0.00}x (expected closer in)"));

    // The element it framed has to be on screen afterwards, which is the entire point.
    var onScreen = canvas.DotsToView(far.X, far.Y);
    bool inView = onScreen.X > 0 && onScreen.X < canvas.Bounds.Width &&
                  onScreen.Y > 0 && onScreen.Y < canvas.Bounds.Height;
    Console.WriteLine($"framed element is in view: {inView} (expected True)");

    // With nothing selected it frames the label rather than doing nothing.
    d.Selection.Clear();
    canvas.ZoomToSelection();
    Pump(300);
    Console.WriteLine(
        FormattableString.Invariant(
            $"nothing selected frames the label: {canvas.GetZoom():0.00}x (expected a sane zoom)"));
    Capture("designer-zoom-selection.png");
    canvas.ResetView();
    Pump(200);

    // The keyboard and mouse reference. Shown in its own window and captured there, so
    // what is checked is the rendered list rather than the view model behind it.
    var shortcuts = new LabelForge.App.Views.ShortcutsWindow();
    shortcuts.Show();
    Pump(600);
    var shortcutModel = new LabelForge.App.ViewModels.ShortcutsViewModel();
    Console.WriteLine(
        $"shortcut reference: {shortcutModel.Groups.Count} groups, "
        + $"{shortcutModel.Groups.Sum(g => g.Entries.Count)} entries "
        + "(expected every group populated)");
    Console.WriteLine(
        $"documents the new one: "
        + $"{shortcutModel.Groups.SelectMany(g => g.Entries).Any(x => x.Keys.Contains("Shift + 0"))} "
        + "(expected True)");
    var shortcutFrame = shortcuts.CaptureRenderedFrame();
    if (shortcutFrame is not null)
    {
        string shortcutPath = Path.Combine(AppContext.BaseDirectory, "designer-shortcuts.png");
        shortcutFrame.Save(shortcutPath, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Console.WriteLine(shortcutPath);
    }

    shortcuts.Close();
    Pump(300);

    // Right-click menus. Two of them, because the two situations are different questions:
    // over an element it asks about that element, over bare stock it asks what to put there.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    var target = new LabelForge.Core.Model.TextElement
    {
        X = 120, Y = 120, Text = "right click me", FontHeightDots = 40,
    };
    d.Document.Elements.Add(target);
    d.Selection.Clear();
    d.NotifyDocumentEdited();
    Pump(700);

    // Over an element that is not selected: the menu selects it first, so it is about
    // what was pointed at rather than about whatever was selected before.
    Avalonia.Point overElement =
        canvas.TranslatePoint(canvas.DotsToView(140, 140), window)!.Value;
    window.MouseDown(overElement, MouseButton.Right);
    window.MouseUp(overElement, MouseButton.Right);
    Pump(400);
    Console.WriteLine(
        $"right click selects what it points at: {ReferenceEquals(d.SelectedElement, target)} "
        + "(expected True)");
    Capture("designer-context-element.png");
    window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
    Pump(300);

    // Over bare stock: no element, and the selection is left alone to be cleared by the
    // menu's own actions rather than by opening it.
    d.CopyCommand.Execute(null);
    Avalonia.Point overStock = canvas.TranslatePoint(canvas.DotsToView(500, 350), window)!.Value;
    window.MouseDown(overStock, MouseButton.Right);
    window.MouseUp(overStock, MouseButton.Right);
    Pump(400);
    Capture("designer-context-canvas.png");
    window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
    Pump(300);

    // Paste at the pointer puts the copy where the click landed, not where the cascade
    // would have taken it.
    int beforePaste = d.Document.Elements.Count;
    d.PasteAt(500, 350);
    Pump(400);
    Console.WriteLine(
        $"paste here: {d.Document.Elements.Count - beforePaste} added at "
        + $"{d.SelectedElement!.X},{d.SelectedElement!.Y} (expected 1 at 500,350)");

    // Insert from the menu places in one step rather than arming and asking again.
    int beforeInsert = d.Document.Elements.Count;
    d.InsertAt(d.AddBoxCommand, 300, 250);
    Pump(400);
    Console.WriteLine(
        $"insert here: {d.Document.Elements.Count - beforeInsert} added at "
        + $"{d.SelectedElement!.X},{d.SelectedElement!.Y}, still placing={d.IsPlacing} "
        + "(expected 1 at 300,250, False)");

    d.SelectAllCommand.Execute(null);
    Console.WriteLine(
        $"select all: {d.SelectionCount} of {d.Document.Elements.Count} (expected all)");
    d.Selection.Clear();
    Pump(200);

    // Element outline: a dense label read as a list, which is the only practical way to
    // pick one field out of a stack of overlapping ones.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    d.LoadDocument(
        LabelForge.Core.Io.ZplDocumentImport.FromZpl(
            LabelForge.Core.Io.ZplTextFile.Read(File.ReadAllBytes(graphicSource)).Text,
            d.Document.Dpmm).Document,
        null);
    Pump(900);
    Console.WriteLine(
        $"outline: '{d.OutlineHeader}' for {d.Document.Elements.Count} elements "
        + "(expected the same count)");
    Console.WriteLine(
        $"named by content: '{(d.Outline.Count > 0 ? d.Outline[0].Display : "none")}' "
        + "(expect a type and a glimpse of its text)");

    // Front to back, which is the order a click meets them.
    var front = d.Document.Elements.OrderByDescending(e => e.ZOrder).First();
    Console.WriteLine(
        $"front first: {ReferenceEquals(d.Outline[0].Element, front)} (expected True)");

    // Picking in the list selects on the canvas, and selecting on the canvas highlights
    // the row: the same act seen from two places.
    var row = d.Outline[3];
    d.SelectedOutlineRow = row;
    Pump(300);
    Console.WriteLine(
        $"list picks the element: {ReferenceEquals(d.SelectedElement, row.Element)} (expected True)");
    d.Selection.Set(d.Outline[7].Element);
    Pump(300);
    Console.WriteLine(
        $"canvas highlights the row: {ReferenceEquals(d.SelectedOutlineRow, d.Outline[7])} (expected True)");

    // A name typed in the panel is what the row reads as.
    d.Selection.Set(d.Outline[3].Element);
    Pump(300);
    d.SelectionProperties!.Name = "Peso liquido";
    d.NotifyDocumentEdited();
    Pump(900);
    Console.WriteLine(
        $"named row: '{d.Outline.First(r => r.Element == row.Element).Display}' (expected Peso liquido)");

    // Open the list for the capture; collapsed by default so the panel stays quiet on a
    // label small enough not to need it.
    var outlineExpander = window.GetVisualDescendants().OfType<Expander>()
        .FirstOrDefault(x => (x.Header as string)?.StartsWith("Elements", StringComparison.Ordinal) == true);
    if (outlineExpander is not null)
    {
        outlineExpander.IsExpanded = true;
    }

    Pump(500);
    Capture("designer-outline.png");

    // Hiding from the list takes it off the canvas as well.
    var hidden = d.Outline[0];
    hidden.IsVisible = false;
    Pump(900);
    Console.WriteLine(
        $"hidden from the list: element IsVisible={hidden.Element.IsVisible} (expected False), "
        + $"one undo step={d.CanUndo} (expected True)");
    hidden.IsVisible = true;
    d.Selection.Clear();
    Pump(200);

    // Print-job export: what a print sends, which is not what the ZPL pane shows once a
    // counter this machine expands is in play.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    d.Document.Elements.Add(new LabelForge.Core.Model.TextElement
    {
        X = 30, Y = 30, Text = "Lote ##SERIE##", FontHeightDots = 30,
    });
    d.Document.Variables["SERIE"] = new LabelForge.Core.Model.VariableDefinition
    {
        Kind = LabelForge.Core.Model.VariableKind.Counter,
        CounterStart = 41,
        CounterPadding = 4,
        UsePrinterCounter = false,
    };
    d.PrintCopies = 3;
    d.NotifyDocumentEdited();
    Pump(900);
    var exported = d.BuildPrintJob();
    Console.WriteLine(
        $"print job export: {d.DescribeJob(exported)} (expected 3 labels in 3 blocks)");
    Console.WriteLine(
        $"every copy numbered: {exported.Zpl.Contains("0041") && exported.Zpl.Contains("0043")} "
        + $"(expected True), pane shows one: {d.GeneratedZpl.Contains("0043")} (expected False)");

    // And the printer-counted form is one block plus a quantity, from the same builder.
    d.Document.Variables["SERIE"].UsePrinterCounter = true;
    d.NotifyDocumentEdited();
    Pump(900);
    var byPrinter = d.BuildPrintJob();
    Console.WriteLine(
        $"printer-counted export: {d.DescribeJob(byPrinter)} "
        + $"^PQ3={byPrinter.Zpl.Contains("^PQ3")} (expected 3 labels in one block, True)");

    // Crash recovery: the snapshot follows the edits, a real save clears it because the
    // work is safe elsewhere, and a snapshot left by a dead session is offered on start.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    d.Document.Elements.Add(new LabelForge.Core.Model.TextElement
    {
        X = 30, Y = 30, Text = "unsaved work", FontHeightDots = 30,
    });
    d.NotifyDocumentEdited();
    Pump(900);
    string snapshot = Path.Combine(recoveryDir, "e2e.recovery.json");
    Console.WriteLine(
        $"snapshot written: {File.Exists(snapshot)}, holds the edit="
        + $"{File.Exists(snapshot) && File.ReadAllText(snapshot).Contains("unsaved work")} "
        + "(expected True/True)");

    d.ClearRecovery();
    Console.WriteLine($"cleared after a real save: {!File.Exists(snapshot)} (expected True)");

    // What a dead session leaves behind: a snapshot with no lock beside it.
    d.NotifyDocumentEdited();
    Pump(900);
    string remnant = Path.Combine(recoveryDir, "crashed.recovery.json");
    File.Copy(snapshot, remnant, overwrite: true);

    var recovered = new MainViewModel(
        new LabelForge.Core.Media.UserMediaStore(presetsPath),
        new LabelForge.Core.Fields.FieldCatalogStore(catalogsPath),
        new LabelForge.Core.Io.RecoveryStore(recoveryDir, "second-start"));
    Console.WriteLine(
        $"offered on next start: {recovered.Designer.HasRecoveryOffer} (expected True), "
        + $"'{recovered.Designer.RecoveryOffer}'");
    recovered.Designer.RecoverDocumentCommand.Execute(null);
    Console.WriteLine(
        $"recovered: {recovered.Designer.Document.Elements.Count} element(s) "
        + $"(expected 1), offer gone={!recovered.Designer.HasRecoveryOffer} (expected True), "
        + $"remnant discarded={!File.Exists(remnant)} (expected True)");

    // A session that ends properly leaves nothing, so the next start stays quiet.
    d.ShutDown();
    var afterCleanExit = new MainViewModel(
        new LabelForge.Core.Media.UserMediaStore(presetsPath),
        new LabelForge.Core.Fields.FieldCatalogStore(catalogsPath),
        new LabelForge.Core.Io.RecoveryStore(recoveryDir, "third-start"));
    Console.WriteLine(
        $"quiet after a clean exit: {!afterCleanExit.Designer.HasRecoveryOffer} (expected True)");

    d.NewDocumentCommand.Execute(null);
    Pump(200);
    d.FieldCatalogs[0].RemoveCommand.Execute(null);
    Pump(300);
    Console.WriteLine($"catalog removed: {d.FieldCatalogs.Count} left (expected 0)");
    d.Selection.Clear();
    Pump(200);

    bool UndoLeavesNothing()
    {
        d.UndoCommand.Execute(null);
        bool empty = d.Document.Elements.Count == 0;
        d.RedoCommand.Execute(null);
        return empty;
    }
}

Capture($"{mode}.png");

int Blocks(string zpl) => zpl.Split("^XA", StringSplitOptions.RemoveEmptyEntries).Length;

int Count(string haystack, string needle) =>
    haystack.Split(needle, StringSplitOptions.None).Length - 1;

/// <summary>A label with downloaded graphics: the real corpus when it is present,
/// otherwise the committed fixture, so the harness runs on a clean clone too.</summary>
string FindGraphicSource()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        string corpus = Path.Combine(dir.FullName, "exemplos zpl", "440.zpl");
        if (File.Exists(corpus))
        {
            return corpus;
        }

        dir = dir.Parent;
    }

    return Path.Combine(AppContext.BaseDirectory, "Fixtures", "embedded-graphic-short-name.zpl");
}

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
