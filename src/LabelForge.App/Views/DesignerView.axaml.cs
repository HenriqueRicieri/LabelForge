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

    public DesignerView()
    {
        InitializeComponent();

        Canvas.DocumentEdited += (_, _) => ViewModel?.NotifyDocumentEdited();
        Canvas.DeleteRequested += (_, _) => ViewModel?.DeleteSelectedCommand.Execute(null);
    }

    private DesignerViewModel? ViewModel => DataContext as DesignerViewModel;

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
            await File.WriteAllTextAsync(path, vm.GeneratedZpl);
            vm.StatusText = $"Exported {Path.GetFileName(path)}";
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
        if (!IsEffectivelyVisible || ViewModel is not { } vm ||
            !e.KeyModifiers.HasFlag(KeyModifiers.Control))
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
