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

// The end-to-end harness. It drives the real windows through Avalonia's headless platform
// and prints one line per check, each carrying the value it found beside the value expected,
// so a run reads as a transcript rather than a pass count.
//
// How it is meant to be read:
//   - nothing in here grades itself. There is no assertion and no failing exit code: exit 0
//     means the run reached the end without throwing, which is worth knowing and is not the
//     same as the checks holding
//   - so the lines are the result. A run is diffed against the previous one and only the
//     lines the change was meant to touch may differ (the crash-recovery line carries a
//     timestamp and always does), and a line whose two halves disagree is a failure a person
//     has to notice
//   - a check written for a bug is run against the code BEFORE the fix first, and the line it
//     prints then has to visibly disagree with its own "expected". A check that cannot fail
//     is not a check
//
// Three things that have caught people out in here:
//   - undo deserializes a whole new document, so an Element held across an undo is a
//     detached object; re-fetch from Document.Elements after every undo
//   - a click and a drag close together are a double-click, exactly as in the real editor,
//     so two gestures meant to be separate need a Pump() between them
//   - decimals go out through a formatter that pins InvariantCulture: this is built on a
//     pt-BR machine, and a decimal comma would make two machines' runs differ for no reason

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
string settingsPath = Path.Combine(AppContext.BaseDirectory, "e2e-user-settings.json");
File.Delete(presetsPath);
File.Delete(catalogsPath);
File.Delete(settingsPath);
if (Directory.Exists(recoveryDir))
{
    Directory.Delete(recoveryDir, recursive: true);
}

// The viewer's compare mode measures against Labelary, which means sending the label
// over the internet. A harness run must not: the same rule as the scratch store paths
// above, and a harder one, because the recipient is a third party rather than a file.
// The offline engine stands in for it, so what is exercised is the comparison rather
// than the service.
var vm = new MainViewModel(
    new LabelForge.Core.Media.UserMediaStore(presetsPath),
    new LabelForge.Core.Fields.FieldCatalogStore(catalogsPath),
    new LabelForge.Core.Io.RecoveryStore(recoveryDir, "e2e"),
    () => new LabelForge.Core.Rendering.BinaryKitsRenderer(),
    new LabelForge.Core.Settings.UserSettingsStore(settingsPath));
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
    d.LoadStarter(LabelForge.Core.Starters.StarterCatalog.Tour);

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
    //
    // This stock is continuous, so since A3 its height is MEASURED from the content
    // rather than being the catalog's 101.6: a roll has no die cut to match. The width
    // is the one the catalog states, and the flag coming across is the other half of
    // what applying a continuous media has to do.
    var media = LabelForge.Core.Media.StockCatalog.Search("3007301-T")[0];
    d.SelectedMedia = media;
    Console.WriteLine(FormattableString.Invariant(
        $"media apply: {d.WidthMm} mm wide, continuous={d.IsContinuous}, length measured at {d.HeightMm} mm (expected 75.4, True, measured)"));
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

    // The canvas draws the underlay into a rectangle taken from Bitmap.Size, which is
    // device-independent pixels, while the label is counted in printer dots. The two are
    // only the same number while the bitmap says 96 dpi, so this is the contract that
    // decides whether a dot is a pixel. It survived the switch from a decoded PNG to a
    // raw pixel buffer because the buffer is built with the same dpi the decoder used;
    // build it with any other and the label silently changes size on screen.
    Console.WriteLine(
        $"underlay is dot for dot: {d.Underlay?.PixelSize} px, {d.Underlay?.Size} dip, "
        + $"{d.Underlay?.Dpi} dpi (expected equal sizes at 96, 96)");

    // A drag is a stream of live frames and one committed edit at the end of it. The
    // recovery snapshot belongs to the edit: it serializes the document and writes it into
    // the user's profile directory, and every frame of a drag changes the document, so
    // before this was gated a drag wrote that file about forty-five times a second.
    {
        d.NotifyDocumentEdited();
        Pump(700);
        string snapshots() => string.Join(
            ";",
            Directory.Exists(recoveryDir)
                ? Directory.GetFiles(recoveryDir)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .Select(f => $"{Path.GetFileName(f)}@{File.GetLastWriteTimeUtc(f):O}#{new FileInfo(f).Length}")
                : []);

        string before = snapshots();
        var dragged = d.Document.Elements[0];
        int startX = dragged.X;
        for (int frame = 0; frame < 40; frame++)
        {
            dragged.X = startX + frame;
            d.NotifyDocumentPreview();
            Dispatcher.UIThread.RunJobs();
        }

        Pump(700);
        Console.WriteLine(
            $"drag writes no recovery snapshot: {snapshots() == before} (expected True), "
            + $"canvas kept up over 40 frames: {d.CanvasRevision > 0} (expected True)");

        d.NotifyDocumentEdited();
        Pump(700);
        Console.WriteLine(
            $"releasing writes it once: {snapshots() != before} (expected True)");

        dragged.X = startX;
        d.NotifyDocumentEdited();
        Pump(700);
    }
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

    // The graphic primitives ZPL has always had and this designer never offered: the
    // ellipse (which is also how a circle is drawn), the diagonal line, and the corner
    // rounding ^GB takes.
    int beforeEllipse = d.Document.Elements.Count;
    d.AddEllipseCommand.Execute(null);
    d.PlaceAt(560, 300);
    Pump(700);
    Console.WriteLine(
        $"ellipse add: {d.Document.Elements.Count - beforeEllipse} added, "
        + $"selected type ok={d.SelectedElement is LabelForge.Core.Model.EllipseElement} (expected 1/True)");
    Console.WriteLine($"ellipse in ZPL: {d.GeneratedZpl.Contains("^GE200,140,3,B")} (expected True)");

    if (d.SelectionProperties is EllipsePropertiesViewModel ellipse)
    {
        Console.WriteLine(
            $"ellipse panel: '{ellipse.TypeName}', circle={ellipse.IsCircle} (expected Ellipse/False)");

        ellipse.MakeCircleCommand.Execute(null);
        Pump(700);
        Console.WriteLine(
            $"made a circle: '{ellipse.TypeName}', circle={ellipse.IsCircle}, "
            + $"in ZPL={d.GeneratedZpl.Contains("^GE200,200,3,B")} (expected Circle/True/True)");

        // The one thing the canvas cannot show: the offline renderer draws no white ^GE,
        // measured, so the panel has to say so rather than let the preview pretend.
        ellipse.IsWhite = true;
        Pump(500);
        Console.WriteLine(
            $"white ellipse warns: {ellipse.HasWhiteNote} (expected True), "
            + $"prints anyway={d.GeneratedZpl.Contains("^GE200,200,3,W")} (expected True)");
        ellipse.IsWhite = false;
        Pump(300);
    }
    else
    {
        Console.WriteLine("ellipse panel: no EllipsePropertiesViewModel (expected one)");
    }

    int beforeDiagonal = d.Document.Elements.Count;
    d.AddDiagonalCommand.Execute(null);
    d.PlaceAt(560, 120);
    Pump(700);
    Console.WriteLine(
        $"diagonal add: {d.Document.Elements.Count - beforeDiagonal} added, "
        + $"selected type ok={d.SelectedElement is LabelForge.Core.Model.DiagonalLineElement} (expected 1/True)");
    Console.WriteLine($"diagonal in ZPL: {d.GeneratedZpl.Contains("^GD200,140,3,B,R")} (expected True)");

    if (d.SelectionProperties is DiagonalPropertiesViewModel diagonal)
    {
        diagonal.LeansRight = false;
        Pump(500);
        Console.WriteLine(
            $"diagonal leans left: {d.GeneratedZpl.Contains("^GD200,140,3,B,L")} (expected True)");

        // A one-dot diagonal prints and the preview cannot draw it, so the panel says so
        // instead of the thickness being quietly clamped to what the canvas can show.
        diagonal.Thickness = 1;
        Pump(500);
        Console.WriteLine(
            $"one-dot diagonal warns: {diagonal.HasThicknessNote} (expected True), "
            + $"kept at 1={d.GeneratedZpl.Contains("^GD200,140,1,B,L")} (expected True)");
        diagonal.Thickness = 3;
        Pump(300);
    }
    else
    {
        Console.WriteLine("diagonal panel: no DiagonalPropertiesViewModel (expected one)");
    }

    d.AddBoxCommand.Execute(null);
    d.PlaceAt(300, 120);
    Pump(500);
    if (d.SelectionProperties is BoxPropertiesViewModel roundedBox)
    {
        // Nothing is emitted below a rounding of 1, so every label saved before this
        // generates the bytes it always did.
        Console.WriteLine(
            $"square box stays square in ZPL: {d.GeneratedZpl.Contains("^GB240,140,3,B^FS")} (expected True)");
        roundedBox.CornerRoundness = 5;
        Pump(700);
        Console.WriteLine(
            $"rounded box in ZPL: {d.GeneratedZpl.Contains("^GB240,140,3,B,5^FS")} (expected True)");
    }
    else
    {
        Console.WriteLine("box panel: no BoxPropertiesViewModel (expected one)");
    }

    Capture("designer-primitives.png");

    // Everything drawn above has to survive a save and a reload, since a new element type
    // that is not registered for the .lfl looks perfect until the file is reopened.
    var withPrimitives = LabelForge.Core.Io.LabelDocumentJson.Deserialize(d.SerializeDocument());
    Console.WriteLine(
        $"primitives round trip: "
        + $"{withPrimitives.Elements.OfType<LabelForge.Core.Model.EllipseElement>().Count()} ellipse, "
        + $"{withPrimitives.Elements.OfType<LabelForge.Core.Model.DiagonalLineElement>().Count()} diagonal, "
        + $"rounding={withPrimitives.Elements.OfType<LabelForge.Core.Model.BoxElement>().Max(b => b.CornerRoundness)} "
        + "(expected 1/1/5)");

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
    //
    // The grab point is taken from the element rather than written down, which is the
    // whole reason this check went quiet: it used to press at a fixed (600,450), and once
    // the ellipse arrived at (560,300) with a 200 by 200 footprint that point was inside
    // the ellipse instead. The drag moved the ellipse, the line printed the box, and the
    // box had never moved. A grab point derived from what it means to grab cannot drift
    // that way again.
    var boxCopy = d.Document.Elements[5];
    var boxBounds = new LabelForge.Core.Model.ElementBoundsCalculator().GetBounds(boxCopy);
    int grabX = boxBounds.X + 60;
    int grabY = boxBounds.Y + 25;
    Avalonia.Point dragFrom = canvas.TranslatePoint(canvas.DotsToView(grabX, grabY), window)!.Value;
    Avalonia.Point dragTo = canvas.TranslatePoint(canvas.DotsToView(grabX + 157, grabY), window)!.Value;
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

    // The two ways an imported label used to come back wrong, both written the way the
    // corpus writes them: a white ^GB clearing the area in front of a graphic, and a ^FH
    // escape naming a code page byte rather than a code point.
    d.ImportZplDocument(
        "^XA\n^FO40,40^GB0,60,200,W^FS\n^FO40,40^GB200,60,60,B^FS\n"
        + "^FO40,140^A0N,40^FH^FDMinist_82rio^FS\n^XZ",
        "erase-and-escape.zpl");
    Pump(900);
    var erase = (LabelForge.Core.Model.LineElement)d.Document.Elements[0];
    Console.WriteLine(
        $"white erase stays white: IsWhite={erase.IsWhite}, ZPL says "
        + $"'{(d.GeneratedZpl.Contains(",W^FS") ? ",W" : ",B")}' (expected True/,W)");
    Console.WriteLine(
        "outline names it: "
        + $"'{d.Outline.First(r => ReferenceEquals(r.Element, erase)).Display}' "
        + "(expected White line, since it draws nothing the eye can find)");
    Console.WriteLine(
        $"hex escape reads its code page: "
        + $"'{((LabelForge.Core.Model.TextElement)d.Document.Elements[2]).Text}' "
        + "(expected Ministerio with an accented e)");

    d.Selection.Clear();
    Pump(400);
    Capture("designer-erase-and-escape.png");

    // ^FT is how real labels place a field, and it stays a ^FT: where one prints depends
    // on the width of whatever fills its marker, so freezing it into a ^FO would pin it
    // to the marker's own width.
    d.ImportZplDocument(
        "^XA\n^FT340,73^A0I,43,43^FD##PESO_LIQUIDO@0,000##^FS\n"
        + "^FT60,300^A0N,40^FDBaseline^FS\n^FO60,360^A0N,40^FDTop left^FS\n^XZ",
        "typeset.zpl");
    Pump(900);
    Console.WriteLine(
        "anchors read per field: "
        + $"{string.Join(", ", d.Document.Elements.Select(e => e.Anchor))} "
        + "(expected Baseline, Baseline, TopLeft)");
    Console.WriteLine(
        $"and written back as they came: ^FT count={Count(d.GeneratedZpl, "^FT")}, "
        + $"^FO count={Count(d.GeneratedZpl, "^FO")}, keeps ^FT340,73="
        + $"{d.GeneratedZpl.Contains("^FT340,73")} (expected 2/1/True)");

    d.Selection.Set(d.Document.Elements[1]);
    Pump(300);
    Console.WriteLine(
        $"panel names the anchor: '{d.SelectionProperties?.SelectedAnchor}' "
        + "(expected the baseline option)");
    Capture("designer-anchors.png");

    // The printer's built-in fonts. A bitmapped font rides in the command name and only
    // prints whole multiples of its own cell, so the panel edits that multiple.
    d.ImportZplDocument(
        "^XA\n^FO20,20^ADN^FDcell size^FS\n^FO20,80^ADN,36,20^FDdouble^FS\n"
        + "^FO20,160^AGN^FDbig^FS\n^FO20,260^A0N,30^FDscalable^FS\n^XZ",
        "fonts.zpl");
    Pump(900);
    Console.WriteLine(
        "fonts read per field: "
        + $"{string.Join(", ", d.Document.Elements.OfType<LabelForge.Core.Model.TextElement>().Select(t => $"{t.Font}@{t.FontHeightDots}x{t.FontWidthDots}"))} "
        + "(expected D@18x10, D@36x20, G@60x40, 0@30x0)");
    Console.WriteLine(
        $"and written back by name: ^AD count={Count(d.GeneratedZpl, "^AD")}, "
        + $"^AG count={Count(d.GeneratedZpl, "^AG")}, ^A0 count={Count(d.GeneratedZpl, "^A0")} "
        + "(expected 2/1/1)");

    d.Selection.Set(d.Document.Elements[1]);
    Pump(300);
    var textPanel = (LabelForge.App.ViewModels.TextPropertiesViewModel)d.SelectionProperties!;
    Console.WriteLine(
        $"panel offers the multiple: {textPanel.Magnification}x, "
        + $"scalable={textPanel.IsScalableFont}, font='{textPanel.SelectedFont}' "
        + "(expected 2x, False, the D option)");
    Console.WriteLine($"and says what the preview can promise: '{textPanel.FontNote}'");
    Capture("designer-fonts.png");

    // A file that states no ^PW/^LL is measured from what it draws, rather than being
    // floated on a default that silently drops whatever hangs past it.
    d.ImportZplDocument(
        "^XA\n^FO0,0^GB320,1600,4,B^FS\n^FO40,1500^A0N,30^FDpast the old default^FS\n^XZ",
        "unsized.zpl");
    Pump(900);
    Console.WriteLine(
        $"unsized file measured: {d.WidthMm}x{d.HeightMm}mm (expected 40x200)");
    Console.WriteLine(
        $"and the low field still prints: {d.GeneratedZpl.Contains("past the old default")} "
        + $"(expected True), status says so: "
        + $"{d.StatusText.Contains("measured from what the label draws")} (expected True)");

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

    // Check digits, through the panel rather than through the math, because the panel is
    // where the offer has to appear and disappear as the data is typed. An EAN-13 carries
    // twelve digits of a thirteen-digit number, so the number that scans is one nobody can
    // read off the screen unless it is worked out and stated.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    var ean = new LabelForge.Core.Model.BarcodeElement
    {
        X = 40, Y = 60, Symbology = LabelForge.Core.Model.BarcodeSymbology.Ean13,
        Data = "590123412345", HeightDots = 80, ModuleWidthDots = 2,
    };
    d.Document.Elements.Add(ean);
    d.Selection.Set(ean);
    d.NotifyDocumentEdited();
    Pump(900);
    var checkPanel = (BarcodePropertiesViewModel)d.SelectionProperties!;
    Console.WriteLine(
        $"ean check digit: '{checkPanel.CheckDigitInfo}' (expect it scans as 5901234123457), "
        + $"offered={checkPanel.CanAddCheckDigit} (expected True)");

    checkPanel.AppendCheckDigitCommand.Execute(null);
    Pump(900);
    Console.WriteLine(
        $"appended: data={checkPanel.Data} (expected 5901234123457), "
        + $"reaches the ZPL={d.GeneratedZpl.Contains("^FD5901234123457", StringComparison.Ordinal)} (expected True), "
        + $"offered again={checkPanel.CanAddCheckDigit} (expected False)");

    // ^B2 is the one symbology whose check digit is a choice, and the count has to be even
    // or the printer silently pads it. The offline renderer refuses instead, so the
    // preview goes blank and the warning is the only thing that explains why.
    checkPanel.Symbology = LabelForge.Core.Model.BarcodeSymbology.Interleaved2of5;
    checkPanel.Data = "1234567890123";
    Pump(900);
    Console.WriteLine(
        $"itf odd count: '{checkPanel.Warning}' (expect a leading zero and a blank preview), "
        + $"ratio shown={checkPanel.UsesRatio} (expected True)");

    checkPanel.AppendCheckDigitCommand.Execute(null);
    Pump(900);
    Console.WriteLine(
        $"itf-14: data={checkPanel.Data} (expected 12345678901231), "
        + $"warning='{checkPanel.Warning}' (expect empty), "
        + $"^B2 emitted={d.GeneratedZpl.Contains("^B2", StringComparison.Ordinal)} (expected True)");
    Capture("designer-checkdigit.png");
    d.Selection.Clear();
    Pump(200);

    // A file with several labels stays reachable after the import. Real ones routinely
    // hold more than one: this corpus file holds four, and one of them holds twenty-seven.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    d.ImportZplDocument(
        LabelForge.Core.Io.ZplTextFile.Read(File.ReadAllBytes(graphicSource)).Text,
        Path.GetFileName(graphicSource));
    Pump(900);
    Console.WriteLine(
        $"imported file offers its labels: {d.ImportedBlocks.Count} blocks, "
        + $"strip shown={d.HasImportedBlocks}, on '{d.SelectedImportedBlock}' "
        + "(expected 4, True, the first with content)");
    Console.WriteLine(
        $"blocks are described: {string.Join(" | ", d.ImportedBlocks)}");
    Capture("designer-imported-blocks.png");

    // Switching opens that one instead, which is the whole point.
    // Any block but the open one. Most of this file's are the bare configuration blocks
    // real files start with, which is exactly why they are labelled "empty": that is the
    // only way to tell which are worth opening before opening one.
    int elementsBefore = d.Document.Elements.Count;
    var other = d.ImportedBlocks.First(b => b != d.SelectedImportedBlock);
    d.SelectedImportedBlock = other;
    Pump(900);
    Console.WriteLine(
        $"switching opens another label: {elementsBefore} -> {d.Document.Elements.Count} elements, "
        + $"now on '{d.SelectedImportedBlock}' (expected the block's own count)");

    d.CloseImportedFileCommand.Execute(null);
    Pump(300);
    Console.WriteLine(
        $"done with the file: strip shown={d.HasImportedBlocks} (expected False)");

    // Design grid: drawn and snapped to from one source, and a canvas-only change, which
    // is the case the render cache could have stopped repainting.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    var snapped = new LabelForge.Core.Model.BoxElement
    {
        X = 137, Y = 91, WidthDots = 120, HeightDots = 80, ThicknessDots = 3,
    };
    d.Document.Elements.Add(snapped);
    d.NotifyDocumentEdited();
    Pump(900);
    int revisionBeforeGrid = d.CanvasRevision;
    d.GridPitchMm = 5;
    Pump(900);
    bool repainted = d.CanvasRevision > revisionBeforeGrid;
    Console.WriteLine(FormattableString.Invariant(
        $"grid on: pitch {d.Document.GridPitchMm} mm, canvas told to repaint={repainted} (expected 5, True)"));
    Console.WriteLine(
        $"grid stays out of the ZPL: {!d.GeneratedZpl.Contains("grid", StringComparison.OrdinalIgnoreCase)} "
        + "(expected True)");
    Capture("designer-grid.png");

    // Dragging near a grid line lands on it. 5 mm at 8 dpmm is every 40 dots, and the
    // grid snaps by proximity like the guides do rather than forcing every position onto
    // it, so the drag has to end within the threshold for there to be anything to see.
    snapped.X = 200;
    snapped.Y = 120;
    d.Selection.Set(snapped);
    d.NotifyDocumentEdited();
    Pump(700);
    // Far enough to be a drag and not a click, and far enough that the element ends
    // somewhere it did not start: +45 dots puts it at 245,165, five short of the lines at
    // 240 and 160, which is inside the snap threshold. The old version moved 5 dots and
    // snapped straight back to where it began, so it read as a pass whether the drag had
    // done anything or not.
    var gridFrom = canvas.TranslatePoint(canvas.DotsToView(240, 150), window)!.Value;
    var gridTo = canvas.TranslatePoint(canvas.DotsToView(285, 195), window)!.Value;
    window.MouseDown(gridFrom, MouseButton.Left);
    window.MouseMove(gridTo);
    window.MouseUp(gridTo, MouseButton.Left);
    Pump(700);
    Console.WriteLine(
        $"drag near a line lands on it: {snapped.X},{snapped.Y} "
        + $"(expected 240,160: multiples of 40, and moved from 200,120)");

    // And a drag that ends well away from any line is left where it was put, because the
    // grid is a hint and not a cage.
    // Chosen so no edge AND no centre of the 120 by 80 box lands within the snap
    // threshold of a line. The centre is the one that catches you out: at 263,183 the box
    // is 17 dots off every line by its edges and 3 dots off one by its middle, so it
    // snapped anyway and the check would have been describing the wrong thing.
    var freeFrom = canvas.TranslatePoint(canvas.DotsToView(280, 200), window)!.Value;
    var freeTo = canvas.TranslatePoint(canvas.DotsToView(290, 220), window)!.Value;
    window.MouseDown(freeFrom, MouseButton.Left);
    window.MouseMove(freeTo);
    window.MouseUp(freeTo, MouseButton.Left);
    Pump(700);
    Console.WriteLine(
        $"drag between lines stays put: {snapped.X},{snapped.Y} "
        + "(expected 250,180: moved, and not forced onto the grid)");

    // A press is a click until the pointer travels. Below the threshold it must move
    // nothing and record nothing: an unsteady click at a low zoom used to nudge an element
    // by a few dots and bury the real edit under an undo step for the accident.
    var pressTarget = d.Document.Elements[0];
    pressTarget.X = 200;
    pressTarget.Y = 120;
    d.Selection.Set(pressTarget);
    d.NotifyDocumentEdited();
    Pump(700);

    var tinyFrom = canvas.TranslatePoint(canvas.DotsToView(240, 150), window)!.Value;
    var tinyTo = new Avalonia.Point(tinyFrom.X + 2, tinyFrom.Y + 2);
    window.MouseDown(tinyFrom, MouseButton.Left);
    window.MouseMove(tinyTo);
    window.MouseUp(tinyTo, MouseButton.Left);
    Pump(700);
    Console.WriteLine(
        $"a 2 px press moves nothing: {d.Document.Elements[0].X},{d.Document.Elements[0].Y} "
        + "(expected 200,120)");

    // Undo has to land on the edit before the press, which is only true if the press
    // recorded nothing of its own.
    d.UndoCommand.Execute(null);
    Pump(700);
    Console.WriteLine(
        $"and records no undo step: {d.Document.Elements[0].X},{d.Document.Elements[0].Y} "
        + "(expected 250,180, where it sat before the move that IS a step)");
    d.RedoCommand.Execute(null);
    Pump(700);

    // Ctrl decides the selection on the RELEASE now, because the same key means duplicate
    // once a drag begins. Pressing it on something already selected and letting go without
    // moving takes that element out; crossing the threshold instead leaves the selection
    // alone, because the press was a grab.
    var second = new LabelForge.Core.Model.BoxElement
    {
        X = 480, Y = 120, WidthDots = 120, HeightDots = 80, ThicknessDots = 3,
    };
    d.Document.Elements.Add(second);
    d.NotifyDocumentEdited();
    Pump(700);

    void SelectBoth()
    {
        d.Selection.SetMany(d.Document.Elements);
        Pump(200);
    }

    var ctrlOn = canvas.TranslatePoint(canvas.DotsToView(520, 150), window)!.Value;
    SelectBoth();
    window.MouseDown(ctrlOn, MouseButton.Left, RawInputModifiers.Control);
    window.MouseUp(ctrlOn, MouseButton.Left, RawInputModifiers.Control);
    Pump(400);
    Console.WriteLine(
        $"ctrl click in place removes it: {d.Selection.Count} selected (expected 1)");

    // Crossing the threshold means the press was a grab, so the toggle never happens. What
    // the grab then does is H11's business (it duplicates), and is checked below; all this
    // asks is that nothing left the selection.
    SelectBoth();
    window.MouseDown(ctrlOn, MouseButton.Left, RawInputModifiers.Control);
    window.MouseMove(new Avalonia.Point(ctrlOn.X + 30, ctrlOn.Y), RawInputModifiers.Control);
    window.MouseUp(new Avalonia.Point(ctrlOn.X + 30, ctrlOn.Y), MouseButton.Left, RawInputModifiers.Control);
    Pump(700);
    Console.WriteLine(
        $"ctrl drag does not toggle: {d.Selection.Count} selected (expected 2)");

    d.UndoCommand.Execute(null);
    Pump(700);

    // Shift locks a move to whichever axis the pointer commits to. Dragged 60 px right
    // and 12 down, the vertical part is dropped.
    var locked = d.Document.Elements[^1];
    d.Selection.Set(locked);
    d.NotifyDocumentEdited();
    Pump(700);
    int lockStartX = locked.X;
    int lockStartY = locked.Y;
    var lockFrom = canvas.TranslatePoint(canvas.DotsToView(locked.X + 40, locked.Y + 30), window)!.Value;
    window.MouseDown(lockFrom, MouseButton.Left);
    window.MouseMove(new Avalonia.Point(lockFrom.X + 60, lockFrom.Y + 12), RawInputModifiers.Shift);
    window.MouseUp(new Avalonia.Point(lockFrom.X + 60, lockFrom.Y + 12), MouseButton.Left, RawInputModifiers.Shift);
    Pump(700);
    Console.WriteLine(
        $"shift drag locks to one axis: moved x by {locked.X - lockStartX}, "
        + $"y by {locked.Y - lockStartY} (expected a change in x and 0 in y)");

    // Ctrl held across the threshold copies instead of moving: the count goes up, the
    // original stays where it was, and the copies are what is selected.
    int beforeDuplicate = d.Document.Elements.Count;
    var original = locked;
    int originalX = original.X;
    var dupFrom = canvas.TranslatePoint(canvas.DotsToView(original.X + 40, original.Y + 30), window)!.Value;
    d.Selection.Set(original);
    Pump(200);
    window.MouseDown(dupFrom, MouseButton.Left, RawInputModifiers.Control);
    window.MouseMove(new Avalonia.Point(dupFrom.X + 40, dupFrom.Y), RawInputModifiers.Control);
    window.MouseUp(new Avalonia.Point(dupFrom.X + 40, dupFrom.Y), MouseButton.Left, RawInputModifiers.Control);
    Pump(700);
    Console.WriteLine(
        $"ctrl drag duplicates: {beforeDuplicate} -> {d.Document.Elements.Count} elements "
        + $"(expected {beforeDuplicate + 1}), original left at {original.X} (expected {originalX}), "
        + $"the copy is selected: {d.Selection.Count == 1 && !d.Selection.Contains(original)} (expected True)");

    // And one undo step covers the copy and the move together.
    d.UndoCommand.Execute(null);
    Pump(700);
    Console.WriteLine(
        $"one undo takes the copy back out: {d.Document.Elements.Count} elements "
        + $"(expected {beforeDuplicate})");

    // Ctrl-drag on a LOCKED element. A copy of something that cannot be dragged would sit
    // in the document with nothing moving it, so nothing is copied at all.
    var immovable = new LabelForge.Core.Model.BoxElement
    {
        X = 700, Y = 60, WidthDots = 120, HeightDots = 80, ThicknessDots = 3,
        ZOrder = 200, IsLocked = true,
    };
    d.Document.Elements.Add(immovable);
    d.Selection.Clear();
    d.NotifyDocumentEdited();
    Pump(700);

    int beforeLockedDrag = d.Document.Elements.Count;
    var lockedFrom = canvas.TranslatePoint(canvas.DotsToView(760, 100), window)!.Value;
    window.MouseDown(lockedFrom, MouseButton.Left, RawInputModifiers.Control);
    window.MouseMove(new Avalonia.Point(lockedFrom.X + 40, lockedFrom.Y), RawInputModifiers.Control);
    window.MouseUp(
        new Avalonia.Point(lockedFrom.X + 40, lockedFrom.Y), MouseButton.Left, RawInputModifiers.Control);
    Pump(700);
    Console.WriteLine(
        $"ctrl drag on a locked element copies nothing: {d.Document.Elements.Count} elements "
        + $"(expected {beforeLockedDrag}), still at {immovable.X} (expected 700)");
    d.Document.Elements.Remove(immovable);
    d.NotifyDocumentEdited();
    Pump(700);

    // Alt-click walks down the stack instead of picking the top one again. Two boxes are
    // parked on the same spot so there is a stack to walk.
    var lower = new LabelForge.Core.Model.BoxElement
    {
        X = 600, Y = 300, WidthDots = 140, HeightDots = 100, ThicknessDots = 3, ZOrder = 90,
    };
    var upper = new LabelForge.Core.Model.BoxElement
    {
        X = 600, Y = 300, WidthDots = 140, HeightDots = 100, ThicknessDots = 3, ZOrder = 91,
    };
    d.Document.Elements.Add(lower);
    d.Document.Elements.Add(upper);
    d.NotifyDocumentEdited();
    Pump(700);

    var stacked = canvas.TranslatePoint(canvas.DotsToView(660, 340), window)!.Value;
    window.MouseDown(stacked, MouseButton.Left);
    window.MouseUp(stacked, MouseButton.Left);
    Pump(300);
    bool topFirst = ReferenceEquals(d.Selection.Primary, upper);
    window.MouseDown(stacked, MouseButton.Left, RawInputModifiers.Alt);
    window.MouseUp(stacked, MouseButton.Left, RawInputModifiers.Alt);
    Pump(300);
    bool thenBelow = ReferenceEquals(d.Selection.Primary, lower);
    window.MouseDown(stacked, MouseButton.Left, RawInputModifiers.Alt);
    window.MouseUp(stacked, MouseButton.Left, RawInputModifiers.Alt);
    Pump(300);
    Console.WriteLine(
        $"alt click selects through: top first={topFirst}, then the one under it={thenBelow}, "
        + $"then wraps={ReferenceEquals(d.Selection.Primary, upper)} (expected True, True, True)");

    // Escape mid-drag puts the element back where the gesture started and records
    // nothing, so undo still lands on the edit before it rather than on the cancelled
    // gesture. The pointer is never released here: cancelling has to end the gesture on
    // its own, or the release afterwards would commit it.
    d.Selection.Set(upper);
    upper.X = 600;
    upper.Y = 300;
    d.NotifyDocumentEdited();
    Pump(700);

    var escFrom = canvas.TranslatePoint(canvas.DotsToView(660, 340), window)!.Value;
    window.MouseDown(escFrom, MouseButton.Left);
    window.MouseMove(new Avalonia.Point(escFrom.X + 50, escFrom.Y + 40));
    Pump(300);
    bool movedFirst = upper.X != 600 || upper.Y != 300;
    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
    Pump(700);
    Console.WriteLine(
        $"escape cancels a drag: moved to {movedFirst}, back at {upper.X},{upper.Y} "
        + "(expected True, 600,300)");

    window.MouseUp(new Avalonia.Point(escFrom.X + 50, escFrom.Y + 40), MouseButton.Left);
    Pump(700);
    Console.WriteLine(
        $"and the release does not commit it: {upper.X},{upper.Y} (expected 600,300)");

    // Escape on a duplicating drag has to take the copies back out as well.
    int beforeEscapeDuplicate = d.Document.Elements.Count;
    d.Selection.Set(d.Document.Elements[^1]);
    Pump(200);
    window.MouseDown(escFrom, MouseButton.Left, RawInputModifiers.Control);
    window.MouseMove(new Avalonia.Point(escFrom.X + 50, escFrom.Y), RawInputModifiers.Control);
    Pump(300);
    int duringDuplicate = d.Document.Elements.Count;
    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
    Pump(700);
    window.MouseUp(new Avalonia.Point(escFrom.X + 50, escFrom.Y), MouseButton.Left, RawInputModifiers.Control);
    Pump(700);
    Console.WriteLine(
        $"escape takes a duplicate back out: {beforeEscapeDuplicate} -> {duringDuplicate} "
        + $"-> {d.Document.Elements.Count} (expected up by one, then back down)");

    // Dragging to the edge scrolls the view and keeps the element following. Both have to
    // move: the view alone means the element was left behind, and the element alone means
    // it stopped at the edge of what was on screen.
    d.Selection.Set(upper);
    upper.X = 600;
    upper.Y = 300;
    d.NotifyDocumentEdited();
    Pump(700);

    double originBeforePan = canvas.DotsToView(0, 0).X;
    // Where the viewport ended before the pan, in dots. That is what the element has to
    // get past for the drag to have reached somewhere the view was not already showing.
    double dotsPerPixel = 100.0 / (canvas.DotsToView(100, 0).X - originBeforePan);
    double edgeDotsBefore = (canvas.Bounds.Width - originBeforePan) * dotsPerPixel;
    var panFrom = canvas.TranslatePoint(canvas.DotsToView(660, 340), window)!.Value;
    // The edge is the CANVAS's edge, and the harness clicks in window coordinates, so it
    // has to be translated like every other point here.
    var panTo = canvas.TranslatePoint(
        new Avalonia.Point(canvas.Bounds.Width - 1, canvas.DotsToView(660, 340).Y), window)!.Value;
    window.MouseDown(panFrom, MouseButton.Left);
    window.MouseMove(panTo);
    Pump(500);
    int xAfterPan = upper.X;
    window.MouseUp(panTo, MouseButton.Left);
    Pump(700);
    // Printed as verdicts rather than distances, and the distances only when one fails.
    // How far the view gets depends on how many timer ticks the pump delivered, which
    // differs from run to run: a line that changes every time is a line nobody reads.
    double originAfterPan = canvas.DotsToView(0, 0).X;
    bool panned = originAfterPan < originBeforePan - 1;
    Console.WriteLine(
        $"drag at the edge pans the view: {panned} (expected True)"
        + (panned ? "" : $" [label origin on screen {originBeforePan:0} -> {originAfterPan:0}]"));
    // What the hand is holding, which started 60 dots into the box and stays there for the
    // whole drag: where THAT ends up is how far the drag reached. It is the quantity to
    // check rather than the box's left edge, because the pointer never leaves the canvas,
    // so a held point past the old edge is only possible if the view moved under it. The
    // left edge would need a longer pan than the harness can drive, since the timer that
    // continues the scroll gets a handful of ticks out of the pump instead of thirty.
    int heldDot = xAfterPan + 60;
    bool reached = heldDot > edgeDotsBefore;
    Console.WriteLine(
        $"and the element keeps following: the held point is past the {edgeDotsBefore:0} dots the "
        + $"viewport ended at: {reached} (expected True)"
        + (reached ? "" : $" [held {heldDot}, element x {xAfterPan}]"));

    // And it stops: nothing keeps scrolling once the button is up.
    double originAtRest = canvas.DotsToView(0, 0).X;
    Pump(500);
    Console.WriteLine(
        $"the pan stops on release: {Math.Abs(canvas.DotsToView(0, 0).X - originAtRest) < 0.5} (expected True)");

    // With no gesture running, Escape clears the selection the way it does everywhere.
    d.Selection.Set(upper);
    Pump(200);
    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
    Pump(300);
    Console.WriteLine(
        $"escape with nothing running clears the selection: {d.Selection.Count} (expected 0)");

    d.Document.Elements.Remove(lower);
    d.Document.Elements.Remove(upper);
    d.Document.Elements.Remove(second);
    d.NotifyDocumentEdited();
    d.GridPitchMm = 0;
    d.Selection.Clear();
    Pump(400);

    // The readout: where the pointer is, and how big the selection is. The view is put
    // back first, because the auto-pan drag above left it scrolled.
    canvas.ResetView();
    Pump(300);
    var readoutBox = new LabelForge.Core.Model.BoxElement
    {
        X = 200, Y = 100, WidthDots = 300, HeightDots = 150, ThicknessDots = 3,
    };
    d.Document.Elements.Add(readoutBox);
    d.NotifyDocumentEdited();
    Pump(700);

    window.MouseMove(canvas.TranslatePoint(canvas.DotsToView(160, 80), window)!.Value);
    Pump(300);
    string readout = d.CanvasReadout;
    Console.WriteLine(
        $"readout follows the pointer: '{readout}' "
        + $"(expected the dots to read 160, 80): {readout.Contains("160, 80 dots")}");

    d.Selection.Set(readoutBox);
    Pump(300);
    string withSelection = d.CanvasReadout;
    Console.WriteLine(
        $"and the selection: '{withSelection}' (expected 200, 100 and 300 x 150): "
        + $"{withSelection.Contains("200, 100") && withSelection.Contains("300 x 150")}");

    // The hover outline goes on the element under the pointer while it is NOT selected,
    // which is the state the screenshot has to show. Text rather than the box above: a
    // box's own border sits exactly where its hover outline goes, so the picture would
    // prove nothing.
    var hoverText = new LabelForge.Core.Model.TextElement
    {
        X = 220, Y = 320, Text = "hover me", FontHeightDots = 50,
    };
    d.Document.Elements.Add(hoverText);
    d.Selection.Clear();
    d.NotifyDocumentEdited();
    Pump(700);
    window.MouseMove(canvas.TranslatePoint(canvas.DotsToView(280, 345), window)!.Value);
    Pump(400);
    Capture("designer-hover.png");
    d.Document.Elements.Remove(hoverText);

    // Over a ruler there is no position to report and nothing is selected, so the readout
    // has nothing to say.
    window.MouseMove(canvas.TranslatePoint(new Avalonia.Point(4, 4), window)!.Value);
    Pump(300);
    Console.WriteLine(
        $"readout clears off the label: '{d.CanvasReadout}' (expected empty)");

    d.Document.Elements.Remove(readoutBox);
    d.NotifyDocumentEdited();
    Pump(400);

    // Selection by keyboard. Three elements with stated z-orders, so front to back is a
    // fact rather than whatever order they went into the list.
    d.Document.Elements.Clear();
    var tabBack = new LabelForge.Core.Model.BoxElement
    {
        X = 40, Y = 40, WidthDots = 120, HeightDots = 80, ThicknessDots = 3, ZOrder = 1,
    };
    var tabMiddle = new LabelForge.Core.Model.BoxElement
    {
        X = 200, Y = 40, WidthDots = 120, HeightDots = 80, ThicknessDots = 3, ZOrder = 2,
    };
    var tabFront = new LabelForge.Core.Model.TextElement
    {
        X = 360, Y = 40, Text = "front", FontHeightDots = 40, ZOrder = 3,
    };
    d.Document.Elements.Add(tabBack);
    d.Document.Elements.Add(tabMiddle);
    d.Document.Elements.Add(tabFront);
    d.Selection.Clear();
    d.NotifyDocumentEdited();
    Pump(700);

    window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
    Pump(300);
    Console.WriteLine($"ctrl+a selects all: {d.Selection.Count} (expected 3)");

    // Tab walks down the z-order from the front and wraps; Shift+Tab comes back up. The
    // canvas has to hold focus for it, which a click gives it.
    d.Selection.Clear();
    canvas.Focus();
    Pump(200);
    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "	");
    Pump(200);
    string tabFirst = d.Selection.Primary is { } a ? a.ZOrder.ToString() : "none";
    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "	");
    Pump(200);
    string tabSecond = d.Selection.Primary is { } b ? b.ZOrder.ToString() : "none";
    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "	");
    Pump(200);
    string tabThird = d.Selection.Primary is { } c ? c.ZOrder.ToString() : "none";
    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "	");
    Pump(200);
    string tabWrapped = d.Selection.Primary is { } e2 ? e2.ZOrder.ToString() : "none";
    Console.WriteLine(
        $"tab walks the z-order: {tabFirst} {tabSecond} {tabThird} then wraps to {tabWrapped} (expected 3 2 1 then 3)");

    window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "	");
    Pump(200);
    Console.WriteLine(
        $"shift+tab goes back up: {(d.Selection.Primary is { } f ? f.ZOrder.ToString() : "none")} "
        + "(expected 1, wrapping the other way)");

    // Double-click puts the caret in the field holding that element's content, with the
    // text selected so typing replaces it.
    var editBounds = new LabelForge.Core.Model.ElementBoundsCalculator().GetBounds(tabFront);
    var editPoint = canvas.TranslatePoint(
        canvas.DotsToView(
            editBounds.X + editBounds.Width / 2, editBounds.Y + editBounds.Height / 2),
        window)!.Value;
    window.MouseDown(editPoint, MouseButton.Left);
    window.MouseUp(editPoint, MouseButton.Left);
    Pump(120);
    window.MouseDown(editPoint, MouseButton.Left);
    window.MouseUp(editPoint, MouseButton.Left);
    Pump(600);
    var editFocused = Avalonia.Controls.TopLevel.GetTopLevel(canvas)?.FocusManager?.GetFocusedElement();
    string editSelected = editFocused is Avalonia.Controls.TextBox tb ? tb.SelectedText : "not a text box";
    Console.WriteLine(
        $"double-click edits the content: selected '{editSelected}' (expected 'front'), "
        + $"selection is the text: {d.Selection.Primary == tabFront}");

    // One step through the stacking order. The z-orders in the document have to be the same
    // SET afterwards, only dealt out differently: the command swaps with the neighbour it
    // passes rather than assigning fresh numbers.
    string zBefore = string.Join(
        ",", d.Document.Elements.Select(el => el.ZOrder).OrderBy(z => z));
    d.Selection.Set(tabBack);
    canvas.Focus();
    Pump(300);
    window.KeyPress(
        Key.Up, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.ArrowUp, null);
    Pump(700);
    string zAfter = string.Join(
        ",", d.Document.Elements.Select(el => el.ZOrder).OrderBy(z => z));
    Console.WriteLine(
        $"ctrl+shift+up brings it forward: back is now {tabBack.ZOrder} (expected 2), "
        + $"middle {tabMiddle.ZOrder} (expected 1), z-orders still {zAfter} (expected {zBefore})");

    // And the brackets, pressed the way a real keyboard sends them. This check used to hand
    // the handler a "[" as the key symbol, which is a character Windows does not send: a
    // bracket held with Ctrl arrives as a control character (U+001B and U+001C on ABNT2,
    // measured), so the character match never fired and the keys did nothing in the running
    // app while this line read as a pass. The symbols below are those control characters, and
    // the keys are whatever the layout in force says carries a bracket, which is the same
    // question DesignerView asks. Nothing here is a fixed key: the answer is ABNT2's Oem6 and
    // Oem5 on this machine and OemOpenBrackets and Oem6 on a US one, and the check holds
    // either way.
    Key openBracketKey = LabelForge.App.Services.KeyboardLayout.KeyThatTypes('[') ?? Key.None;
    Key closeBracketKey = LabelForge.App.Services.KeyboardLayout.KeyThatTypes(']') ?? Key.None;
    Console.WriteLine(
        $"the layout carries the brackets: [ on {openBracketKey}, ] on {closeBracketKey} "
        + "(expected two keys that are not None on Windows)");

    window.KeyPress(openBracketKey, RawInputModifiers.Control, PhysicalKey.None, ControlChar(0x1B));
    Pump(700);
    Console.WriteLine(
        $"ctrl+[ sends it back down: back is {tabBack.ZOrder} (expected 1 again), "
        + $"middle {tabMiddle.ZOrder} (expected 2 again)");

    // Nothing to pass: the front element stays where it is and records no undo step.
    d.Selection.Set(tabFront);
    Pump(200);
    int undosBefore = d.Document.Elements.Count;
    window.KeyPress(closeBracketKey, RawInputModifiers.Control, PhysicalKey.None, ControlChar(0x1C));
    Pump(700);
    Console.WriteLine(
        $"and stops at the front: {tabFront.ZOrder} (expected 3), "
        + $"z-orders {string.Join(",", d.Document.Elements.Select(el => el.ZOrder).OrderBy(z => z))} "
        + $"(expected {zBefore})");

    // Cut is a copy and a delete, and it has to be ONE undo step: the copy records nothing,
    // because a clipboard is not part of the document.
    d.Selection.Set(tabMiddle);
    Pump(200);
    int beforeCut = d.Document.Elements.Count;
    window.KeyPress(Key.X, RawInputModifiers.Control, PhysicalKey.X, "x");
    Pump(700);
    int afterCut = d.Document.Elements.Count;
    d.UndoCommand.Execute(null);
    Pump(700);
    Console.WriteLine(
        $"ctrl+x cuts: {beforeCut} -> {afterCut} elements (expected {beforeCut - 1}), "
        + $"one undo brings it back: {d.Document.Elements.Count} (expected {beforeCut})");

    // Paste in place puts the copy exactly where the original was; the plain paste walks it
    // along instead. Cutting again first, so the clipboard holds a known position.
    var placed = d.Document.Elements.First(el => el is LabelForge.Core.Model.BoxElement b && b.X == 200);
    int placedX = placed.X;
    int placedY = placed.Y;
    d.Selection.Set(placed);
    Pump(200);
    window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
    Pump(300);
    window.KeyPress(
        Key.V, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.V, "v");
    Pump(700);
    var inPlace = d.Selection.Primary!;
    Console.WriteLine(
        $"ctrl+shift+v pastes in place: {inPlace.X},{inPlace.Y} (expected {placedX},{placedY}), "
        + $"and it is a copy: {!ReferenceEquals(inPlace, placed)} (expected True)");

    window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");
    Pump(700);
    var cascaded = d.Selection.Primary!;
    Console.WriteLine(
        $"and plain ctrl+v still walks it along: {cascaded.X},{cascaded.Y} "
        + $"(expected past {placedX},{placedY})");

    d.Selection.Clear();
    Pump(200);

    // Handles that tell the truth. A box carries no orientation in its ZPL, so the rotation
    // handle is not drawn for one and must not be grabbable either: pressing where it would
    // be has to start a MARQUEE, which is what that empty spot means without it.
    var turnable = d.Document.Elements.OfType<LabelForge.Core.Model.TextElement>().First();
    var unturnable = d.Document.Elements.OfType<LabelForge.Core.Model.BoxElement>().First();
    d.Selection.Set(unturnable);
    Pump(400);
    var boxRect = new LabelForge.Core.Model.ElementBoundsCalculator().GetBounds(unturnable);
    var boxTop = canvas.DotsToView(boxRect.X + boxRect.Width / 2, boxRect.Y);
    var rotGrab = canvas.TranslatePoint(new Avalonia.Point(boxTop.X, boxTop.Y - 26), window)!.Value;
    var boxOrientationBefore = unturnable.Orientation;
    window.MouseDown(rotGrab, MouseButton.Left);
    window.MouseMove(new Avalonia.Point(rotGrab.X + 60, rotGrab.Y + 60));
    window.MouseUp(new Avalonia.Point(rotGrab.X + 60, rotGrab.Y + 60), MouseButton.Left);
    Pump(700);
    Console.WriteLine(
        $"no rotation handle on a box: orientation {unturnable.Orientation} "
        + $"(expected {boxOrientationBefore}, unchanged)");

    // The same grab on a field that DOES turn, so the line above is a difference between the
    // two elements rather than a drag the harness aimed wrong.
    d.Selection.Set(turnable);
    Pump(400);
    var textRect = new LabelForge.Core.Model.ElementBoundsCalculator().GetBounds(turnable);
    var textTop = canvas.DotsToView(textRect.X + textRect.Width / 2, textRect.Y);
    var textRotGrab = canvas.TranslatePoint(new Avalonia.Point(textTop.X, textTop.Y - 26), window)!.Value;
    window.MouseDown(textRotGrab, MouseButton.Left);
    window.MouseMove(new Avalonia.Point(textRotGrab.X + 80, textRotGrab.Y + 80));
    window.MouseUp(new Avalonia.Point(textRotGrab.X + 80, textRotGrab.Y + 80), MouseButton.Left);
    Pump(700);
    Console.WriteLine(
        $"but the same grab turns a text field: {turnable.Orientation} (expected not Normal)");
    d.UndoCommand.Execute(null);
    Pump(700);

    // Undo rebuilds the document by deserializing a snapshot, so every element reference
    // taken before it now points at a detached copy that no longer belongs to any document.
    // Anything after an undo has to ask the document again.
    turnable = d.Document.Elements.OfType<LabelForge.Core.Model.TextElement>().First();
    unturnable = d.Document.Elements.OfType<LabelForge.Core.Model.BoxElement>().First();
    Console.WriteLine(
        $"and undo puts it back: {turnable.Orientation} (expected Normal)");

    // An element smaller than its own handles does not get them, so a press on its corner
    // is a press on the ELEMENT and moves it instead of resizing it.
    var tiny = new LabelForge.Core.Model.BoxElement
    {
        X = 600, Y = 400, WidthDots = 10, HeightDots = 10, ThicknessDots = 1, ZOrder = 50,
    };
    d.Document.Elements.Add(tiny);
    d.Selection.Set(tiny);
    d.NotifyDocumentEdited();
    Pump(700);
    var tinyCorner = canvas.TranslatePoint(canvas.DotsToView(601, 401), window)!.Value;
    window.MouseDown(tinyCorner, MouseButton.Left);
    window.MouseMove(new Avalonia.Point(tinyCorner.X + 40, tinyCorner.Y + 40));
    window.MouseUp(new Avalonia.Point(tinyCorner.X + 40, tinyCorner.Y + 40), MouseButton.Left);
    Pump(700);
    Console.WriteLine(
        $"a tiny element has no handles to catch: {tiny.WidthDots}x{tiny.HeightDots} dots "
        + $"(expected 10x10, unresized), moved to {tiny.X},{tiny.Y} (expected past 600,400)");
    d.Document.Elements.Remove(tiny);
    d.NotifyDocumentEdited();
    Pump(400);

    // Ctrl+R turns what can be turned and leaves the rest alone, so a mixed selection is
    // not an all-or-nothing choice.
    d.Selection.SetMany([turnable, unturnable]);
    canvas.Focus();
    Pump(300);
    var textBefore = turnable.Orientation;
    window.KeyPress(Key.R, RawInputModifiers.Control, PhysicalKey.R, "r");
    Pump(700);
    var expectedTurn = (LabelForge.Core.Model.Orientation)(((int)textBefore + 1) % 4);
    Console.WriteLine(
        $"ctrl+r turns the text: {textBefore} -> {turnable.Orientation} (expected {expectedTurn}), "
        + $"and leaves the box at {unturnable.Orientation} (expected {boxOrientationBefore})");
    d.UndoCommand.Execute(null);
    Pump(700);

    d.Selection.Clear();
    Pump(200);

    // Groups. Three elements, two of them about to become one thing.
    d.Document.Elements.Clear();
    var gLeft = new LabelForge.Core.Model.BoxElement
    {
        X = 60, Y = 60, WidthDots = 120, HeightDots = 80, ThicknessDots = 3, ZOrder = 1,
    };
    var gRight = new LabelForge.Core.Model.TextElement
    {
        X = 240, Y = 100, Text = "in a group", FontHeightDots = 40, ZOrder = 2,
    };
    var gLoose = new LabelForge.Core.Model.BoxElement
    {
        X = 500, Y = 60, WidthDots = 120, HeightDots = 80, ThicknessDots = 3, ZOrder = 3,
    };
    d.Document.Elements.Add(gLeft);
    d.Document.Elements.Add(gRight);
    d.Document.Elements.Add(gLoose);
    d.Selection.SetMany([gLeft, gRight]);
    canvas.Focus();
    d.NotifyDocumentEdited();
    Pump(700);

    window.KeyPress(Key.G, RawInputModifiers.Control, PhysicalKey.G, "g");
    Pump(700);
    Console.WriteLine(
        $"ctrl+g groups: both carry an id: {gLeft.GroupId is not null && gLeft.GroupId == gRight.GroupId} "
        + $"(expected True), the loose one does not: {gLoose.GroupId is null} (expected True)");

    // Clicking one member takes the whole group, which is the point of having one.
    d.Selection.Clear();
    Pump(200);
    var memberPoint = canvas.TranslatePoint(canvas.DotsToView(120, 100), window)!.Value;
    window.MouseDown(memberPoint, MouseButton.Left);
    window.MouseUp(memberPoint, MouseButton.Left);
    Pump(400);
    Console.WriteLine(
        $"clicking a member takes the group: {d.Selection.Count} selected (expected 2)");

    // And it moves as one thing: dragging one member carries the other by the same amount.
    // Grabbed at another point and after a pause, or the press that starts the drag lands
    // inside the double-click window of the click above and opens the group instead, which
    // is what a real editor does too.
    Pump(900);
    int leftBefore = gLeft.X;
    int rightBefore = gRight.X;
    var dragPoint = canvas.TranslatePoint(canvas.DotsToView(100, 125), window)!.Value;
    window.MouseDown(dragPoint, MouseButton.Left);
    window.MouseMove(new Avalonia.Point(dragPoint.X + 60, dragPoint.Y));
    window.MouseUp(new Avalonia.Point(dragPoint.X + 60, dragPoint.Y), MouseButton.Left);
    Pump(700);
    Console.WriteLine(
        $"dragging one member moves both: left by {gLeft.X - leftBefore}, "
        + $"right by {gRight.X - rightBefore} (expected the same non-zero number twice)");

    // A double-click opens the group and takes the one member under the pointer; a second
    // one on that member is what reaches its text (the H15 gesture, one step further in).
    var textPoint = canvas.TranslatePoint(
        canvas.DotsToView(
            new LabelForge.Core.Model.ElementBoundsCalculator().GetBounds(gRight).X + 40,
            new LabelForge.Core.Model.ElementBoundsCalculator().GetBounds(gRight).Y + 20),
        window)!.Value;
    window.MouseDown(textPoint, MouseButton.Left);
    window.MouseUp(textPoint, MouseButton.Left);
    Pump(120);
    window.MouseDown(textPoint, MouseButton.Left);
    window.MouseUp(textPoint, MouseButton.Left);
    Pump(500);
    Console.WriteLine(
        $"double-click opens the group: {d.Selection.Count} selected (expected 1), "
        + $"and it is the member under the pointer: {d.Selection.Primary == gRight} (expected True)");

    window.MouseDown(textPoint, MouseButton.Left);
    window.MouseUp(textPoint, MouseButton.Left);
    Pump(120);
    window.MouseDown(textPoint, MouseButton.Left);
    window.MouseUp(textPoint, MouseButton.Left);
    Pump(600);
    var groupFocus = Avalonia.Controls.TopLevel.GetTopLevel(canvas)?.FocusManager?.GetFocusedElement();
    Console.WriteLine(
        "a second double-click reaches the text: "
        + $"'{(groupFocus is Avalonia.Controls.TextBox gtb ? gtb.SelectedText : "not a text box")}' "
        + "(expected 'in a group')");

    // Escape steps back out and leaves the whole group selected.
    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
    Pump(300);
    canvas.Focus();
    Pump(200);
    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
    Pump(400);
    Console.WriteLine(
        $"escape leaves the group: {d.Selection.Count} selected (expected 2)");

    // One locked member holds all of it, because a group that half moves is not a group.
    gLeft.IsLocked = true;
    d.NotifyDocumentEdited();
    Pump(700);
    int heldLeft = gLeft.X;
    int heldRight = gRight.X;
    var heldPoint = canvas.TranslatePoint(canvas.DotsToView(gLeft.X + 60, gLeft.Y + 40), window)!.Value;
    window.MouseDown(heldPoint, MouseButton.Left);
    window.MouseMove(new Avalonia.Point(heldPoint.X + 60, heldPoint.Y));
    window.MouseUp(new Avalonia.Point(heldPoint.X + 60, heldPoint.Y), MouseButton.Left);
    Pump(700);
    Console.WriteLine(
        $"a locked member holds the group: left {gLeft.X} (expected {heldLeft}), "
        + $"right {gRight.X} (expected {heldRight})");
    gLeft.IsLocked = false;
    d.NotifyDocumentEdited();
    Pump(400);

    // A copy of a group is its own group rather than more members of the original.
    d.Selection.SetMany([gLeft, gRight]);
    Pump(200);
    d.DuplicateCommand.Execute(null);
    Pump(700);
    var copies = d.Selection.Items.ToList();
    Console.WriteLine(
        $"a duplicated group is its own: {copies.Count} copies (expected 2), "
        + $"same id as each other: {copies.Count == 2 && copies[0].GroupId == copies[1].GroupId} "
        + $"(expected True), different from the original: {copies[0].GroupId != gLeft.GroupId} "
        + "(expected True)");
    d.UndoCommand.Execute(null);
    Pump(700);

    // The outline lists a group as a header with its members under it.
    Console.WriteLine(
        $"the outline shows the group: {d.Outline.Count} rows for 3 elements (expected 4), "
        + $"header reads '{d.Outline.FirstOrDefault(r => r.IsGroupHeader)?.Display}' "
        + "(expected Group of 2)");

    // Ungroup puts them back to being separate things.
    d.Selection.SetMany(d.Document.Elements.Where(el => el.GroupId is not null).ToList());
    canvas.Focus();
    Pump(300);
    window.KeyPress(
        Key.G, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.G, "g");
    Pump(700);
    Console.WriteLine(
        $"ctrl+shift+g ungroups: {d.Document.Elements.Count(el => el.GroupId is not null)} still grouped "
        + "(expected 0)");

    d.Selection.Clear();
    Pump(200);

    // Aligning to the LABEL rather than to the rest of the selection, and a size that can
    // be copied from one element to the others.
    d.Document.Elements.Clear();
    var aLeft = new LabelForge.Core.Model.BoxElement
    {
        X = 300, Y = 100, WidthDots = 100, HeightDots = 50, ThicknessDots = 3,
    };
    var aRight = new LabelForge.Core.Model.BoxElement
    {
        X = 500, Y = 200, WidthDots = 240, HeightDots = 120, ThicknessDots = 3,
    };
    d.Document.Elements.Add(aLeft);
    d.Document.Elements.Add(aRight);
    d.Selection.SetMany([aLeft, aRight]);
    d.NotifyDocumentEdited();
    Pump(700);

    d.AlignLeftCommand.Execute(null);
    Pump(500);
    Console.WriteLine(
        $"align to selection: {aLeft.X},{aRight.X} (expected both at 300, the leftmost)");

    d.AlignToLabel = true;
    d.AlignLeftCommand.Execute(null);
    Pump(500);
    Console.WriteLine(
        $"align to label: {aLeft.X},{aRight.X} (expected both at 0), "
        + $"and the setting stuck: {File.Exists(settingsPath)} (expected True)");

    d.CenterOnLabelCommand.Execute(null);
    Pump(500);
    Console.WriteLine(
        $"center on label: first at {aLeft.X},{aLeft.Y} on an "
        + $"{d.Document.WidthDots} x {d.Document.HeightDots} dot label (expected both axes centred)");

    // Same size copies from the LAST element picked, which is the one a person controls.
    d.Selection.SetMany([aLeft, aRight]);
    Pump(300);
    d.MatchSizeCommand.Execute(null);
    Pump(500);
    Console.WriteLine(
        $"same size takes the last picked: {aLeft.WidthDots}x{aLeft.HeightDots} "
        + $"(expected {aRight.WidthDots}x{aRight.HeightDots})");

    d.AlignToLabel = false;

    // A size can be typed in millimetres, the same toggle X and Y already answer to. The
    // model stays in dots whatever the panel is showing.
    d.Selection.Set(aLeft);
    Pump(400);
    var sizeEditor = (LabelForge.App.ViewModels.BoxPropertiesViewModel)d.SelectionProperties!;
    sizeEditor.UseMm = true;
    Console.WriteLine(
        $"size shows in mm: {sizeEditor.BoxWidth} mm for {aLeft.WidthDots} dots at "
        + $"{d.Document.Dpmm} dpmm (expected {aLeft.WidthDots / d.Document.Dpmm}), "
        + $"label reads '{sizeEditor.UnitSuffix}' (expected mm)");

    sizeEditor.BoxWidth = 25;
    Pump(500);
    Console.WriteLine(
        $"and a size typed in mm lands in dots: {aLeft.WidthDots} "
        + $"(expected {25 * d.Document.Dpmm})");

    sizeEditor.UseMm = false;
    Console.WriteLine(
        $"back in dots: {sizeEditor.BoxWidth} (expected {aLeft.WidthDots}), "
        + $"label reads '{sizeEditor.UnitSuffix}' (expected dots)");

    d.Selection.Clear();
    Pump(300);

    // Snap toggles. A grid line to land on, and a drag that ends three dots away from one.
    d.Document.Elements.Clear();
    d.GridPitchMm = 5;
    var snapBox = new LabelForge.Core.Model.BoxElement
    {
        X = 200, Y = 200, WidthDots = 80, HeightDots = 60, ThicknessDots = 3,
    };
    d.Document.Elements.Add(snapBox);
    d.Selection.Set(snapBox);
    d.NotifyDocumentEdited();
    Pump(700);

    var snapFrom = canvas.TranslatePoint(canvas.DotsToView(240, 230), window)!.Value;
    double dotsPerPx = 40.0 / (canvas.DotsToView(240, 230).X - canvas.DotsToView(200, 230).X);
    var snapTo = new Avalonia.Point(snapFrom.X + (37 / dotsPerPx), snapFrom.Y);
    window.MouseDown(snapFrom, MouseButton.Left);
    window.MouseMove(snapTo);
    window.MouseUp(snapTo, MouseButton.Left);
    Pump(700);
    Console.WriteLine(
        $"with grid snapping on, a drag lands on a line: x {snapBox.X} "
        + $"(expected a multiple of {5 * d.Document.Dpmm})");

    d.SnapToGrid = false;
    Pump(300);
    int offGridStart = snapBox.X;
    var offFrom = canvas.TranslatePoint(canvas.DotsToView(snapBox.X + 40, 230), window)!.Value;
    var offTo = new Avalonia.Point(offFrom.X + (37 / dotsPerPx), offFrom.Y);
    window.MouseDown(offFrom, MouseButton.Left);
    window.MouseMove(offTo);
    window.MouseUp(offTo, MouseButton.Left);
    Pump(700);
    Console.WriteLine(
        $"with it off, the same drag does not: moved {snapBox.X - offGridStart} dots "
        + "(expected 37, the distance dragged)");
    Console.WriteLine(
        $"and the status line says which are on: '{d.SnapSummary}' "
        + "(expected guides and objects, not grid)");

    d.SnapToGrid = true;
    d.GridPitchMm = 0;
    d.Selection.Clear();
    Pump(300);

    // Keyboard zoom, about the middle of the view rather than the pointer, and actual size.
    canvas.SetZoom(1);
    canvas.Focus();
    Pump(300);
    double zoomBefore = canvas.GetZoom();
    window.KeyPress(Key.OemPlus, RawInputModifiers.Control, PhysicalKey.Equal, "=");
    Pump(400);
    double zoomedIn = canvas.GetZoom();
    window.KeyPress(Key.OemMinus, RawInputModifiers.Control, PhysicalKey.Minus, "-");
    Pump(400);
    // Invariant, or this line reads 1,00 here and 1.00 on anyone else's machine.
    static string Zoom(double value) =>
        value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    Console.WriteLine(
        $"ctrl+= and ctrl+- zoom: {Zoom(zoomBefore)} -> {Zoom(zoomedIn)} -> {Zoom(canvas.GetZoom())} "
        + "(expected up then back)");

    // The same two keys with NO character, which is the case the fallback binding exists for
    // and the one a headless run can produce on demand. The check above sends "=" and "-", so
    // it never reaches that binding.
    canvas.SetZoom(1);
    Pump(300);
    window.KeyPress(Key.OemPlus, RawInputModifiers.Control, PhysicalKey.Equal, null);
    Pump(400);
    double mute = canvas.GetZoom();
    window.KeyPress(Key.OemMinus, RawInputModifiers.Control, PhysicalKey.Minus, null);
    Pump(400);
    Console.WriteLine(
        $"and with no character at all: 1.00 -> {Zoom(mute)} -> {Zoom(canvas.GetZoom())} "
        + "(expected up then back, on the key binding rather than the character)");

    canvas.ZoomBy(2);
    Pump(300);
    window.KeyPress(Key.D1, RawInputModifiers.Control, PhysicalKey.Digit1, "1");
    Pump(400);
    Console.WriteLine(
        $"ctrl+1 is actual size: {Zoom(canvas.GetZoom())} (expected 1.00)");

    // Space turns a left drag into a pan, for a mouse with no middle button.
    double panOriginBefore = canvas.DotsToView(0, 0).X;
    var spaceFrom = canvas.TranslatePoint(canvas.DotsToView(200, 200), window)!.Value;
    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
    window.MouseDown(spaceFrom, MouseButton.Left);
    window.MouseMove(new Avalonia.Point(spaceFrom.X - 40, spaceFrom.Y));
    window.MouseUp(new Avalonia.Point(spaceFrom.X - 40, spaceFrom.Y), MouseButton.Left);
    Pump(500);
    Console.WriteLine(
        $"space turns a drag into a pan: label origin {panOriginBefore:0} -> "
        + $"{canvas.DotsToView(0, 0).X:0} (expected to have moved left), "
        + $"and nothing was selected: {d.Selection.Count == 0} (expected True)");

    canvas.ResetView();
    Pump(300);

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

    // And back in again: ^SN is where a serialized field's data lives, so reading it is
    // what stops the whole field vanishing on import. The counter lands in the panel with
    // the numbers it left with, under a name of the importer's own since ZPL never
    // states one.
    d.ImportZplDocument(byPrinter.Zpl, "counter.zpl");
    Pump(900);
    var serial = d.Variables.FirstOrDefault(
        v => v.Kind == LabelForge.Core.Model.VariableKind.Counter);
    Console.WriteLine(
        $"^SN read back: {d.Document.Elements.Count} element(s), counter="
        + $"{serial?.Name ?? "none"} start={serial?.CounterStart} "
        + $"pad={serial?.CounterPadding} printer={serial?.UsePrinterCounter} "
        + "(expected 1 element, SERIAL 41 4 True)");

    // Multi-across stock. The design stays one label and the canvas keeps drawing one, so
    // the checks that matter are on the run: the columns, the web width, and the fact that
    // a quantity which does not divide by the column count prints a few extra.
    d.NewDocumentCommand.Execute(null);
    d.WidthMm = 25m;
    d.HeightMm = 20m;
    d.AddBoxCommand.Execute(null);
    d.PlaceAt(10, 10);
    d.LabelsAcross = 3m;
    d.AcrossGapMm = 3m;
    d.PrintCopies = 10;
    d.NotifyDocumentEdited();
    Pump(900);
    var web = d.BuildPrintJob();
    Console.WriteLine(
        $"across: {d.AcrossHint}"
        + $" | web {d.Document.WebWidthMm:0.#} mm (expected 81)");
    Console.WriteLine(
        $"across run: {Count(web.Zpl, "^GB")} columns (expected 3), "
        + $"^PW648={web.Zpl.Contains("^PW648")} ^PQ4={web.Zpl.Contains("^PQ4")} "
        + $"labels={web.Labels} (expected True True 12)");
    Console.WriteLine(
        $"across warns about the overshoot: "
        + $"{web.Warnings.Any(w => w.Contains("prints 12 labels", StringComparison.Ordinal))} (expected True)");

    // The pane still shows the single label, which is what keeps the round trip true.
    Console.WriteLine(
        $"pane stays one label: {Count(d.GeneratedZpl, "^GB") == 1} (expected True), "
        + $"^PW200={d.GeneratedZpl.Contains("^PW200")} (expected True)");

    // Each is its own undo step, like every other document edit: the gap comes off
    // without taking the columns with it.
    d.UndoCommand.Execute(null);
    d.UndoCommand.Execute(null);
    Console.WriteLine(
        $"gap undone on its own: {d.LabelsAcross} across, gap {d.AcrossGapMm} (expected 3 0)");
    d.UndoCommand.Execute(null);
    Console.WriteLine($"across undone: {d.LabelsAcross} across (expected 1)");

    // A 4-across stock from the catalog brings its own web with it, which is the mistake
    // this exists to prevent: designing on stock whose other three columns print blank.
    var acrossMedia = LabelForge.Core.Media.StockCatalog.All.First(m => m.Across == 4);
    d.SelectedMedia = acrossMedia;
    Pump(300);
    string acrossGap = FormattableString.Invariant($"{d.AcrossGapMm:0.##}");
    Console.WriteLine(
        $"media {acrossMedia.PartNumber}: {d.LabelsAcross} across, gap {acrossGap} mm, "
        + $"multi={d.IsMultiAcross} (expected 4 3.18 True)");

    // And the printhead check measures the web, not the label: each column fits on its own.
    d.SelectedPrinter = d.Printers.First(p => p.Id == "zd421-203");
    d.WidthMm = 40m;
    d.LabelsAcross = 3m;
    Pump(300);
    Console.WriteLine(
        $"printhead sees the web: {d.PrinterWarning.Contains("Web width", StringComparison.Ordinal)} "
        + "(expected True)");

    // What happens to the media after a label prints. The unit tests cover the bytes; what
    // they cannot reach is the panel, where the point is that choosing a mode is an ordinary
    // undoable edit and that the cut hint answers for the mode beside it rather than for the
    // number alone.
    d.NewDocumentCommand.Execute(null);
    d.WidthMm = 50m;
    d.HeightMm = 30m;
    d.AddTextCommand.Execute(null);
    d.PlaceAt(10, 10);
    d.PrintCopies = 20;
    d.PrintCutAfter = 5;
    Pump(300);
    Console.WriteLine(
        $"a cut group with no cutter: hint mentions the cutter="
        + $"{d.CutHint.Contains("cutter", StringComparison.Ordinal)} (expected True)");

    d.SelectedMediaHandling = d.MediaHandlingOptions.First(
        o => o.Value == LabelForge.Core.Model.MediaHandling.Cutter);
    Pump(300);
    var cutJob = d.BuildPrintJob();
    Console.WriteLine(
        $"cutter picked: ^MMC={cutJob.Zpl.Contains("^MMC", StringComparison.Ordinal)} "
        + $"^PQ20,5,0,Y={cutJob.Zpl.Contains("^PQ20,5,0,Y", StringComparison.Ordinal)} "
        + $"hint says it cuts={d.CutHint.Contains("cuts after each group", StringComparison.Ordinal)} "
        + "(expected True True True)");

    // Peel-off is the only mode with anything to pre-peel, so the checkbox appears with it
    // and with nothing else.
    d.SelectedMediaHandling = d.MediaHandlingOptions.First(
        o => o.Value == LabelForge.Core.Model.MediaHandling.PeelOff);
    Pump(200);
    bool peelShows = d.IsPeelOff;
    d.PrintPrepeel = true;
    d.SelectedMediaHandling = d.MediaHandlingOptions.First(
        o => o.Value == LabelForge.Core.Model.MediaHandling.TearOff);
    Pump(200);
    Console.WriteLine(
        $"prepeel is offered for the peeler only: {peelShows} then {d.IsPeelOff} "
        + "(expected True then False)");

    // And the mode is an undo step of its own, like every other document edit.
    d.UndoCommand.Execute(null);
    Pump(200);
    Console.WriteLine(
        $"mode undone on its own: {d.SelectedMediaHandling.Value} (expected PeelOff)");

    // ^LT rides everything that reaches a printer, which is the pane's exported label and
    // the print job alike. What it must stay out of is the canvas underlay, and that is
    // GeneratePreview rather than anything reachable from here, so a unit test pins it.
    d.PrintLabelTop = 15;
    Pump(500);
    Console.WriteLine(
        $"label top rides what prints: "
        + $"job={d.BuildPrintJob().Zpl.Contains("^LT15", StringComparison.Ordinal)} "
        + $"pane={d.GeneratedZpl.Contains("^LT15", StringComparison.Ordinal)} "
        + "(expected True True)");

    // The two label-wide options, and the difference between them is the thing worth
    // reaching from here: reverse is ink the canvas can draw, so the underlay has to
    // change; mirror is a transform the engine does not implement, so the canvas keeps
    // the picture and the panel says which side it is showing.
    d.NewDocumentCommand.Execute(null);
    d.WidthMm = 50m;
    d.HeightMm = 30m;
    d.Document.Elements.Add(new LabelForge.Core.Model.BoxElement
    {
        X = 20, Y = 20, WidthDots = 240, HeightDots = 80, ThicknessDots = 80,
    });
    d.Document.Elements.Add(new LabelForge.Core.Model.TextElement
    {
        X = 40, Y = 40, Text = "REVERSE", FontHeightDots = 40, ZOrder = 1,
    });
    d.NotifyDocumentEdited();
    Pump(700);
    var beforeReverse = d.Underlay;

    d.PrintReverseAll = true;
    Pump(700);
    Console.WriteLine(
        $"reverse redraws the canvas: {!ReferenceEquals(d.Underlay, beforeReverse) && d.Underlay is not null} "
        + $"preview carries it={d.GeneratedZpl.Contains("^LRY", StringComparison.Ordinal)} "
        + "(expected True True)");

    var beforeMirror = d.Underlay;
    d.PrintMirror = true;
    Pump(700);
    Console.WriteLine(
        $"mirror leaves the picture alone: {ReferenceEquals(d.Underlay, beforeMirror)} "
        + $"and says so={d.MirrorHint.Length > 0} "
        + $"job={d.BuildPrintJob().Zpl.Contains("^PMY", StringComparison.Ordinal)} "
        + "(expected True True True)");

    d.UndoCommand.Execute(null);
    Pump(300);
    Console.WriteLine(
        $"mirror undone on its own: {!d.PrintMirror} still reversed={d.PrintReverseAll} "
        + "(expected True True)");

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

    // Opening a file the app was started with: a double-clicked label, "Open with", or a
    // file dropped on the executable. The kind decides the tab, because a label is what the
    // designer edits and a ZPL file is what the viewer reads.
    d.NewDocumentCommand.Execute(null);
    Pump(200);
    string startupLabel = Path.Combine(AppContext.BaseDirectory, "e2e-startup.lfl");
    d.Document.Elements.Add(new LabelForge.Core.Model.TextElement
    {
        X = 30, Y = 30, Text = "opened at startup", FontHeightDots = 30,
    });
    File.WriteAllText(startupLabel, d.SerializeDocument());
    d.NewDocumentCommand.Execute(null);
    Pump(300);

    vm.OpenStartupFile(new LabelForge.Core.Io.StartupFile(
        startupLabel, LabelForge.Core.Io.StartupFileKind.Label));
    Pump(600);
    Console.WriteLine(
        $"startup .lfl: tab={tabs.SelectedIndex} elements={d.Document.Elements.Count} "
        + $"recent={d.RecentFiles.Contains(startupLabel)} (expected 0 1 True)");

    vm.OpenStartupFile(new LabelForge.Core.Io.StartupFile(
        FindGraphicSource(), LabelForge.Core.Io.StartupFileKind.Zpl));
    Pump(600);
    Console.WriteLine(
        $"startup .zpl: tab={tabs.SelectedIndex} "
        + $"loaded={vm.Viewer.ZplText.Contains("^XA")} (expected 1 True)");

    vm.OpenStartupFile(new LabelForge.Core.Io.StartupFile(
        @"C:\nowhere\logo.png", LabelForge.Core.Io.StartupFileKind.Unsupported));
    Pump(300);
    Console.WriteLine(
        $"startup .png: tab={tabs.SelectedIndex} said={d.StatusText.Contains(".png")} "
        + "(expected 0 True)");

    // Labelary compare mode, against the offline engine standing in for the service, so
    // the harness never sends a label anywhere. Comparing a renderer with itself is the
    // one case whose answer is known in advance, which makes it a real check: anything
    // but "identical" means the comparison is measuring something other than the ink.
    tabs.SelectedIndex = 1;
    Pump(700);
    var v = vm.Viewer;
    Console.WriteLine(
        $"before comparing: HasComparison={v.HasComparison} (expected False), "
        + $"warns about sending={v.OutboundDescription.Contains("over the internet")} (expected True)");

    v.CompareCommand.Execute(null);
    Pump(2500);
    Console.WriteLine(
        $"compare: HasComparison={v.HasComparison} (expected True), image={v.ComparisonImage != null} "
        + $"(expected True), busy={v.IsComparing} (expected False)");
    Console.WriteLine($"  summary: {v.ComparisonSummary}");
    Console.WriteLine(
        $"  same renderer twice is identical: {v.ComparisonSummary.StartsWith("Identical")} (expected True)");

    v.ClearComparisonCommand.Execute(null);
    Pump(300);
    Console.WriteLine(
        $"close compare: HasComparison={v.HasComparison} (expected False), "
        + $"image={v.ComparisonImage != null} (expected False)");
    tabs.SelectedIndex = 0;
    Pump(300);

    // The .lfl shell association. Pointed at a scratch classes root for the same reason
    // the media, catalog and recovery stores are pointed at scratch files: this is
    // per-machine state, and a harness run must not touch what the user has.
    const string scratchClasses = @"Software\LabelForge.E2E\Classes";
    var association = new LabelForge.App.Services.FileAssociation(scratchClasses);
    association.Register(@"C:\Program Files\LabelForge\LabelForge.App.exe");
    string? handler = Microsoft.Win32.Registry.GetValue(
        $@"HKEY_CURRENT_USER\{scratchClasses}\.lfl", null, null) as string;
    string? verb = Microsoft.Win32.Registry.GetValue(
        $@"HKEY_CURRENT_USER\{scratchClasses}\{LabelForge.App.Services.FileAssociation.ProgId}\shell\open\command",
        null,
        null) as string;
    Console.WriteLine(
        $"association: .lfl -> {handler ?? "none"}, command quotes the path="
        + $"{verb == "\"C:\\Program Files\\LabelForge\\LabelForge.App.exe\" \"%1\""} "
        + "(expected LabelForge.Label, True)");

    association.Unregister();
    bool gone = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(scratchClasses) is not { } left
        || left.GetSubKeyNames().Length == 0;
    Console.WriteLine($"association removed on uninstall: {gone} (expected True)");
    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\LabelForge.E2E", false);

    // The starter gallery. Every card has to come back with a picture: a starter that
    // renders empty is exactly what the gallery exists to show, and a name alone would
    // hide it. Then the density check, which is the reason a starter is a layout rather
    // than a stored document: picked at 300 dpi it has to be the same physical label, so
    // the millimetres match and the dot coordinates do not.
    d.SelectedDensity = d.Densities.First(o => o.Dpmm == 12);
    Pump(400);
    var gallery = new StarterGalleryViewModel(d.Document.Dpmm);

    // Through the window rather than the view model alone: the pictures are loaded by the
    // window opening, and a gallery whose cards never fill in looks identical to a gallery
    // that has no cards.
    var galleryWindow = new StarterGalleryWindow { DataContext = gallery };
    galleryWindow.Show();
    for (int i = 0; i < 40 && gallery.Cards.Any(c => c.Preview is null); i++)
    {
        Pump(250);
    }

    Pump(400);
    Console.WriteLine(
        $"gallery: {gallery.Cards.Count} starters (expected 5), "
        + $"all drawn={gallery.Cards.All(c => c.Preview is not null)} (expected True), "
        + $"selected={gallery.Selected?.Name}");
    Capture("gallery.png", galleryWindow);
    galleryWindow.Close();
    Pump(200);

    LabelForge.Core.Starters.StarterLabel shipping = gallery.Cards[0].Starter;
    d.LoadStarter(shipping);
    Pump(600);
    var at203 = shipping.Create(8);
    Console.WriteLine(
        $"start from \"{shipping.Name}\": {d.Document.Elements.Count} elements, "
        + $"{d.Document.WidthMm} x {d.Document.HeightMm} mm at {d.Document.Dpmm} dpmm "
        + "(expected 101.6 x 152.4 at 12), "
        + $"file={d.CurrentFilePath ?? "none"} (expected none)");
    Console.WriteLine(
        $"  same label, denser dots: 300 dpi X={d.Document.Elements[1].X} vs 203 dpi "
        + $"X={at203.Elements[1].X} (expected 1.5x), "
        + $"same millimetres={Math.Abs((d.Document.Elements[1].X / 12.0) - (at203.Elements[1].X / 8.0)) < 0.1} "
        + "(expected True)");
    Console.WriteLine(
        $"  markers have samples: {d.Variables.Count} variables (expected 12), "
        + $"every one seeded={d.Variables.All(v => v.Sample.Length > 0)} (expected True)");

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

// What a key held with Ctrl actually reports as its symbol on Windows. Named rather than
// written as an escape in the call, because an invisible byte in a source line is exactly
// the kind of thing that gets copied wrong, and because the point of these two is that they
// are NOT the bracket the old check was sending.
static string ControlChar(int code) => ((char)code).ToString();

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

void Capture(string name, Window? target = null)
{
    var frame = (target ?? window).CaptureRenderedFrame();
    if (frame is null)
    {
        Console.WriteLine($"{name}: CaptureRenderedFrame returned null");
        return;
    }

    string path = Path.Combine(AppContext.BaseDirectory, name);
    frame.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    Console.WriteLine(path);
}
