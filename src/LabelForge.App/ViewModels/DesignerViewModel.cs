using System;
using System.Collections.Generic;
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
    private readonly ZplGenerator _generator = new();
    private readonly SnapshotHistory _history = new();
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
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
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
    public partial decimal WidthMm { get; set; } = 100m;

    [ObservableProperty]
    public partial decimal HeightMm { get; set; } = 60m;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool CanUndo { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    public partial bool CanRedo { get; set; }

    public DesignerViewModel()
    {
        Selection.Changed += (_, _) => OnSelectionChanged();

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
            .Render(_generator.Generate(document), document.WidthMm, document.HeightMm, document.Dpmm)
            .Png);
    }

    /// <summary>Renders the current document to a PDF page at its physical size.</summary>
    public Task<byte[]> RenderPdfAsync()
    {
        LabelDocument document = Document;
        return Task.Run(() =>
        {
            byte[] png = _renderer
                .Render(_generator.Generate(document), document.WidthMm, document.HeightMm, document.Dpmm)
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
            SelectedDensity = Densities.FirstOrDefault(d => d.Dpmm == document.Dpmm) ?? Densities[0];
            Selection.Clear();
        }
        finally
        {
            _restoring = false;
        }

        CurrentFilePath = path;
        UpdatePrinterWarning();
        _history.Clear();
        _lastRecordTicks = 0;
        _lastCoalesceKey = null;
        RecordUndo();
        ScheduleRender();
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
            string zpl = _generator.Generate(Document);

            // The connection phase is bounded inside SendAsync; a timeout surfaces as a
            // TimeoutException whose message already names the unreachable endpoint.
            await Core.Printing.RawNetworkPrinter.SendAsync(host, (int)PrinterPort, zpl);
            StatusText = $"Sent to {host}:{(int)PrinterPort}";
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
            string zpl = _generator.Generate(Document);
            await SendToWindowsPrinterAsync(name, zpl);
            StatusText = $"Sent to {name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Print failed: {ex.Message}";
        }
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

    private Func<Element>? _pendingFactory;

    [RelayCommand]
    private void AddText() => ArmInsert(
        () => new TextElement { Text = "New text", FontHeightDots = 40 });

    [RelayCommand]
    private void AddBox() => ArmInsert(
        () => new BoxElement { WidthDots = 240, HeightDots = 140, ThicknessDots = 3 });

    [RelayCommand]
    private void AddLine() => ArmInsert(
        () => new LineElement { LengthDots = 240, ThicknessDots = 3 });

    [RelayCommand]
    private void AddBarcode() => ArmInsert(
        () => new BarcodeElement { Data = "123456", HeightDots = 100, ModuleWidthDots = 2 });

    [RelayCommand]
    private void AddQr() => ArmInsert(
        () => new QrCodeElement { Data = "https://example.com", Magnification = 5 });

    /// <summary>Arms the insert: the mouse becomes a placement tool until a click or Esc.</summary>
    private void ArmInsert(Func<Element> factory)
    {
        _pendingFactory = factory;
        IsPlacing = true;
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
        StatusText = string.Empty;
        RecordUndo();
        ScheduleRender();
    }

    public void CancelInsert()
    {
        _pendingFactory = null;
        IsPlacing = false;
        StatusText = string.Empty;
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
        TextElement text => new TextPropertiesViewModel(text, OnPanelEdited),
        BarcodeElement barcode => new BarcodePropertiesViewModel(barcode, OnPanelEdited),
        QrCodeElement qr => new QrPropertiesViewModel(qr, OnPanelEdited),
        LineElement line => new LinePropertiesViewModel(line, OnPanelEdited),
        BoxElement box => new BoxPropertiesViewModel(box, OnPanelEdited),
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

        Document.HeightMm = (double)value;
        RecordUndo("doc-height");
        ScheduleRender();
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
        UpdatePrinterWarning();
        ScheduleRender();
    }

    private void UpdateUndoState()
    {
        CanUndo = _history.CanUndo;
        CanRedo = _history.CanRedo;
    }

    /// <summary>The first visible barcode whose data cannot be encoded, described for the
    /// status line, or null when every barcode is fine. Explains a blank preview.</summary>
    private string? FindBarcodeProblem()
    {
        foreach (BarcodeElement barcode in Document.Elements.OfType<BarcodeElement>().Where(b => b.IsVisible))
        {
            if (Core.Zpl.BarcodeValidator.Validate(barcode.Symbology, barcode.Data) is { } warning)
            {
                string name = string.IsNullOrEmpty(barcode.Name) ? barcode.Symbology.ToString() : barcode.Name;
                return $"Barcode '{name}': {warning}";
            }
        }

        return null;
    }

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

            (string zpl, RenderResult result) = await Task.Run(
                () =>
                {
                    string generated = _generator.Generate(document);
                    return (generated, _renderer.Render(generated, widthMm, heightMm, dpmm));
                },
                cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            GeneratedZpl = zpl;

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

            // On a failed or empty render, lead with a specific diagnosis when a
            // barcode cannot be encoded, but keep the engine's own message too: the
            // failure may have a different cause than the barcode we flagged.
            var diagnosis = new List<string>(2);
            if ((result.Errors.Count > 0 || result.Png.Length == 0) && FindBarcodeProblem() is { } problem)
            {
                diagnosis.Add(problem);
            }

            if (result.Errors.Count > 0)
            {
                diagnosis.Add(string.Join("; ", result.Errors.Take(2)));
            }

            StatusText = diagnosis.Count > 0
                ? string.Join(" | ", diagnosis)
                : $"{result.WidthDots} x {result.HeightDots} dots";
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
