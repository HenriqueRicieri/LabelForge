using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Templating;

internal static class PrinterStatusChecks
{
    private const string Ready = "\x02" + "000,0,0,0800,000,0,0,0,000,0,0,0\x03\r\n"
        + "\x02" + "000,0,0,0,0,2,4,0,00000000,1,000\x03\r\n\x02" + "0000,0\x03\r\n";
    private static readonly string Paused = Ready.Replace("000,0,0,0800", "000,0,1,0800");
    private static readonly string HeadOpen = Ready.Replace("000,0,0,0,0,2", "000,0,1,0,0,2");

    public static void Run(MainWindow window, MainViewModel vm, Action<string, bool> check)
    {
        int originalTab = vm.SelectedTab;
        var size = (window.Width, window.Height);
        try
        {
            window.Width = 1024;
            window.Height = 640;
            Exercise("Designer", MainViewModel.DesignerTab, vm.Designer.NetworkPrinter,
                vm.Designer.PrintCommand, () => vm.Designer.BuildPrintJob().Zpl);
            Exercise("Viewer", MainViewModel.ViewerTab, vm.Viewer.NetworkPrinter,
                vm.Viewer.PrintCommand, () => new TemplateSubstitutor().Substitute(vm.Viewer.ZplText));
        }
        finally
        {
            vm.SelectedTab = originalTab;
            (window.Width, window.Height) = size;
            Pump();
        }

        void Exercise(string name, int tab, NetworkPrinterViewModel printer, IAsyncRelayCommand send, Func<string> expected)
        {
            var settings = (printer.Host, printer.Port, printer.CheckBeforeSending, printer.StatusText);
            vm.SelectedTab = tab;
            Pump();
            var button = window.GetVisualDescendants().OfType<Button>()
                .Single(b => b.IsEffectivelyVisible && Equals(b.Content, "Print..."));
            var flyout = (Flyout)button.Flyout!;
            flyout.ShowAt(button);
            Pump();
            var scroll = (ScrollViewer)flyout.Content!;
            var panel = (StackPanel)scroll.Content!;
            var controls = panel.GetVisualDescendants().ToArray();
            var hostInput = controls.OfType<TextBox>().Single(t => t.PlaceholderText == "192.168.0.50");
            var portInput = controls.OfType<NumericUpDown>().Single(c => c.Maximum == 65535);
            var checkButton = controls.OfType<Button>().Single(b => Equals(b.Content, "Check status"));
            var sendButton = controls.OfType<Button>().Single(b => b.Command == send);
            var statusToggle = controls.OfType<CheckBox>().Single(c => c.Content is TextBlock t && t.Text!.StartsWith("Check status"));
            try
            {
                check($"{name} checks printer status by default", statusToggle.IsChecked == true);
                check($"{name} print panel fits a 640-pixel window", scroll.Bounds.Height > 0 && scroll.Bounds.Height <= 540);
                printer.Host = "";
                Wait(send.ExecuteAsync(null));
                check($"{name} prompts for a printer address", printer.StatusText == "Enter the printer address first");

                string received = Exchange(Paused, Ready, () => send.ExecuteAsync(null));
                check($"{name} does not send a job to a paused printer", received.Length == 0);
                check($"{name} identifies the paused printer before sending", printer.StatusText.Contains("paused") && printer.StatusText.Contains("Not sent"));
                check($"{name} prints feedback in the panel", controls.OfType<TextBlock>().Any(t => t.Text == printer.StatusText && t.IsVisible));

                received = Exchange(Ready, Ready, () => printer.CheckStatusCommand.ExecuteAsync(null));
                check($"{name} can check status without sending a label", received.Length == 0 && printer.StatusText.Contains(": ready."));

                received = Exchange(Ready, HeadOpen, () => send.ExecuteAsync(null));
                check($"{name} sends the exact generated job between status checks", received == expected());
                check($"{name} reports a head opened after sending", printer.StatusText.Contains("Sent to") && printer.StatusText.Contains("head open"));
                check($"{name} does not equate TCP delivery with printing", printer.StatusText.Contains("completion is not confirmed"));

                received = Exchange(null, Ready, () => send.ExecuteAsync(null));
                check($"{name} blocks a send when status is unavailable", received.Length == 0 && printer.StatusText.Contains("Not sent") && printer.StatusText.Contains("Status unknown"));

                received = Exchange(Ready, null, () => send.ExecuteAsync(null));
                check($"{name} distinguishes a missing post-send status from an unsent job", received == expected() &&
                    printer.StatusText.Contains("Sent to") && printer.StatusText.Contains("Status after send is unknown"));

                statusToggle.IsChecked = false;
                Pump();
                check($"{name} can explicitly disable status checks", !printer.CheckBeforeSending);
                received = Exchange(null, null, () => send.ExecuteAsync(null));
                check($"{name} unchecked send preserves the job without querying", received == expected() && printer.StatusText.Contains("Status was not checked"));
                printer.CheckBeforeSending = true;
                received = Exchange(Paused, Ready, () => send.ExecuteAsync(null));
                string result = printer.StatusText;
                if (tab == MainViewModel.DesignerTab)
                {
                    vm.Designer.NotifyDocumentEdited();
                }
                else
                {
                    vm.Viewer.AutoSize = !vm.Viewer.AutoSize;
                    vm.Viewer.AutoSize = !vm.Viewer.AutoSize;
                }
                Pump(700);
                check($"{name} retains printer feedback after rendering", printer.StatusText == result);
                check($"{name} unlocks print controls after the operation", !printer.IsBusy && checkButton.IsEnabled && sendButton.IsEnabled);
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                Pump();
                check($"{name} printer feedback stays reachable by scrolling", scroll.Offset.Y >= 0 &&
                    panel.Bounds.Height <= scroll.Viewport.Height + scroll.Offset.Y + 1);
                var presenter = scroll.GetVisualAncestors().OfType<Control>().First(c => c.GetType().Name == "FlyoutPresenter");
                using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(presenter.Bounds.Width), (int)Math.Ceiling(presenter.Bounds.Height)));
                image.Render(presenter);
                image.Save(Path.Combine(AppContext.BaseDirectory, $"printer-status-{name.ToLowerInvariant()}.png"), PngBitmapEncoderOptions.Default);
            }
            finally
            {
                flyout.Hide();
                (printer.Host, printer.Port, printer.CheckBeforeSending, printer.StatusText) = settings;
            }

            string Exchange(string? before, string? after, Func<Task> action)
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                hostInput.Text = "127.0.0.1";
                portInput.Value = ((IPEndPoint)listener.LocalEndpoint).Port;
                Pump();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var responseAllowed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<string> receive = Task.Run(async () =>
                {
                    using TcpClient client = await listener.AcceptTcpClientAsync(deadline.Token);
                    using NetworkStream stream = client.GetStream();
                    byte[] first = new byte[3];
                    await stream.ReadExactlyAsync(first, deadline.Token);
                    await responseAllowed.Task.WaitAsync(deadline.Token);
                    using var payload = new MemoryStream();
                    if (Encoding.ASCII.GetString(first) == "~HS")
                    {
                        if (before is null)
                        {
                            return "";
                        }
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(before), deadline.Token);
                    }
                    else
                    {
                        payload.Write(first);
                    }
                    byte[] buffer = new byte[4096];
                    int read;
                    bool answered = false;
                    while ((read = await stream.ReadAsync(buffer, deadline.Token)) > 0)
                    {
                        payload.Write(buffer, 0, read);
                        string text = Encoding.UTF8.GetString(payload.ToArray());
                        if (!answered && text.EndsWith("~HS"))
                        {
                            payload.SetLength(payload.Length - 3);
                            answered = true;
                            if (after is null)
                            {
                                break;
                            }
                            await stream.WriteAsync(Encoding.ASCII.GetBytes(after), deadline.Token);
                        }
                    }
                    return Encoding.UTF8.GetString(payload.ToArray());
                });
                Task operation = action();
                Dispatcher.UIThread.RunJobs();
                if (printer.CheckBeforeSending)
                {
                    check($"{name} disables concurrent print actions", printer.IsBusy && !checkButton.IsEffectivelyEnabled && !sendButton.IsEffectivelyEnabled && !hostInput.IsEffectivelyEnabled);
                }
                responseAllowed.SetResult();
                Wait(operation);
                Wait(receive);
                Pump();
                return receive.Result;
            }
        }
    }

    private static void Wait(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        if (!task.IsCompleted)
        {
            throw new TimeoutException("The loopback print operation did not finish.");
        }
        task.GetAwaiter().GetResult();
    }

    private static void Pump(int milliseconds = 100)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }
}
