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
    private string? _clipboardElement;

    public IReadOnlyList<DensityOption> Densities => DensityOption.Standard;

    [ObservableProperty]
    public partial LabelDocument Document { get; set; }

    [ObservableProperty]
    public partial Bitmap? Underlay { get; set; }

    [ObservableProperty]
    public partial string GeneratedZpl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Element? SelectedElement { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand))]
    [NotifyCanExecuteChangedFor(nameof(BringToFrontCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendToBackCommand))]
    public partial bool HasSelection { get; set; }

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
        // Property setters record undo states; construction must not, or the
        // history would start with a spurious empty document before the seed.
        _restoring = true;
        Document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        SelectedDensity = Densities[0];
        SelectedPrinter = Core.Printers.PrinterProfile.Any;
        SeedSampleLabel();
        _restoring = false;

        RecordUndo(coalesce: false);
        ScheduleRender();
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
            SelectedElement = null;
        }
        finally
        {
            _restoring = false;
        }

        CurrentFilePath = path;
        UpdatePrinterWarning();
        _history.Clear();
        _lastRecordTicks = 0;
        RecordUndo(coalesce: false);
        ScheduleRender();
    }

    [RelayCommand]
    private void NewDocument() =>
        LoadDocument(new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 }, path: null);

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
            await Task.Run(() => Core.Printing.WindowsRawPrinter.Send(name, zpl));
            StatusText = $"Sent to {name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Print failed: {ex.Message}";
        }
    }

    /// <summary>Called by the view when the canvas edits the model (drag, resize, nudge).</summary>
    public void NotifyDocumentEdited()
    {
        SelectionProperties?.Refresh();
        RecordUndo(coalesce: true);
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

    [RelayCommand]
    private void AddText() => AddElement(
        new TextElement { Text = "New text", FontHeightDots = 40 });

    [RelayCommand]
    private void AddBox() => AddElement(
        new BoxElement { WidthDots = 240, HeightDots = 140, ThicknessDots = 3 });

    [RelayCommand]
    private void AddLine() => AddElement(
        new LineElement { LengthDots = 240, ThicknessDots = 3 });

    [RelayCommand]
    private void AddBarcode() => AddElement(
        new BarcodeElement { Data = "123456", HeightDots = 100, ModuleWidthDots = 2 });

    [RelayCommand]
    private void AddQr() => AddElement(
        new QrCodeElement { Data = "https://example.com", Magnification = 5 });

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy()
    {
        if (SelectedElement is { } selected)
        {
            _clipboardElement = LabelDocumentJson.SerializeElement(selected);
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

        Element element = LabelDocumentJson.DeserializeElement(_clipboardElement);
        element.Id = Guid.NewGuid();
        element.X += 20;
        element.Y += 20;
        element.ZOrder = Document.Elements.Count == 0
            ? 0
            : Document.Elements.Max(e => e.ZOrder) + 1;
        Document.Elements.Add(element);
        SelectedElement = element;

        // Re-serialize so repeated pastes cascade instead of stacking.
        _clipboardElement = LabelDocumentJson.SerializeElement(element);

        RecordUndo(coalesce: false);
        ScheduleRender();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        Copy();
        Paste();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void BringToFront()
    {
        if (SelectedElement is { } selected)
        {
            selected.ZOrder = Document.Elements.Max(e => e.ZOrder) + 1;
            RecordUndo(coalesce: false);
            ScheduleRender();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SendToBack()
    {
        if (SelectedElement is { } selected)
        {
            selected.ZOrder = Document.Elements.Min(e => e.ZOrder) - 1;
            RecordUndo(coalesce: false);
            ScheduleRender();
        }
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedElement is { } selected)
        {
            Document.Elements.Remove(selected);
            SelectedElement = null;
            RecordUndo(coalesce: false);
            ScheduleRender();
        }
    }

    private void AddElement(Element element)
    {
        element.X = 40;
        element.Y = 40;
        element.ZOrder = Document.Elements.Count == 0
            ? 0
            : Document.Elements.Max(e => e.ZOrder) + 1;
        Document.Elements.Add(element);
        SelectedElement = element;
        RecordUndo(coalesce: false);
        ScheduleRender();
    }

    partial void OnSelectedElementChanged(Element? value)
    {
        HasSelection = value is not null;
        SelectionProperties = value switch
        {
            TextElement text => new TextPropertiesViewModel(text, OnPanelEdited),
            BarcodeElement barcode => new BarcodePropertiesViewModel(barcode, OnPanelEdited),
            QrCodeElement qr => new QrPropertiesViewModel(qr, OnPanelEdited),
            LineElement line => new LinePropertiesViewModel(line, OnPanelEdited),
            BoxElement box => new BoxPropertiesViewModel(box, OnPanelEdited),
            _ => null,
        };
    }

    private void OnPanelEdited()
    {
        RecordUndo(coalesce: true);
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
        RecordUndo(coalesce: true);
        ScheduleRender();
    }

    partial void OnHeightMmChanged(decimal value)
    {
        if (_restoring)
        {
            return;
        }

        Document.HeightMm = (double)value;
        RecordUndo(coalesce: true);
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
        RecordUndo(coalesce: true);
        ScheduleRender();
    }

    /// <summary>
    /// Serializes the document into the history. Edits arriving within the coalesce
    /// window replace the current snapshot instead of pushing a new one, so typing a
    /// word or holding an arrow key undoes as a single step.
    /// </summary>
    private void RecordUndo(bool coalesce)
    {
        string snapshot = LabelDocumentJson.Serialize(Document);
        long now = Environment.TickCount64;

        if (coalesce && now - _lastRecordTicks < CoalesceWindowMs)
        {
            _history.ReplaceCurrent(snapshot);
        }
        else
        {
            _history.Record(snapshot);
        }

        _lastRecordTicks = now;
        UpdateUndoState();
    }

    private void RestoreSnapshot(string snapshot)
    {
        Guid? selectedId = SelectedElement?.Id;

        _restoring = true;
        try
        {
            LabelDocument document = LabelDocumentJson.Deserialize(snapshot);
            Document = document;
            WidthMm = (decimal)document.WidthMm;
            HeightMm = (decimal)document.HeightMm;
            SelectedDensity = Densities.FirstOrDefault(d => d.Dpmm == document.Dpmm) ?? Densities[0];
            SelectedElement = document.Elements.FirstOrDefault(e => e.Id == selectedId);
        }
        finally
        {
            _restoring = false;
        }

        // Restoring must not count as a new edit; a subsequent edit starts fresh.
        _lastRecordTicks = 0;
        UpdatePrinterWarning();
        ScheduleRender();
    }

    private void UpdateUndoState()
    {
        CanUndo = _history.CanUndo;
        CanRedo = _history.CanRedo;
    }

    private async void ScheduleRender()
    {
        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;

        try
        {
            await Task.Delay(150, cts.Token);

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

            StatusText = result.Errors.Count > 0
                ? string.Join("; ", result.Errors.Take(2))
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

    private void SeedSampleLabel()
    {
        Document.Elements.Add(new BoxElement
        {
            Name = "Border", X = 15, Y = 15, WidthDots = 770, HeightDots = 450, ThicknessDots = 3, ZOrder = 0,
        });
        Document.Elements.Add(new TextElement
        {
            Name = "Title", X = 50, Y = 50, Text = "LabelForge", FontHeightDots = 60, ZOrder = 1,
        });
        Document.Elements.Add(new BarcodeElement
        {
            Name = "Barcode", X = 50, Y = 170, Data = "LF-000123", HeightDots = 140, ModuleWidthDots = 3, ZOrder = 2,
        });
        Document.Elements.Add(new QrCodeElement
        {
            Name = "QR", X = 600, Y = 170, Data = "https://labelforge.app", Magnification = 6, ZOrder = 3,
        });
    }
}
