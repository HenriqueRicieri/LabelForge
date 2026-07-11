using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelForge.Core.Printing;
using LabelForge.Core.Rendering;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;

namespace LabelForge.App.ViewModels;

/// <summary>
/// The ZPL viewer: an editable ZPL pane on the left drives a live offline preview on
/// the right, with a diagnostics line for unsupported commands and errors. Rendering
/// runs off the UI thread, debounced, latest-wins, so typing stays responsive.
/// </summary>
public partial class ViewerViewModel : ViewModelBase
{
    private readonly IZplRenderer _renderer = new BinaryKitsRenderer();
    private readonly TemplateSubstitutor _substitutor = new();
    private CancellationTokenSource? _renderCts;
    private bool _suppressSizeRender;
    private bool _suppressLabelRender;

    public System.Collections.Generic.IReadOnlyList<DensityOption> Densities => DensityOption.Standard;

    /// <summary>Label numbers ("Label 1".."Label N") shown when a file has several blocks.</summary>
    public ObservableCollection<string> LabelOptions { get; } = ["Label 1"];

    [ObservableProperty]
    public partial string ZplText { get; set; } = SampleZpl;

    [ObservableProperty]
    public partial bool AutoSize { get; set; } = true;

    [ObservableProperty]
    public partial decimal WidthMm { get; set; } = 100m;

    [ObservableProperty]
    public partial decimal HeightMm { get; set; } = 150m;

    [ObservableProperty]
    public partial DensityOption? SelectedDensity { get; set; }

    [ObservableProperty]
    public partial int SelectedLabelIndex { get; set; }

    [ObservableProperty]
    public partial int LabelCount { get; set; } = 1;

    [ObservableProperty]
    public partial bool HasMultipleLabels { get; set; }

    [ObservableProperty]
    public partial Bitmap? PreviewImage { get; set; }

    [ObservableProperty]
    public partial bool HasErrors { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "No errors";

    [ObservableProperty]
    public partial string InfoText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PrinterHost { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal PrinterPort { get; set; } = RawNetworkPrinter.DefaultPort;

    /// <summary>Sends what the preview shows: the current ZPL with sample data substituted.</summary>
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await RawNetworkPrinter.SendAsync(
                host, (int)PrinterPort, _substitutor.Substitute(ZplText ?? string.Empty), cts.Token);
            StatusText = $"Sent to {host}:{(int)PrinterPort}";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Print timed out: could not reach {host}:{(int)PrinterPort}";
        }
        catch (Exception ex)
        {
            StatusText = $"Print failed: {ex.Message}";
        }
    }

    public ViewerViewModel()
    {
        SelectedDensity = Densities[0];
        ScheduleRender();
    }

    /// <summary>Replaces the editor contents (used when opening a file).</summary>
    public void LoadZpl(string zpl)
    {
        SelectedLabelIndex = 0;
        ZplText = zpl;
    }

    partial void OnZplTextChanged(string value) => ScheduleRender();

    partial void OnAutoSizeChanged(bool value) => ScheduleRender();

    partial void OnWidthMmChanged(decimal value)
    {
        if (!_suppressSizeRender)
        {
            ScheduleRender();
        }
    }

    partial void OnHeightMmChanged(decimal value)
    {
        if (!_suppressSizeRender)
        {
            ScheduleRender();
        }
    }

    partial void OnSelectedDensityChanged(DensityOption? value) => ScheduleRender();

    partial void OnSelectedLabelIndexChanged(int value)
    {
        if (!_suppressLabelRender)
        {
            ScheduleRender();
        }
    }

    private async void ScheduleRender()
    {
        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;

        try
        {
            await Task.Delay(200, cts.Token);

            string zpl = ZplText ?? string.Empty;
            int dpmm = SelectedDensity?.Dpmm ?? 8;
            ApplyAutoSize(zpl, dpmm);

            double widthMm = (double)WidthMm;
            double heightMm = (double)HeightMm;
            int labelIndex = SelectedLabelIndex;

            RenderResult result = await Task.Run(
                () => _renderer.Render(_substitutor.Substitute(zpl), widthMm, heightMm, dpmm, labelIndex),
                cts.Token);

            if (!cts.IsCancellationRequested)
            {
                Apply(result);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke superseded this render; drop it.
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    /// <summary>When auto-size is on, drives the width/height fields from ^PW/^LL in the ZPL.</summary>
    private void ApplyAutoSize(string zpl, int dpmm)
    {
        if (!AutoSize || dpmm <= 0)
        {
            return;
        }

        int? printWidth = ZplSizeScanner.PrintWidthDots(zpl);
        int? labelLength = ZplSizeScanner.LabelLengthDots(zpl);

        _suppressSizeRender = true;
        if (printWidth is int pw && pw > 0)
        {
            WidthMm = Math.Round((decimal)pw / dpmm, 1);
        }

        if (labelLength is int ll && ll > 0)
        {
            HeightMm = Math.Round((decimal)ll / dpmm, 1);
        }

        _suppressSizeRender = false;
    }

    private void Apply(RenderResult result)
    {
        Bitmap? previous = PreviewImage;
        if (result.Png.Length > 0)
        {
            using var stream = new MemoryStream(result.Png);
            PreviewImage = new Bitmap(stream);
            StatusText = $"{result.WidthDots} x {result.HeightDots} dots";
        }
        else
        {
            PreviewImage = null;
            StatusText = "no image";
        }

        previous?.Dispose();

        UpdateLabelOptions(result.LabelCount);

        HasErrors = result.Errors.Count > 0;
        ErrorText = HasErrors
            ? $"{result.Errors.Count} error(s): {string.Join("; ", result.Errors.Take(3))}"
            : "No errors";

        InfoText = result.UnknownCommands.Count > 0
            ? $"{result.UnknownCommands.Count} unsupported: {string.Join(", ", result.UnknownCommands.Distinct().Take(12))}"
            : "All commands supported";
    }

    private void UpdateLabelOptions(int count)
    {
        count = Math.Max(count, 1);

        // Adjust the list in place. Clearing it would momentarily empty the bound
        // ComboBox, which pushes SelectedIndex = -1 back through the binding and blanks
        // the selection. Suppress render while the list churns, then clamp the index.
        _suppressLabelRender = true;

        while (LabelOptions.Count > count)
        {
            LabelOptions.RemoveAt(LabelOptions.Count - 1);
        }

        while (LabelOptions.Count < count)
        {
            LabelOptions.Add($"Label {LabelOptions.Count + 1}");
        }

        LabelCount = count;
        HasMultipleLabels = count > 1;

        if (SelectedLabelIndex < 0 || SelectedLabelIndex >= count)
        {
            SelectedLabelIndex = 0;
        }

        _suppressLabelRender = false;
    }

    private const string SampleZpl =
        "^XA\n" +
        "^CI28\n" +
        "^PW800\n" +
        "^LL600\n" +
        "^FO50,45^A0N,55,55^FDLabelForge^FS\n" +
        "^FO50,120^A0N,30,30^FDLive ZPL viewer^FS\n" +
        "^FO50,175^GB700,3,3^FS\n" +
        "^BY3^FO50,220^BCN,130,Y,N,N^FDLF-000123^FS\n" +
        "^FO560,220^BQN,2,6^FDMA,https://labelforge.app^FS\n" +
        "^XZ\n";
}
