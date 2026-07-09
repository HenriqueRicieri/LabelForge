using System;
using System.ComponentModel;
using System.Linq;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using LabelForge.App.ViewModels;

namespace LabelForge.App.Views;

public partial class MainWindow : Window
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 20.0;

    private double _zoom = 1.0;
    private bool _fitMode = true;
    private bool _updatingEditorText;

    public MainWindow()
    {
        InitializeComponent();
        LoadZplHighlighting();

        ZplEditor.TextChanged += OnEditorTextChanged;
        DataContextChanged += OnDataContextChanged;
        PreviewScroll.EffectiveViewportChanged += (_, _) =>
        {
            if (_fitMode)
            {
                FitToWindow();
            }
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private Bitmap? CurrentImage => ViewModel?.PreviewImage;

    private void LoadZplHighlighting()
    {
        using var stream = AssetLoader.Open(new Uri("avares://LabelForge.App/Assets/Zpl.xshd"));
        using var reader = XmlReader.Create(stream);
        ZplEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ViewModel is { } vm)
        {
            PushTextToEditor(vm.ZplText);
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.PreviewImage):
                if (_fitMode)
                {
                    FitToWindow();
                }
                else
                {
                    ApplyZoom();
                }

                break;

            case nameof(MainViewModel.ZplText):
                // The view model changed the text itself (e.g. opening a file).
                if (ViewModel is { } vm && vm.ZplText != ZplEditor.Text)
                {
                    PushTextToEditor(vm.ZplText);
                }

                break;
        }
    }

    private void PushTextToEditor(string text)
    {
        _updatingEditorText = true;
        ZplEditor.Text = text;
        _updatingEditorText = false;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (!_updatingEditorText && ViewModel is { } vm)
        {
            vm.ZplText = ZplEditor.Text;
        }
    }

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open ZPL file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ZPL files") { Patterns = ["*.zpl", "*.ZPL", "*.txt"] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });

        var file = files.FirstOrDefault();
        if (file is null || ViewModel is not { } vm)
        {
            return;
        }

        await using var stream = await file.OpenReadAsync();
        using var reader = new System.IO.StreamReader(stream);
        vm.LoadZpl(await reader.ReadToEndAsync());
        Title = $"LabelForge - {file.Name}";
    }

    private void ApplyZoom()
    {
        _zoom = Math.Clamp(_zoom, MinZoom, MaxZoom);
        PreviewTransform.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        ZoomLabel.Text = $"{Math.Round(_zoom * 100)}%";
    }

    private void FitToWindow()
    {
        Bitmap? image = CurrentImage;
        if (image is null)
        {
            return;
        }

        Size viewport = PreviewScroll.Viewport;
        double imageWidth = image.Size.Width;
        double imageHeight = image.Size.Height;
        if (imageWidth <= 0 || imageHeight <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        _zoom = Math.Min(viewport.Width / imageWidth, viewport.Height / imageHeight);
        ApplyZoom();
    }

    private void OnFit(object? sender, RoutedEventArgs e)
    {
        _fitMode = true;
        FitToWindow();
    }

    private void OnActual(object? sender, RoutedEventArgs e)
    {
        _fitMode = false;
        _zoom = 1.0;
        ApplyZoom();
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => ZoomBy(1.2);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => ZoomBy(1.0 / 1.2);

    private void ZoomBy(double factor)
    {
        _fitMode = false;
        _zoom *= factor;
        ApplyZoom();
    }

    private void OnPreviewWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ZoomBy(e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15);
            e.Handled = true;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.O:
                OnOpenFile(sender, e);
                e.Handled = true;
                break;
            case Key.D0 or Key.NumPad0:
                OnActual(sender, e);
                e.Handled = true;
                break;
            case Key.D9 or Key.NumPad9:
                OnFit(sender, e);
                e.Handled = true;
                break;
            case Key.OemPlus or Key.Add:
                ZoomBy(1.2);
                e.Handled = true;
                break;
            case Key.OemMinus or Key.Subtract:
                ZoomBy(1.0 / 1.2);
                e.Handled = true;
                break;
        }
    }
}
