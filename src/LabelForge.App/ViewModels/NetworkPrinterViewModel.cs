using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelForge.Core.Printing;

namespace LabelForge.App.ViewModels;

public partial class NetworkPrinterViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Host { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal Port { get; set; } = RawNetworkPrinter.DefaultPort;

    [ObservableProperty]
    public partial bool CheckBeforeSending { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckStatusCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    private bool CanCheckStatus() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCheckStatus))]
    private async Task CheckStatusAsync()
    {
        if (IsBusy || !HasAddress())
        {
            return;
        }
        string host = Host.Trim();
        int port = (int)Port;
        string endpoint = $"{host}:{port}";
        IsBusy = true;
        StatusText = $"Checking {endpoint}...";
        try
        {
            PrinterStatus status = await RawNetworkPrinter.QueryStatusAsync(host, port);
            StatusText = $"{endpoint}: {DescribeStatus(status)}.";
        }
        catch (Exception ex)
        {
            StatusText = $"{endpoint}: status unknown. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SendAsync(string zpl, string jobSummary = "")
    {
        if (IsBusy || !HasAddress())
        {
            return;
        }
        string host = Host.Trim();
        int port = (int)Port;
        string endpoint = $"{host}:{port}";
        bool checkStatus = CheckBeforeSending;
        IsBusy = true;
        StatusText = checkStatus ? $"Checking and sending to {endpoint}..." : $"Sending to {endpoint}...";
        try
        {
            if (!checkStatus)
            {
                await RawNetworkPrinter.SendAsync(host, port, zpl);
                StatusText = $"Sent to {endpoint}{jobSummary}. Status was not checked; printing completion is not confirmed.";
                return;
            }

            NetworkPrintResult result = await RawNetworkPrinter.SendWithStatusAsync(host, port, zpl);
            StatusText = result.Delivery switch
            {
                PrintDelivery.NotSent => result.Before is { } before
                    ? $"Not sent to {endpoint}. Printer: {DescribeStatus(before)}."
                    : $"Not sent to {endpoint}. Status unknown. {result.Error}",
                PrintDelivery.Uncertain => $"Delivery to {endpoint} is uncertain. Check the printer before resending. {result.Error}",
                _ => result.After is { } after
                    ? $"Sent to {endpoint}{jobSummary}. Printer after send: {DescribeStatus(after)}. Printing completion is not confirmed."
                    : $"Sent to {endpoint}{jobSummary}. Status after send is unknown; check the printer before resending. {result.Error}"
            };
        }
        catch (Exception ex)
        {
            StatusText = $"Send to {endpoint} failed; delivery is not confirmed. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool HasAddress()
    {
        if (!string.IsNullOrWhiteSpace(Host))
        {
            return true;
        }
        StatusText = "Enter the printer address first";
        return false;
    }

    private static string DescribeStatus(PrinterStatus status)
    {
        var parts = new List<string>();
        foreach ((PrinterFaults flag, string text) in FaultNames)
        {
            if ((status.Faults & flag) != 0)
            {
                parts.Add(text);
            }
        }
        if (status.IsReady)
        {
            parts.Add("ready");
        }
        if (status.LabelWaiting)
        {
            parts.Add("label waiting to be removed");
        }
        if (status.FormatsQueued > 0 || status.LabelsRemaining > 0)
        {
            parts.Add($"{status.FormatsQueued} formats queued, {status.LabelsRemaining} labels remaining");
        }
        return string.Join(", ", parts);
    }

    private static readonly (PrinterFaults, string)[] FaultNames =
    [
        (PrinterFaults.PaperOut, "paper out"),
        (PrinterFaults.Paused, "paused"),
        (PrinterFaults.HeadOpen, "head open"),
        (PrinterFaults.RibbonOut, "ribbon out"),
        (PrinterFaults.BufferFull, "receive buffer full"),
        (PrinterFaults.DiagnosticMode, "communications diagnostic mode"),
        (PrinterFaults.PartialFormat, "incomplete label format in buffer"),
        (PrinterFaults.CorruptMemory, "corrupt printer memory"),
        (PrinterFaults.UnderTemperature, "head temperature too low"),
        (PrinterFaults.OverTemperature, "head temperature too high")
    ];
}
