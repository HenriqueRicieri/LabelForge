using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LabelForge.App.ViewModels;
using LabelForge.Core.Io;

namespace LabelForge.App.Views;

public partial class DesignerView : UserControl
{
    private TopLevel? _topLevel;
    private bool _syncingScroll;

    public DesignerView()
    {
        InitializeComponent();

        // Custom filter so "4000d 50.8" narrows by material and size together;
        // the rule lives in Core (StockCatalog) and is unit-tested there.
        MediaBox.ItemFilter = (query, item) =>
            item is Core.Media.StockMedia media && Core.Media.StockCatalog.IsMatch(media, query);

        // The recent-files submenu is rebuilt in code: a handful of items, and it
        // sidesteps binding ancestor lookups inside menu popups.
        DataContextChanged += (_, _) => WireRecentFiles();

        Canvas.DocumentEdited += (_, _) => ViewModel?.NotifyDocumentEdited();
        Canvas.LiveEdited += (_, _) => ViewModel?.NotifyDocumentPreview();
        Canvas.DeleteRequested += (_, _) => ViewModel?.DeleteSelectedCommand.Execute(null);
        Canvas.PlaceRequested += (x, y) => ViewModel?.PlaceAt(x, y);
        Canvas.CancelRequested += (_, _) => ViewModel?.CancelInsert();

        Canvas.ViewChanged += (_, _) => SyncScrollBars();
        CanvasHScroll.ValueChanged += (_, _) => OnScrollBarChanged();
        CanvasVScroll.ValueChanged += (_, _) => OnScrollBarChanged();
        ZoomOutButton.Click += (_, _) => Canvas.ZoomBy(1 / 1.25);
        ZoomInButton.Click += (_, _) => Canvas.ZoomBy(1.25);
    }

    /// <summary>Pushes the canvas view (extent, viewport, offset) into the scrollbars
    /// and refreshes the zoom readout. Guarded so the resulting ValueChanged does not
    /// bounce back into the canvas.</summary>
    private void SyncScrollBars()
    {
        var (h, v) = Canvas.GetScrollInfo();
        _syncingScroll = true;
        try
        {
            CanvasHScroll.Maximum = Math.Max(h.Extent - h.Viewport, 0);
            CanvasHScroll.ViewportSize = h.Viewport;
            CanvasHScroll.Value = Math.Clamp(h.Offset, 0, CanvasHScroll.Maximum);

            CanvasVScroll.Maximum = Math.Max(v.Extent - v.Viewport, 0);
            CanvasVScroll.ViewportSize = v.Viewport;
            CanvasVScroll.Value = Math.Clamp(v.Offset, 0, CanvasVScroll.Maximum);
        }
        finally
        {
            _syncingScroll = false;
        }

        ZoomLevelButton.Content = Math.Round(Canvas.GetZoom() * 100)
            .ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    private void OnZoom50(object? sender, RoutedEventArgs e) => Canvas.SetZoom(0.5);

    private void OnZoom100(object? sender, RoutedEventArgs e) => Canvas.SetZoom(1);

    private void OnZoom200(object? sender, RoutedEventArgs e) => Canvas.SetZoom(2);

    private void OnZoomFit(object? sender, RoutedEventArgs e) => Canvas.ResetView();

    private void OnScrollBarChanged()
    {
        if (!_syncingScroll)
        {
            Canvas.SetScrollOffsets(CanvasHScroll.Value, CanvasVScroll.Value);
        }
    }

    private DesignerViewModel? ViewModel => DataContext as DesignerViewModel;

    private void WireRecentFiles()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        vm.RecentFiles.CollectionChanged += (_, _) => RebuildRecentMenu(vm);
        RebuildRecentMenu(vm);
    }

    private void RebuildRecentMenu(DesignerViewModel vm)
    {
        RecentMenu.Items.Clear();
        foreach (string path in vm.RecentFiles)
        {
            RecentMenu.Items.Add(new MenuItem
            {
                Header = path,
                Command = vm.OpenRecentCommand,
                CommandParameter = path,
            });
        }
    }

    private async void OnAddImage(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || ViewModel is not { } vm)
        {
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"] },
            ],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            byte[] data = await File.ReadAllBytesAsync(path);
            if (Core.Imaging.ImageRasterizer.Probe(data) is not { } size)
            {
                vm.StatusText = $"Could not read {Path.GetFileName(path)} as an image";
                return;
            }

            vm.ArmInsertImage(data, size.Width, size.Height);
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Could not read the image: {ex.Message}";
        }
    }

    private void OnThemeSystem(object? sender, RoutedEventArgs e) => SetTheme(Avalonia.Styling.ThemeVariant.Default);

    private void OnThemeLight(object? sender, RoutedEventArgs e) => SetTheme(Avalonia.Styling.ThemeVariant.Light);

    private void OnThemeDark(object? sender, RoutedEventArgs e) => SetTheme(Avalonia.Styling.ThemeVariant.Dark);

    private static void SetTheme(Avalonia.Styling.ThemeVariant variant)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = variant;
        }
    }

    private static FilePickerFileType LflType { get; } =
        new("LabelForge label") { Patterns = ["*.lfl"] };

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || ViewModel is not { } vm)
        {
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open label",
            AllowMultiple = false,
            FileTypeFilter = [LflType],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            vm.LoadDocument(LabelDocumentJson.Deserialize(await File.ReadAllTextAsync(path)), path);
            vm.StatusText = $"Opened {Path.GetFileName(path)}";
            vm.RegisterRecentFile(path);
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Could not open: {ex.Message}";
        }
    }

    private async void OnSaveFile(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (vm.CurrentFilePath is { } path)
        {
            await SaveToAsync(vm, path);
        }
        else
        {
            OnSaveFileAs(sender, e);
        }
    }

    private async void OnSaveFileAs(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || ViewModel is not { } vm)
        {
            return;
        }

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save label",
            DefaultExtension = "lfl",
            SuggestedFileName = "label.lfl",
            FileTypeChoices = [LflType],
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            await SaveToAsync(vm, path);
            vm.CurrentFilePath = path;
        }
    }

    private static async Task SaveToAsync(DesignerViewModel vm, string path)
    {
        try
        {
            await File.WriteAllTextAsync(path, vm.SerializeDocument());
            vm.StatusText = $"Saved {Path.GetFileName(path)}";
            vm.RegisterRecentFile(path);
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Could not save: {ex.Message}";
        }
    }

    private async void OnExportZpl(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || ViewModel is not { } vm)
        {
            return;
        }

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export ZPL",
            DefaultExtension = "zpl",
            SuggestedFileName = "label.zpl",
            FileTypeChoices = [new FilePickerFileType("ZPL") { Patterns = ["*.zpl"] }],
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            // Explicit UTF-8 without a BOM: the label declares ^CI28, and three BOM
            // bytes in front of ^XA are bytes a printer has no reason to tolerate.
            await LabelForge.Core.Io.ZplTextFile.WriteFileAsync(path, vm.GeneratedZpl);
            vm.StatusText = $"Exported {Path.GetFileName(path)}";
        }
    }

    private async void OnExportPdf(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || ViewModel is not { } vm)
        {
            return;
        }

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export PDF",
            DefaultExtension = "pdf",
            SuggestedFileName = "label.pdf",
            FileTypeChoices = [new FilePickerFileType("PDF document") { Patterns = ["*.pdf"] }],
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            try
            {
                byte[] pdf = await vm.RenderPdfAsync();
                await File.WriteAllBytesAsync(path, pdf);
                vm.StatusText = $"Exported {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                vm.StatusText = $"Could not export: {ex.Message}";
            }
        }
    }

    private async void OnExportPng(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || ViewModel is not { } vm)
        {
            return;
        }

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export PNG",
            DefaultExtension = "png",
            SuggestedFileName = "label.png",
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            byte[] png = await vm.RenderPngAsync();
            await File.WriteAllBytesAsync(path, png);
            vm.StatusText = $"Exported {Path.GetFileName(path)}";
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.KeyDown += OnTopLevelKeyDown;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
        {
            _topLevel.KeyDown -= OnTopLevelKeyDown;
            _topLevel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        // The handler sits on the window; ignore shortcuts while another tab is
        // active. Text boxes handle their own Ctrl+Z and mark the event handled
        // before it bubbles up here.
        if (!IsEffectivelyVisible || ViewModel is not { } vm)
        {
            return;
        }

        if (e.Key == Key.Escape && vm.IsPlacing)
        {
            vm.CancelInsert();
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.N:
                if (vm.NewDocumentCommand.CanExecute(null))
                {
                    vm.NewDocumentCommand.Execute(null);
                }

                e.Handled = true;
                break;

            case Key.O:
                OnOpenFile(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.S:
                OnSaveFile(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.C:
                if (vm.CopyCommand.CanExecute(null))
                {
                    vm.CopyCommand.Execute(null);
                }

                e.Handled = true;
                break;

            case Key.V:
                if (vm.PasteCommand.CanExecute(null))
                {
                    vm.PasteCommand.Execute(null);
                }

                e.Handled = true;
                break;

            case Key.D:
                if (vm.DuplicateCommand.CanExecute(null))
                {
                    vm.DuplicateCommand.Execute(null);
                }

                e.Handled = true;
                break;

            case Key.D0 or Key.NumPad0:
                Canvas.ResetView();
                e.Handled = true;
                break;

            case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
            case Key.Y:
                if (vm.RedoCommand.CanExecute(null))
                {
                    vm.RedoCommand.Execute(null);
                }

                e.Handled = true;
                break;

            case Key.Z:
                if (vm.UndoCommand.CanExecute(null))
                {
                    vm.UndoCommand.Execute(null);
                }

                e.Handled = true;
                break;
        }
    }
}
