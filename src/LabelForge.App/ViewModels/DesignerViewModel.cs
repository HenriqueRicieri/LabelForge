using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;

namespace LabelForge.App.ViewModels;

/// <summary>
/// The visual designer. The LabelDocument is the source of truth: the canvas and the
/// properties panel edit it, the ZPL generator turns it into code, and the offline
/// renderer turns that code into the canvas underlay (WYSIWYG rule). Rendering is
/// debounced, background, latest-wins, mirroring the viewer pipeline.
/// </summary>
public partial class DesignerViewModel : ViewModelBase
{
    private readonly IZplRenderer _renderer = new BinaryKitsRenderer();
    private readonly ZplGenerator _generator = new();
    private CancellationTokenSource? _renderCts;
    private bool _syncingSelection;

    public LabelDocument Document { get; } = new() { WidthMm = 100, HeightMm = 60, Dpmm = 8 };

    public IReadOnlyList<DensityOption> Densities => DensityOption.Standard;

    [ObservableProperty]
    public partial Bitmap? Underlay { get; set; }

    [ObservableProperty]
    public partial string GeneratedZpl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Element? SelectedElement { get; set; }

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial bool SelectionHasContent { get; set; }

    [ObservableProperty]
    public partial decimal SelectedX { get; set; }

    [ObservableProperty]
    public partial decimal SelectedY { get; set; }

    [ObservableProperty]
    public partial string SelectedContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal WidthMm { get; set; } = 100m;

    [ObservableProperty]
    public partial decimal HeightMm { get; set; } = 60m;

    [ObservableProperty]
    public partial DensityOption? SelectedDensity { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    public DesignerViewModel()
    {
        SelectedDensity = Densities[0];
        SeedSampleLabel();
        ScheduleRender();
    }

    /// <summary>Called by the view when the canvas edits the model (drag or nudge).</summary>
    public void NotifyDocumentEdited()
    {
        RefreshSelectionEditors();
        ScheduleRender();
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

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedElement is { } selected)
        {
            Document.Elements.Remove(selected);
            SelectedElement = null;
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
        ScheduleRender();
    }

    partial void OnSelectedElementChanged(Element? value)
    {
        HasSelection = value is not null;
        SelectionHasContent = value is TextElement or BarcodeElement or QrCodeElement;
        RefreshSelectionEditors();
    }

    partial void OnSelectedXChanged(decimal value)
    {
        if (!_syncingSelection && SelectedElement is { } selected)
        {
            selected.X = (int)value;
            ScheduleRender();
        }
    }

    partial void OnSelectedYChanged(decimal value)
    {
        if (!_syncingSelection && SelectedElement is { } selected)
        {
            selected.Y = (int)value;
            ScheduleRender();
        }
    }

    partial void OnSelectedContentChanged(string value)
    {
        if (_syncingSelection)
        {
            return;
        }

        switch (SelectedElement)
        {
            case TextElement text:
                text.Text = value;
                break;
            case BarcodeElement barcode:
                barcode.Data = value;
                break;
            case QrCodeElement qr:
                qr.Data = value;
                break;
            default:
                return;
        }

        ScheduleRender();
    }

    partial void OnWidthMmChanged(decimal value)
    {
        Document.WidthMm = (double)value;
        ScheduleRender();
    }

    partial void OnHeightMmChanged(decimal value)
    {
        Document.HeightMm = (double)value;
        ScheduleRender();
    }

    partial void OnSelectedDensityChanged(DensityOption? value)
    {
        if (value is not null)
        {
            Document.Dpmm = value.Dpmm;
            ScheduleRender();
        }
    }

    private void RefreshSelectionEditors()
    {
        _syncingSelection = true;
        SelectedX = SelectedElement?.X ?? 0;
        SelectedY = SelectedElement?.Y ?? 0;
        SelectedContent = SelectedElement switch
        {
            TextElement text => text.Text,
            BarcodeElement barcode => barcode.Data,
            QrCodeElement qr => qr.Data,
            _ => string.Empty,
        };
        _syncingSelection = false;
    }

    private async void ScheduleRender()
    {
        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;

        try
        {
            await Task.Delay(150, cts.Token);

            double widthMm = Document.WidthMm;
            double heightMm = Document.HeightMm;
            int dpmm = Document.Dpmm;

            (string zpl, RenderResult result) = await Task.Run(
                () =>
                {
                    string generated = _generator.Generate(Document);
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
