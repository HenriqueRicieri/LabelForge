using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelForge.Core.Editing;
using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;

namespace LabelForge.App.ViewModels;

/// <summary>
/// The visual designer. The LabelDocument is the source of truth: the canvas and the
/// properties panel edit it, the ZPL generator turns it into code, and the offline
/// renderer turns that code into the canvas underlay (WYSIWYG rule). Rendering is
/// debounced, background, latest-wins, mirroring the viewer pipeline.
/// Undo/redo is snapshot-based: every committed edit records the serialized document
/// (the same JSON as the .lfl file format); bursts of small edits coalesce into one step.
/// </summary>
public partial class DesignerViewModel : ViewModelBase
{
    private const int CoalesceWindowMs = 500;

    private readonly IZplRenderer _renderer = new BinaryKitsRenderer();
    private readonly TemplateSubstitutor _substitutor = new();
    private readonly Core.Media.UserMediaStore _userMediaStore;
    private readonly SnapshotHistory _history = new();
    private LabelDocument? _variablesDocument;
    private CancellationTokenSource? _renderCts;
    private bool _restoring;
    private long _lastRecordTicks;
    private string? _lastCoalesceKey;
    private string? _clipboardElement;

    public IReadOnlyList<DensityOption> Densities => DensityOption.Standard;

    [ObservableProperty]
    public partial LabelDocument Document { get; set; }

    [ObservableProperty]
    public partial Bitmap? Underlay { get; set; }

    /// <summary>Pasteboard margin baked into the underlay bitmap, in dots. 0 when the
    /// underlay is the plain label render; positive when something sits off the label
    /// and the render was expanded so that content stays visible.</summary>
    [ObservableProperty]
    public partial int UnderlayMarginDots { get; set; }

    [ObservableProperty]
    public partial string GeneratedZpl { get; set; } = string.Empty;

    /// <summary>Shared selection, mutated by the canvas and by commands here.</summary>
    public SelectionSet Selection { get; } = new();

    /// <summary>The primary selected element (last selected).</summary>
    public Element? SelectedElement => Selection.Primary;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand))]
    [NotifyCanExecuteChangedFor(nameof(BringToFrontCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendToBackCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlignLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlignCenterHorizontalCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlignRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlignTopCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlignMiddleCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlignBottomCommand))]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DistributeHorizontalCommand))]
    [NotifyCanExecuteChangedFor(nameof(DistributeVerticalCommand))]
    public partial int SelectionCount { get; set; }

    [ObservableProperty]
    public partial bool IsSingleSelection { get; set; }

    [ObservableProperty]
    public partial bool HasMultiSelection { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PasteCommand))]
    public partial bool CanPaste { get; set; }

    /// <summary>Per-type property editor for the selection; DataTemplates pick the view.</summary>
    [ObservableProperty]
    public partial ElementPropertiesViewModel? SelectionProperties { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewMediaSizeText))]
    public partial decimal WidthMm { get; set; } = 100m;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewMediaSizeText))]
    public partial decimal HeightMm { get; set; } = 60m;

    /// <summary>What the media picker searches: the user's own presets first, then the
    /// official Zebra catalog. Replaced wholesale when presets change, rather than
    /// mutated, because the catalog is 797 entries and the picker only needs the list.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Core.Media.StockMedia> MediaCatalog { get; set; } = [];

    /// <summary>The user's saved media definitions, each with its own remove command.</summary>
    public ObservableCollection<UserMediaEntryViewModel> UserMedia { get; } = [];

    public bool HasUserMedia => UserMedia.Count > 0;

    [ObservableProperty]
    public partial string NewMediaName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewMediaMaterial { get; set; } = string.Empty;

    /// <summary>Die-cut corner radius recorded with a preset. Descriptive for now, like
    /// the radius the Zebra catalog already carries; it starts affecting the drawn
    /// outline when the document gains a corner radius (backlog A2).</summary>
    [ObservableProperty]
    public partial decimal NewMediaRadiusMm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewMediaSizeText))]
    public partial bool NewMediaContinuous { get; set; }

    /// <summary>The size saving would record right now, shown so it is confirmed before
    /// it is stored rather than after.</summary>
    public string NewMediaSizeText => Core.Media.StockMedia.FormatSize(
        (double)WidthMm, (double)HeightMm, NewMediaContinuous);

    /// <summary>Template variables found on the label, with editable preview samples.
    /// Rebuilt only when the variable set or the document instance changes, so typing
    /// in a sample box never loses focus to a refresh.</summary>
    public ObservableCollection<VariableSampleViewModel> Variables { get; } = [];

    public bool HasVariables => Variables.Count > 0;

    /// <summary>Recently opened or saved .lfl paths, newest first.</summary>
    public ObservableCollection<string> RecentFiles { get; } = new(Services.RecentFilesStore.Load());

    public bool HasRecentFiles => RecentFiles.Count > 0;

    /// <summary>Copies to print (^PQ); job settings live on the document.</summary>
    public decimal PrintCopies
    {
        get => Document.Print.Copies;
        set => EditPrintSetting((int)value, 1, 9999, Document.Print.Copies,
            v => Document.Print.Copies = v, "print-copies");
    }

    /// <summary>Darkness adjustment (^MD), -30..30; 0 keeps the printer default.</summary>
    public decimal PrintDarkness
    {
        get => Document.Print.DarknessDelta;
        set => EditPrintSetting((int)value, -30, 30, Document.Print.DarknessDelta,
            v => Document.Print.DarknessDelta = v, "print-darkness");
    }

    /// <summary>Print speed (^PR) in inches per second; 0 keeps the printer default
    /// (the generator clamps emitted values to the ^PR 2..14 range).</summary>
    public decimal PrintSpeed
    {
        get => Document.Print.SpeedIps;
        set => EditPrintSetting((int)value, 0, 14, Document.Print.SpeedIps,
            v => Document.Print.SpeedIps = v, "print-speed");
    }

    private void EditPrintSetting(int value, int min, int max, int current, Action<int> apply,
        string undoKey, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        int next = Math.Clamp(value, min, max);
        if (next == current)
        {
            return;
        }

        apply(next);
        OnPropertyChanged(property);
        if (!_restoring)
        {
            RecordUndo(undoKey);
            ScheduleRender();
        }
    }

    /// <summary>Media picked from the catalog; applying it sets the label size.
    /// Cleared when the size is edited by hand so the field never lies.</summary>
    [ObservableProperty]
    public partial Core.Media.StockMedia? SelectedMedia { get; set; }

    [ObservableProperty]
    public partial DensityOption? SelectedDensity { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Path of the open .lfl file; null until first save.</summary>
    [ObservableProperty]
    public partial string? CurrentFilePath { get; set; }

    [ObservableProperty]
    public partial string PrinterHost { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal PrinterPort { get; set; } = Core.Printing.RawNetworkPrinter.DefaultPort;

    public IReadOnlyList<Core.Printers.PrinterProfile> Printers => Core.Printers.PrinterCatalog.All;

    /// <summary>Printer queues installed in Windows (USB path); empty elsewhere.</summary>
    public IReadOnlyList<string> WindowsPrinters { get; } = LoadWindowsPrinters();

    [ObservableProperty]
    public partial string? SelectedWindowsPrinter { get; set; }

    [ObservableProperty]
    public partial Core.Printers.PrinterProfile? SelectedPrinter { get; set; }

    /// <summary>Head-width/density warnings for the selected printer; empty when fine.</summary>
    [ObservableProperty]
    public partial string PrinterWarning { get; set; } = string.Empty;

    /// <summary>Document-wide barcode validation summary; empty when every barcode is
    /// encodable. Refreshed on each render so it tracks edits without being selected.</summary>
    [ObservableProperty]
    public partial string ValidationWarning { get; set; } = string.Empty;

    /// <summary>Elements off the label or crossing its edge, summarized for display;
    /// empty when everything sits inside. Refreshed on each render.</summary>
    [ObservableProperty]
    public partial string PlacementWarning { get; set; } = string.Empty;

    /// <summary>Why a printer-side counter or clock the document asked for is not being
    /// used. Empty when everything the label requested is expressible in ZPL, which is
    /// the normal case; a fallback still prints correctly, just more slowly.</summary>
    [ObservableProperty]
    public partial string VariableWarning { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool CanUndo { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    public partial bool CanRedo { get; set; }

    /// <param name="userMediaStore">Where the user's own media presets live; defaults
    /// to the per-user file. Injected so a harness can exercise presets without
    /// touching what the person using the app has saved.</param>
    public DesignerViewModel(Core.Media.UserMediaStore? userMediaStore = null)
    {
        _userMediaStore = userMediaStore ?? new Core.Media.UserMediaStore();
        Selection.Changed += (_, _) => OnSelectionChanged();

        ApplyUserMedia(_userMediaStore.Load());

        // Property setters record undo states; construction must not, or the
        // history would start with a spurious extra document before the baseline.
        _restoring = true;
        Document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        SelectedDensity = Densities[0];
        SelectedPrinter = Core.Printers.PrinterProfile.Any;
        _restoring = false;

        RecordUndo();
        ScheduleRender();
    }

    /// <summary>Called continuously while the canvas drags or resizes: the model is
    /// already updated, so re-render and refresh the panel, but record no undo.
    /// Uses a much shorter debounce than typing so content tracks the pointer.</summary>
    public void NotifyDocumentPreview()
    {
        SelectionProperties?.Refresh();
        ScheduleRender(delayMs: 40);
    }

    private void OnSelectionChanged()
    {
        HasSelection = Selection.Count > 0;
        SelectionCount = Selection.Count;
        IsSingleSelection = Selection.Count == 1;
        HasMultiSelection = Selection.Count > 1;
        SelectionProperties = Selection.Count == 1
            ? CreatePropertiesEditor(Selection.Primary)
            : null;
    }

    /// <summary>Serializes the current document in the .lfl format.</summary>
    public string SerializeDocument() => LabelDocumentJson.Serialize(Document);

    /// <summary>Renders the current document to PNG bytes (for export).</summary>
    public Task<byte[]> RenderPngAsync()
    {
        LabelDocument document = Document;
        return Task.Run(() => _renderer
            .Render(new ZplGenerator().Generate(document), document.WidthMm, document.HeightMm, document.Dpmm)
            .Png);
    }

    /// <summary>Renders the current document to a PDF page at its physical size.</summary>
    public Task<byte[]> RenderPdfAsync()
    {
        LabelDocument document = Document;
        return Task.Run(() =>
        {
            byte[] png = _renderer
                .Render(new ZplGenerator().Generate(document), document.WidthMm, document.HeightMm, document.Dpmm)
                .Png;
            return Core.Export.PdfExporter.FromPng(png, document.WidthMm, document.HeightMm);
        });
    }

    /// <summary>Replaces the document (new file or opened .lfl) and resets history.</summary>
    public void LoadDocument(LabelDocument document, string? path)
    {
        _restoring = true;
        try
        {
            Document = document;
            WidthMm = (decimal)document.WidthMm;
            HeightMm = (decimal)document.HeightMm;
            SelectedMedia = null;
            SelectedDensity = Densities.FirstOrDefault(d => d.Dpmm == document.Dpmm) ?? Densities[0];
            Selection.Clear();
        }
        finally
        {
            _restoring = false;
        }

        CurrentFilePath = path;
        NotifyPrintSettingsChanged();
        RefreshVariables();
        UpdatePrinterWarning();
        _history.Clear();
        _lastRecordTicks = 0;
        _lastCoalesceKey = null;
        RecordUndo();
        ScheduleRender();
    }

    private void NotifyPrintSettingsChanged()
    {
        OnPropertyChanged(nameof(PrintCopies));
        OnPropertyChanged(nameof(PrintDarkness));
        OnPropertyChanged(nameof(PrintSpeed));
    }

    [RelayCommand]
    private void NewDocument() =>
        LoadDocument(new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 }, path: null);

    /// <summary>A demo label showing each element type; reachable from File.</summary>
    [RelayCommand]
    private void LoadSample()
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        SeedSampleLabel(doc);
        LoadDocument(doc, path: null);
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        string host = PrinterHost.Trim();
        if (host.Length == 0)
        {
            StatusText = "Enter the printer address first";
            return;
        }

        try
        {
            StatusText = $"Sending to {host}...";
            PrintJobResult job = PrintJob.Build(Document, DateTime.Now);

            // The connection phase is bounded inside SendAsync; a timeout surfaces as a
            // TimeoutException whose message already names the unreachable endpoint.
            await Core.Printing.RawNetworkPrinter.SendAsync(host, (int)PrinterPort, job.Zpl);
            StatusText = $"Sent to {host}:{(int)PrinterPort}{DescribeRun(job)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Print failed: {ex.Message}";
        }
    }

    private static IReadOnlyList<string> LoadWindowsPrinters()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            return Core.Printing.WindowsRawPrinter.GetInstalledPrinters();
        }
        catch
        {
            return [];
        }
    }

    [RelayCommand]
    private async Task PrintWindowsAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            StatusText = "Windows printing is only available on Windows";
            return;
        }

        if (SelectedWindowsPrinter is not { Length: > 0 } name)
        {
            StatusText = "Pick a Windows printer first";
            return;
        }

        try
        {
            StatusText = $"Spooling to {name}...";
            PrintJobResult job = PrintJob.Build(Document, DateTime.Now);
            await SendToWindowsPrinterAsync(name, job.Zpl);
            StatusText = $"Sent to {name}{DescribeRun(job)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Print failed: {ex.Message}";
        }
    }

    /// <summary>Trailing summary of what a run produced, so "sent" also says how many
    /// labels went out and whether the printer or this PC numbered them.</summary>
    private string DescribeRun(PrintJobResult job)
    {
        if (job.Labels <= 1)
        {
            return string.Empty;
        }

        string capped = job.Labels < Document.Print.Copies
            ? $", capped from {Document.Print.Copies}"
            : string.Empty;
        string numbering = job.CountedByPrinter ? ", serialized by the printer" : string.Empty;
        return $" ({job.Labels} labels{numbering}{capped})";
    }

    /// <summary>Guarded so the platform-specific spooler call has a Windows-only context;
    /// callers reach it only after an <see cref="OperatingSystem.IsWindows"/> check.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Task SendToWindowsPrinterAsync(string name, string zpl) =>
        Task.Run(() => Core.Printing.WindowsRawPrinter.Send(name, zpl));

    /// <summary>Called by the view when the canvas edits the model (drag, resize, nudge).</summary>
    public void NotifyDocumentEdited()
    {
        SelectionProperties?.Refresh();
        // Key on the set of moved elements so a continuous drag or a run of nudges of
        // the same selection coalesces, but editing a different selection does not.
        string key = "canvas:" + string.Join(",", Selection.Items.Select(e => e.Id));
        RecordUndo(key);
        ScheduleRender();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_history.Undo() is { } state)
        {
            RestoreSnapshot(state);
        }

        UpdateUndoState();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_history.Redo() is { } state)
        {
            RestoreSnapshot(state);
        }

        UpdateUndoState();
    }

    /// <summary>True while an insert is armed; the next canvas click places it.</summary>
    [ObservableProperty]
    public partial bool IsPlacing { get; set; }

    /// <summary>Name of the armed insert tool ("Text", "Box", ...), null when none.
    /// Drives the highlighted state of the tool buttons in the sidebar.</summary>
    [ObservableProperty]
    public partial string? ArmedTool { get; set; }

    partial void OnArmedToolChanged(string? value)
    {
        OnPropertyChanged(nameof(IsTextArmed));
        OnPropertyChanged(nameof(IsBoxArmed));
        OnPropertyChanged(nameof(IsLineArmed));
        OnPropertyChanged(nameof(IsBarcodeArmed));
        OnPropertyChanged(nameof(IsQrArmed));
        OnPropertyChanged(nameof(IsDataMatrixArmed));
        OnPropertyChanged(nameof(IsImageArmed));
    }

    public bool IsTextArmed => ArmedTool == "Text";

    public bool IsBoxArmed => ArmedTool == "Box";

    public bool IsLineArmed => ArmedTool == "Line";

    public bool IsBarcodeArmed => ArmedTool == "Barcode";

    public bool IsQrArmed => ArmedTool == "QR";

    public bool IsDataMatrixArmed => ArmedTool == "DataMatrix";

    public bool IsImageArmed => ArmedTool == "Image";

    private Func<Element>? _pendingFactory;

    [RelayCommand]
    private void AddText() => ArmInsert("Text",
        () => new TextElement { Text = "New text", FontHeightDots = 40 });

    [RelayCommand]
    private void AddBox() => ArmInsert("Box",
        () => new BoxElement { WidthDots = 240, HeightDots = 140, ThicknessDots = 3 });

    [RelayCommand]
    private void AddLine() => ArmInsert("Line",
        () => new LineElement { LengthDots = 240, ThicknessDots = 3 });

    [RelayCommand]
    private void AddBarcode() => ArmInsert("Barcode",
        () => new BarcodeElement { Data = "123456", HeightDots = 100, ModuleWidthDots = 2 });

    [RelayCommand]
    private void AddQr() => ArmInsert("QR",
        () => new QrCodeElement { Data = "https://example.com", Magnification = 5 });

    [RelayCommand]
    private void AddDataMatrix() => ArmInsert("DataMatrix",
        () => new DataMatrixElement { Data = "LF-000123", ModuleSizeDots = 4 });

    /// <summary>Arms image placement with an already-picked file (the file dialog
    /// runs in the view). The image scales to fit a 240-dot box, keeping aspect.</summary>
    public void ArmInsertImage(byte[] imageData, int pixelWidth, int pixelHeight)
    {
        const int fitDots = 240;
        double scale = Math.Min(
            (double)fitDots / Math.Max(pixelWidth, 1),
            (double)fitDots / Math.Max(pixelHeight, 1));
        ArmInsert("Image", () => new ImageElement
        {
            ImageData = imageData,
            SourcePixelWidth = pixelWidth,
            SourcePixelHeight = pixelHeight,
            WidthDots = Math.Max((int)Math.Round(pixelWidth * scale), 8),
            HeightDots = Math.Max((int)Math.Round(pixelHeight * scale), 8),
        });
    }

    /// <summary>Arms the insert: the mouse becomes a placement tool until a click or Esc.</summary>
    private void ArmInsert(string tool, Func<Element> factory)
    {
        _pendingFactory = factory;
        IsPlacing = true;
        ArmedTool = tool;
        StatusText = "Click the canvas to place the new element (Esc cancels)";
    }

    /// <summary>Called by the canvas with the clicked position in dots.</summary>
    public void PlaceAt(int x, int y)
    {
        if (_pendingFactory is null)
        {
            IsPlacing = false;
            return;
        }

        Element element = _pendingFactory();
        element.X = Math.Clamp(x, 0, Math.Max(Document.WidthDots - 1, 0));
        element.Y = Math.Clamp(y, 0, Math.Max(Document.HeightDots - 1, 0));
        element.ZOrder = Document.Elements.Count == 0
            ? 0
            : Document.Elements.Max(e => e.ZOrder) + 1;
        Document.Elements.Add(element);
        Selection.Set(element);

        _pendingFactory = null;
        IsPlacing = false;
        ArmedTool = null;
        StatusText = string.Empty;
        RecordUndo();
        ScheduleRender();
    }

    public void CancelInsert()
    {
        _pendingFactory = null;
        IsPlacing = false;
        ArmedTool = null;
        StatusText = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AlignLeft() => ApplyAlign(AlignEdge.Left);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AlignCenterHorizontal() => ApplyAlign(AlignEdge.CenterHorizontal);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AlignRight() => ApplyAlign(AlignEdge.Right);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AlignTop() => ApplyAlign(AlignEdge.Top);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AlignMiddle() => ApplyAlign(AlignEdge.Middle);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AlignBottom() => ApplyAlign(AlignEdge.Bottom);

    /// <summary>Distribution needs at least three elements; with fewer there is no
    /// middle gap to equalize.</summary>
    public bool CanDistribute => SelectionCount >= 3;

    [RelayCommand(CanExecute = nameof(CanDistribute))]
    private void DistributeHorizontal() => ApplyDistribute(horizontal: true);

    [RelayCommand(CanExecute = nameof(CanDistribute))]
    private void DistributeVertical() => ApplyDistribute(horizontal: false);

    private void ApplyAlign(AlignEdge edge)
    {
        if (Aligner.Align(Selection.Items.ToList(), edge, Document.WidthDots, Document.HeightDots))
        {
            SelectionProperties?.Refresh();
            RecordUndo();
            ScheduleRender();
        }
    }

    private void ApplyDistribute(bool horizontal)
    {
        if (Aligner.Distribute(Selection.Items.ToList(), horizontal))
        {
            SelectionProperties?.Refresh();
            RecordUndo();
            ScheduleRender();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy()
    {
        if (Selection.Count > 0)
        {
            // Preserve draw order so a pasted group stacks like the original.
            _clipboardElement = LabelDocumentJson.SerializeElements(
                Selection.Items.OrderBy(e => e.ZOrder));
            CanPaste = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste()
    {
        if (_clipboardElement is null)
        {
            return;
        }

        List<Element> elements = LabelDocumentJson.DeserializeElements(_clipboardElement);
        if (PlaceClones(elements))
        {
            // Re-serialize the placed (offset, clamped) copies so repeated pastes
            // cascade from where the last one landed instead of stacking.
            _clipboardElement = LabelDocumentJson.SerializeElements(elements);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        if (Selection.Count == 0)
        {
            return;
        }

        // Duplicate is independent of the clipboard: clone the selection directly so it
        // never overwrites what the user has copied.
        List<Element> clones = LabelDocumentJson.DeserializeElements(
            LabelDocumentJson.SerializeElements(Selection.Items.OrderBy(e => e.ZOrder)));
        PlaceClones(clones);
    }

    /// <summary>Adds cloned elements to the document with fresh ids, a +20 dot cascade
    /// offset, and z-order on top; selects them and records one undo step. One delta is
    /// applied to the whole group so relative offsets are preserved, and when the group
    /// reaches a label edge the cascade wraps back near the top-left instead of
    /// clamping, so repeated pastes never pile up on a single spot. Returns false when
    /// there was nothing to add.</summary>
    private bool PlaceClones(List<Element> clones)
    {
        if (clones.Count == 0)
        {
            return false;
        }

        int nextZ = Document.Elements.Count == 0
            ? 0
            : Document.Elements.Max(e => e.ZOrder) + 1;
        int dx = CascadeDelta(clones.Min(e => e.X), clones.Max(e => e.X), Math.Max(Document.WidthDots - 1, 0));
        int dy = CascadeDelta(clones.Min(e => e.Y), clones.Max(e => e.Y), Math.Max(Document.HeightDots - 1, 0));

        foreach (Element element in clones)
        {
            element.Id = Guid.NewGuid();
            element.X += dx;
            element.Y += dy;
            element.ZOrder = nextZ++;
            Document.Elements.Add(element);
        }

        Selection.SetMany(clones);
        RecordUndo();
        ScheduleRender();
        return true;
    }

    /// <summary>The cascade offset along one axis for a group whose origins span
    /// [min, max]: +20 while that fits within the label, otherwise wrap the group back
    /// so its first origin restarts at 20. A group whose origins span the whole axis
    /// stays put; it cannot cascade without leaving the label.</summary>
    private static int CascadeDelta(int min, int max, int limit)
    {
        const int step = 20;
        if (max + step <= limit)
        {
            return step;
        }

        int wrap = step - min;
        return max + wrap <= limit ? wrap : -min;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void BringToFront()
    {
        if (Selection.Count == 0)
        {
            return;
        }

        int nextZ = Document.Elements.Max(e => e.ZOrder) + 1;
        foreach (Element element in Selection.Items.OrderBy(e => e.ZOrder))
        {
            element.ZOrder = nextZ++;
        }

        RecordUndo();
        ScheduleRender();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SendToBack()
    {
        if (Selection.Count == 0)
        {
            return;
        }

        int nextZ = Document.Elements.Min(e => e.ZOrder) - Selection.Count;
        foreach (Element element in Selection.Items.OrderBy(e => e.ZOrder))
        {
            element.ZOrder = nextZ++;
        }

        RecordUndo();
        ScheduleRender();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Selection.Count == 0)
        {
            return;
        }

        foreach (Element element in Selection.Items.ToList())
        {
            Document.Elements.Remove(element);
        }

        Selection.Clear();
        RecordUndo();
        ScheduleRender();
    }

    private ElementPropertiesViewModel? CreatePropertiesEditor(Element? value) => value switch
    {
        TextElement text => new TextPropertiesViewModel(text, Document, OnPanelEdited),
        BarcodeElement barcode => new BarcodePropertiesViewModel(barcode, Document, OnPanelEdited),
        QrCodeElement qr => new QrPropertiesViewModel(qr, Document, OnPanelEdited),
        DataMatrixElement dm => new DataMatrixPropertiesViewModel(dm, Document, OnPanelEdited),
        ImageElement image => new ImagePropertiesViewModel(image, Document, OnPanelEdited),
        LineElement line => new LinePropertiesViewModel(line, Document, OnPanelEdited),
        BoxElement box => new BoxPropertiesViewModel(box, Document, OnPanelEdited),
        _ => null,
    };

    private void OnPanelEdited(string property)
    {
        // Key on the edited element and property so typing in one field coalesces, but
        // moving to another field or another element starts a fresh undo step.
        RecordUndo($"panel:{Selection.Primary?.Id}:{property}");
        ScheduleRender();
    }

    partial void OnSelectedPrinterChanged(Core.Printers.PrinterProfile? value)
    {
        if (!_restoring && value is { IsAny: false })
        {
            // Adopting a printer pushes its density onto the label.
            SelectedDensity = Densities.FirstOrDefault(d => d.Dpmm == value.Dpmm) ?? SelectedDensity;
        }

        UpdatePrinterWarning();
    }

    private void UpdatePrinterWarning() =>
        PrinterWarning = SelectedPrinter is { IsAny: false } printer
            ? string.Join("; ", printer.Validate(Document))
            : string.Empty;

    partial void OnWidthMmChanged(decimal value)
    {
        if (_restoring)
        {
            return;
        }

        SelectedMedia = null;
        Document.WidthMm = (double)value;
        UpdatePrinterWarning();
        RecordUndo("doc-width");
        ScheduleRender();
    }

    partial void OnHeightMmChanged(decimal value)
    {
        if (_restoring)
        {
            return;
        }

        SelectedMedia = null;
        Document.HeightMm = (double)value;
        RecordUndo("doc-height");
        ScheduleRender();
    }

    /// <summary>Applies a catalog media to the label: both dimensions in one undo
    /// step. The width/height setters are bypassed via the restore guard so the
    /// apply does not clear the selection or record two extra steps.</summary>
    partial void OnSelectedMediaChanged(Core.Media.StockMedia? value)
    {
        if (_restoring || value is null)
        {
            return;
        }

        _restoring = true;
        WidthMm = (decimal)value.WidthMm;
        HeightMm = (decimal)value.HeightMm;
        _restoring = false;

        Document.WidthMm = value.WidthMm;
        Document.HeightMm = value.HeightMm;
        UpdatePrinterWarning();
        RecordUndo();
        ScheduleRender();
        string kind = value.IsUserDefined ? "my media" : "media";
        StatusText = value.Continuous
            ? $"Applied {kind} {value.PartNumber}: continuous {value.WidthMm:0.#} mm roll, adjust the height to your content"
            : $"Applied {kind} {value.PartNumber} ({value.SizeText})";
    }

    /// <summary>Saves the label's current size as one of the user's own media. Presets
    /// are per machine, not part of the document, so this records no undo step.</summary>
    [RelayCommand]
    private void SaveUserMedia()
    {
        string name = NewMediaName.Trim();
        if (name.Length == 0)
        {
            StatusText = "Name the media before saving it";
            return;
        }

        var media = Core.Media.StockMedia.UserDefined(
            name,
            (double)WidthMm,
            (double)HeightMm,
            NewMediaMaterial,
            (double)NewMediaRadiusMm,
            NewMediaContinuous);

        Core.Media.UserMediaResult result = _userMediaStore.Add(media);
        ApplyUserMedia(result.Entries);
        NewMediaName = string.Empty;
        NewMediaMaterial = string.Empty;

        // A failed write still leaves a usable preset in this session; say which it is
        // rather than reporting a success the next start would contradict.
        StatusText = result.Error is null
            ? $"Saved my media {name} ({media.SizeText})"
            : $"Saved for this session only, the presets file could not be written: {result.Error}";
    }

    private void RemoveUserMedia(Core.Media.StockMedia media)
    {
        Core.Media.UserMediaResult result = _userMediaStore.Remove(media.PartNumber);
        if (SelectedMedia == media)
        {
            SelectedMedia = null;
        }

        ApplyUserMedia(result.Entries);
        StatusText = result.Error is null
            ? $"Removed my media {media.PartNumber}"
            : $"Could not update the presets file: {result.Error}";
    }

    private void ApplyUserMedia(IReadOnlyList<Core.Media.StockMedia> presets)
    {
        UserMedia.Clear();
        foreach (Core.Media.StockMedia media in presets)
        {
            UserMedia.Add(new UserMediaEntryViewModel(media, RemoveUserMedia));
        }

        // The user's own first: they are the few entries that were deliberately
        // defined, and the catalog behind them is 797 entries deep.
        MediaCatalog = [.. presets, .. Core.Media.StockCatalog.All];
        OnPropertyChanged(nameof(HasUserMedia));
    }

    partial void OnSelectedDensityChanged(DensityOption? value)
    {
        if (_restoring || value is null)
        {
            return;
        }

        Document.Dpmm = value.Dpmm;
        UpdatePrinterWarning();
        RecordUndo("density");
        ScheduleRender();
    }

    /// <summary>
    /// Serializes the document into the history. A non-null <paramref name="coalesceKey"/>
    /// identifies the logical action being edited (a selection being dragged, one
    /// property being typed, the label width being adjusted). Consecutive edits that
    /// share the same key within the coalesce window replace the current snapshot
    /// instead of pushing a new one, so typing a word or holding an arrow key undoes as
    /// a single step; an edit to a different target starts a fresh step. A null key
    /// never coalesces (discrete actions like add, paste, delete, z-order).
    /// </summary>
    private void RecordUndo(string? coalesceKey = null)
    {
        string snapshot = LabelDocumentJson.Serialize(Document);
        long now = Environment.TickCount64;

        bool coalesce = coalesceKey is not null
            && coalesceKey == _lastCoalesceKey
            && now - _lastRecordTicks < CoalesceWindowMs;

        if (coalesce)
        {
            _history.ReplaceCurrent(snapshot);
        }
        else
        {
            _history.Record(snapshot);
        }

        _lastRecordTicks = now;
        _lastCoalesceKey = coalesceKey;
        UpdateUndoState();
    }

    private void RestoreSnapshot(string snapshot)
    {
        var selectedIds = Selection.Items.Select(e => e.Id).ToHashSet();

        _restoring = true;
        try
        {
            LabelDocument document = LabelDocumentJson.Deserialize(snapshot);
            Document = document;
            WidthMm = (decimal)document.WidthMm;
            HeightMm = (decimal)document.HeightMm;
            SelectedDensity = Densities.FirstOrDefault(d => d.Dpmm == document.Dpmm) ?? Densities[0];
            Selection.SetMany(document.Elements.Where(e => selectedIds.Contains(e.Id)));
        }
        finally
        {
            _restoring = false;
        }

        // Restoring must not count as a new edit; a subsequent edit starts fresh.
        _lastRecordTicks = 0;
        _lastCoalesceKey = null;
        NotifyPrintSettingsChanged();
        RefreshVariables();
        UpdatePrinterWarning();
        ScheduleRender();
    }

    private void UpdateUndoState()
    {
        CanUndo = _history.CanUndo;
        CanRedo = _history.CanRedo;
    }

    /// <summary>Syncs the Variables panel with the markers on the label. Skips the
    /// rebuild when nothing changed so an open sample TextBox keeps its focus.</summary>
    private void RefreshVariables()
    {
        IReadOnlyList<string> names = TemplateVariables.Discover(Document);
        bool unchanged = ReferenceEquals(_variablesDocument, Document)
            && names.Count == Variables.Count
            && !names.Where((name, i) => Variables[i].Name != name).Any();
        if (unchanged)
        {
            return;
        }

        _variablesDocument = Document;
        Variables.Clear();
        foreach (string name in names)
        {
            Variables.Add(new VariableSampleViewModel(name, Document, OnVariableEdited));
        }

        OnPropertyChanged(nameof(HasVariables));
    }

    /// <summary>An edit from the Variables panel. The row supplies the coalescing key so
    /// typing a sample and nudging a counter never merge into one undo step.</summary>
    private void OnVariableEdited(string undoKey)
    {
        RecordUndo(undoKey);
        ScheduleRender();
    }

    /// <summary>Moves (or adds) a path to the top of the recent files list.</summary>
    public void RegisterRecentFile(string path)
    {
        SyncRecentFiles(Services.RecentFilesStore.Add(path));
    }

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            LoadDocument(LabelDocumentJson.Deserialize(File.ReadAllText(path)), path);
            StatusText = $"Opened {Path.GetFileName(path)}";
            RegisterRecentFile(path);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open: {ex.Message}";
            SyncRecentFiles(Services.RecentFilesStore.Remove(path));
        }
    }

    private void SyncRecentFiles(IReadOnlyList<string> entries)
    {
        RecentFiles.Clear();
        foreach (string entry in entries)
        {
            RecentFiles.Add(entry);
        }

        OnPropertyChanged(nameof(HasRecentFiles));
    }

    /// <summary>Every visible barcode whose data cannot be encoded, each described for
    /// display ("Barcode 'Name': reason"). Empty when all barcodes are fine.</summary>
    private List<string> CollectBarcodeProblems()
    {
        var problems = new List<string>();
        foreach (BarcodeElement barcode in Document.Elements.OfType<BarcodeElement>().Where(b => b.IsVisible))
        {
            if (Core.Zpl.BarcodeValidator.Validate(barcode.Symbology, barcode.Data) is { } warning)
            {
                string name = string.IsNullOrEmpty(barcode.Name) ? barcode.Symbology.ToString() : barcode.Name;
                problems.Add($"Barcode '{name}': {warning}");
            }
        }

        return problems;
    }

    /// <summary>Refreshes the document-wide barcode validation summary shown near the
    /// canvas, so an un-encodable barcode is visible even when it is not selected.</summary>
    private void UpdateValidationWarning(IReadOnlyList<string> problems) =>
        ValidationWarning = problems.Count switch
        {
            0 => string.Empty,
            1 => problems[0],
            _ => $"{problems.Count} barcodes need attention: {string.Join("; ", problems)}",
        };

    private async void ScheduleRender(int delayMs = 150)
    {
        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;

        try
        {
            await Task.Delay(delayMs, cts.Token);

            LabelDocument document = Document;
            double widthMm = document.WidthMm;
            double heightMm = document.HeightMm;
            int dpmm = document.Dpmm;

            // One timestamp for the whole pass so the ZPL pane and the canvas agree on
            // what a date variable currently reads.
            DateTime now = DateTime.Now;

            (string zpl, RenderResult result, int marginDots, string placementWarning,
                    string variableWarning) =
                await Task.Run(
                    () =>
                    {
                        // A generator instance carries the state of one pass, and a
                        // superseded render can still be in flight, so never share one.
                        var generator = new ZplGenerator();
                        string generated = generator.Generate(
                            document, new GenerationContext { Now = now });
                        GenerationInfo run = generator.LastRun;

                        var bounds = new ElementBoundsCalculator();
                        var offLabel = document.Elements
                            .Where(e => e.IsVisible)
                            .Select(e => (Element: e, Status: ElementPlacement.Classify(
                                e, bounds.GetBounds(e), document.WidthDots, document.HeightDots)))
                            .Where(t => t.Status != PlacementStatus.Inside)
                            .ToList();

                        // Only pay for the expanded pasteboard render when something
                        // actually sits off the label.
                        int margin = offLabel.Count > 0
                            ? Units.MmToDots(ElementPlacement.PasteboardMarginMm, dpmm)
                            : 0;
                        double marginMm = Units.DotsToMm(margin, dpmm);

                        // Always the preview variant, even at margin 0: it keeps job
                        // settings and printer-side clock codes out of the render, which
                        // the offline engine would report as unknown commands or draw
                        // literally instead of as a date.
                        string previewZpl = new ZplGenerator().GeneratePreview(document, margin);

                        // The preview resolves every marker to something renderable: the
                        // user's sample, the counter's first value, or the current date.
                        // Exported and printed ZPL keeps external markers literal.
                        previewZpl = _substitutor.Substitute(
                            previewZpl, inner => VariableValues.ForPreview(document, inner, now));
                        RenderResult rendered = _renderer.Render(
                            previewZpl, widthMm + 2 * marginMm, heightMm + 2 * marginMm, dpmm);
                        return (generated, rendered, margin, DescribePlacement(offLabel),
                            string.Join(" ", run.Warnings));
                    },
                    cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            GeneratedZpl = zpl;
            UnderlayMarginDots = marginDots;
            PlacementWarning = placementWarning;
            VariableWarning = variableWarning;
            RefreshVariables();

            Bitmap? previous = Underlay;
            if (result.Png.Length > 0)
            {
                using var stream = new MemoryStream(result.Png);
                Underlay = new Bitmap(stream);
            }
            else
            {
                Underlay = null;
            }

            previous?.Dispose();

            // Document-wide validation summary, shown near the canvas regardless of
            // what is selected.
            List<string> barcodeProblems = CollectBarcodeProblems();
            UpdateValidationWarning(barcodeProblems);

            // On a failed or empty render, lead with a specific diagnosis when a
            // barcode cannot be encoded, but keep the engine's own message too: the
            // failure may have a different cause than the barcode we flagged.
            var diagnosis = new List<string>(2);
            if ((result.Errors.Count > 0 || result.Png.Length == 0) && barcodeProblems.Count > 0)
            {
                diagnosis.Add(barcodeProblems[0]);
            }

            if (result.Errors.Count > 0)
            {
                diagnosis.Add(string.Join("; ", result.Errors.Take(2)));
            }

            // The rendered bitmap may be pasteboard-expanded; always report the label's
            // own size.
            StatusText = diagnosis.Count > 0
                ? string.Join(" | ", diagnosis)
                : $"{document.WidthDots} x {document.HeightDots} dots";
        }
        catch (OperationCanceledException)
        {
            // A newer edit superseded this render; drop it.
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    /// <summary>One line summarizing elements off the label ("will not print") and
    /// elements crossing its right/bottom edge ("will be clipped"); empty when none.</summary>
    private static string DescribePlacement(
        IReadOnlyList<(Element Element, PlacementStatus Status)> offLabel)
    {
        var outside = offLabel.Where(t => t.Status == PlacementStatus.NotPrintable).ToList();
        var clipped = offLabel.Where(t => t.Status == PlacementStatus.Clipped).ToList();

        var parts = new List<string>(2);
        if (outside.Count == 1)
        {
            parts.Add($"'{DisplayName(outside[0].Element)}' is outside the label and will not print");
        }
        else if (outside.Count > 1)
        {
            parts.Add($"{outside.Count} elements are outside the label and will not print");
        }

        if (clipped.Count == 1)
        {
            parts.Add($"'{DisplayName(clipped[0].Element)}' extends past the label edge and will be clipped");
        }
        else if (clipped.Count > 1)
        {
            parts.Add($"{clipped.Count} elements extend past the label edge and will be clipped");
        }

        return string.Join("; ", parts);
    }

    private static string DisplayName(Element element) =>
        !string.IsNullOrEmpty(element.Name) ? element.Name : element switch
        {
            TextElement => "Text",
            BarcodeElement => "Barcode",
            QrCodeElement => "QR code",
            DataMatrixElement => "Data Matrix",
            ImageElement => "Image",
            LineElement => "Line",
            BoxElement => "Box",
            _ => "Element",
        };

    private static void SeedSampleLabel(LabelDocument doc)
    {
        doc.Elements.Add(new BoxElement
        {
            Name = "Border", X = 15, Y = 15, WidthDots = 770, HeightDots = 450, ThicknessDots = 3, ZOrder = 0,
        });
        doc.Elements.Add(new TextElement
        {
            Name = "Title", X = 50, Y = 50, Text = "LabelForge", FontHeightDots = 60, ZOrder = 1,
        });
        doc.Elements.Add(new BarcodeElement
        {
            Name = "Barcode", X = 50, Y = 170, Data = "LF-000123", HeightDots = 140, ModuleWidthDots = 3, ZOrder = 2,
        });
        doc.Elements.Add(new QrCodeElement
        {
            Name = "QR", X = 600, Y = 170, Data = "https://labelforge.app", Magnification = 6, ZOrder = 3,
        });
    }
}
