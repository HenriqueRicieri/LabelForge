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

        // Same reasoning for the grid pitches: a fixed handful, and the tick beside the
        // active one has to be recomputed each time the menu opens.
        GridMenu.SubmenuOpened += (_, _) => BuildGridMenu();
        BuildGridMenu();

        Canvas.DocumentEdited += (_, _) => ViewModel?.NotifyDocumentEdited();
        Canvas.LiveEdited += (_, _) => ViewModel?.NotifyDocumentPreview();
        Canvas.DeleteRequested += (_, _) => ViewModel?.DeleteSelectedCommand.Execute(null);
        Canvas.ContextMenuRequested += OnCanvasContextMenu;
        Canvas.PlaceRequested += (x, y) => ViewModel?.PlaceAt(x, y);
        Canvas.CancelRequested += (_, _) => ViewModel?.CancelInsert();

        Canvas.ViewChanged += (_, _) => SyncScrollBars();
        CanvasHScroll.ValueChanged += (_, _) => OnScrollBarChanged();
        CanvasVScroll.ValueChanged += (_, _) => OnScrollBarChanged();
        ZoomOutButton.Click += (_, _) => Canvas.ZoomBy(1 / 1.25);
        ZoomInButton.Click += (_, _) => Canvas.ZoomBy(1.25);
    }

    /// <summary>
    /// Builds the canvas context menu for whatever the pointer was over.
    ///
    /// Two menus rather than one long one with half of it greyed out: right-clicking an
    /// element asks about that element, and right-clicking bare stock asks what to put
    /// there. Everything offered runs the same command the menu bar and the keyboard run,
    /// so there is one implementation of copy and one of delete, not three.
    /// </summary>
    private void OnCanvasContextMenu(LabelForge.Core.Model.Element? element, int x, int y)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        MenuFlyout menu = element is null ? EmptyCanvasMenu(vm, x, y) : ElementMenu(vm, element);
        if (menu.Items.Count > 0)
        {
            menu.ShowAt(Canvas, showAtPointer: true);
        }
    }

    private MenuFlyout ElementMenu(DesignerViewModel vm, LabelForge.Core.Model.Element element)
    {
        var menu = new MenuFlyout();
        Add(menu, "Copy", vm.CopyCommand);
        Add(menu, "Duplicate", vm.DuplicateCommand);
        Add(menu, "Delete", vm.DeleteSelectedCommand);
        menu.Items.Add(new Separator());
        Add(menu, "Bring to Front", vm.BringToFrontCommand);
        Add(menu, "Send to Back", vm.SendToBackCommand);
        menu.Items.Add(new Separator());

        // The two switches that are otherwise a trip to the properties panel, and the two
        // most likely to be wanted for several elements one after another.
        var frame = new MenuItem { Header = "Zoom to Selection" };
        frame.Click += (_, _) => Canvas.ZoomToSelection();
        menu.Items.Add(frame);
        menu.Items.Add(new Separator());

        var locked = new MenuItem { Header = "Lock position", Icon = Check(element.IsLocked) };
        locked.Click += (_, _) =>
        {
            element.IsLocked = !element.IsLocked;
            vm.NotifyDocumentEdited();
        };
        menu.Items.Add(locked);

        var suppressed = new MenuItem { Header = "Do not print", Icon = Check(element.DoNotPrint) };
        suppressed.Click += (_, _) =>
        {
            element.DoNotPrint = !element.DoNotPrint;
            vm.NotifyDocumentEdited();
        };
        menu.Items.Add(suppressed);

        // Alignment only means something once there is more than one thing to align.
        if (vm.SelectionCount > 1)
        {
            menu.Items.Add(new Separator());
            var align = new MenuItem { Header = "Align" };
            Add(align, "Left", vm.AlignLeftCommand);
            Add(align, "Center", vm.AlignCenterHorizontalCommand);
            Add(align, "Right", vm.AlignRightCommand);
            align.Items.Add(new Separator());
            Add(align, "Top", vm.AlignTopCommand);
            Add(align, "Middle", vm.AlignMiddleCommand);
            Add(align, "Bottom", vm.AlignBottomCommand);
            align.Items.Add(new Separator());
            Add(align, "Distribute Horizontally", vm.DistributeHorizontalCommand);
            Add(align, "Distribute Vertically", vm.DistributeVerticalCommand);
            menu.Items.Add(align);
        }

        return menu;
    }

    private MenuFlyout EmptyCanvasMenu(DesignerViewModel vm, int x, int y)
    {
        var menu = new MenuFlyout();

        var pasteHere = new MenuItem
        {
            Header = "Paste Here",
            IsEnabled = vm.CanPaste,
        };
        pasteHere.Click += (_, _) => vm.PasteAt(x, y);
        menu.Items.Add(pasteHere);
        Add(menu, "Paste", vm.PasteCommand);
        Add(menu, "Select All", vm.SelectAllCommand);
        menu.Items.Add(new Separator());

        // Insert places where the click landed. Arming a tool and asking for a second
        // click would be asking twice for something already said.
        var insert = new MenuItem { Header = "Insert Here" };
        InsertItem(insert, "Text", vm, vm.AddTextCommand, x, y);
        InsertItem(insert, "Box", vm, vm.AddBoxCommand, x, y);
        InsertItem(insert, "Line", vm, vm.AddLineCommand, x, y);
        InsertItem(insert, "Barcode", vm, vm.AddBarcodeCommand, x, y);
        InsertItem(insert, "QR Code", vm, vm.AddQrCommand, x, y);
        InsertItem(insert, "Data Matrix", vm, vm.AddDataMatrixCommand, x, y);
        InsertItem(insert, "PDF417", vm, vm.AddPdf417Command, x, y);
        menu.Items.Add(insert);

        menu.Items.Add(new Separator());
        var frame = new MenuItem { Header = "Zoom to Selection" };
        frame.Click += (_, _) => Canvas.ZoomToSelection();
        menu.Items.Add(frame);

        var fit = new MenuItem { Header = "Fit to Window" };
        fit.Click += (_, _) => Canvas.ResetView();
        menu.Items.Add(fit);

        var actual = new MenuItem { Header = "Zoom 100%" };
        actual.Click += (_, _) => Canvas.SetZoom(1);
        menu.Items.Add(actual);

        return menu;
    }

    private static void InsertItem(
        MenuItem parent,
        string header,
        DesignerViewModel vm,
        System.Windows.Input.ICommand arm,
        int x,
        int y)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => vm.InsertAt(arm, x, y);
        parent.Items.Add(item);
    }

    private static void Add(MenuFlyout menu, string header, System.Windows.Input.ICommand command) =>
        menu.Items.Add(new MenuItem { Header = header, Command = command });

    private static void Add(MenuItem parent, string header, System.Windows.Input.ICommand command) =>
        parent.Items.Add(new MenuItem { Header = header, Command = command });

    /// <summary>A tick beside a switch that is on. Avalonia has no checkable menu item,
    /// and a header that changed between "Lock" and "Unlock" would make the reader work
    /// out the current state from the verb.</summary>
    private static Control? Check(bool on) =>
        on ? new TextBlock { Text = "✓" } : null;

    /// <summary>
    /// The grid pitches on offer, with a tick beside the one in force.
    ///
    /// A handful of presets rather than a free number: the useful pitches are the ones a
    /// ruler already reads in, and a box accepting 3.7 mm would invite a grid nothing else
    /// on the label lines up with.
    /// </summary>
    private void BuildGridMenu()
    {
        GridMenu.Items.Clear();
        if (ViewModel is not { } vm)
        {
            return;
        }

        foreach (double pitch in new[] { 0d, 1, 2, 2.5, 5, 10 })
        {
            double value = pitch;
            var item = new MenuItem
            {
                Header = value <= 0
                    ? "Off"
                    : value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + " mm",
                Icon = Check(Math.Abs(vm.GridPitchMm - value) < 0.001),
            };
            item.Click += (_, _) => vm.GridPitchMm = value;
            GridMenu.Items.Add(item);
        }
    }

    /// <summary>Opens the keyboard and mouse reference. Modal to the window, because it
    /// is something you consult and close rather than work beside.</summary>
    private async void OnShowShortcuts(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        await new ShortcutsWindow().ShowDialog(owner);
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

    /// <summary>Opens a ZPL label as a document. Read through ZplTextFile so a legacy
    /// CP1252 file keeps its accents instead of collecting replacement characters.</summary>
    private async void OnImportZpl(object? sender, RoutedEventArgs e)
    {
        if (await PickZplFile("Import a ZPL label") is not { } picked)
        {
            return;
        }

        (DesignerViewModel vm, string path) = picked;
        try
        {
            ZplTextRead read = await ZplTextFile.ReadFileAsync(path);
            vm.ImportZplDocument(read.Text, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Could not read the file: {ex.Message}";
        }
    }

    /// <summary>Pulls the logos and stamps out of an existing label. The file is read
    /// through ZplTextFile, not a plain reader, so a legacy CP1252 label does not lose
    /// its accents on the way in even though only the graphics are used here.</summary>
    /// <summary>
    /// Turns a data box into a marker-completing one.
    ///
    /// The default AutoCompleteBox behaviour replaces the whole box with the chosen
    /// item, which is wrong here: real fields read "MA,##CODIGO_BARRAS##" or "Lote
    /// ##LOTE## / ##SERIE##", and completing one marker must not delete the rest of the
    /// field. So both halves are marker-aware. The filter only offers anything while the
    /// caret is inside an unterminated marker, and the selector splices the chosen field
    /// into that marker and leaves everything around it alone.
    /// </summary>
    private void OnFieldBoxAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not AutoCompleteBox box)
        {
            return;
        }

        box.TextFilter = (search, item) =>
        {
            string? fragment = OpenFragment(search);
            return fragment is not null && item is not null &&
                   item.Contains(fragment, StringComparison.OrdinalIgnoreCase);
        };

        box.ItemSelector = (search, item) =>
        {
            string marker = item as string ?? string.Empty;
            if (search is null || OpenFragment(search) is null)
            {
                return marker;
            }

            // Everything before the marker being typed is kept verbatim.
            int start = search.LastIndexOf(Open, StringComparison.Ordinal);
            return search[..start] + marker;
        };
    }

    /// <summary>The part of a field's text that is being typed as a marker: what follows
    /// the last opening delimiter, when that delimiter has not been closed again. Null
    /// when the caret is not inside a marker, which is how completion stays out of the
    /// way while ordinary text is typed.</summary>
    private string? OpenFragment(string? text)
    {
        if (text is null)
        {
            return null;
        }

        int start = text.LastIndexOf(Open, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        string tail = text[(start + Open.Length)..];
        return tail.Contains(Close, StringComparison.Ordinal) ? null : tail;
    }

    private string Open => ViewModel?.Document.Markers.Open ?? "##";

    private string Close => ViewModel?.Document.Markers.Close ?? "##";

    /// <summary>Imports a field list, or a script whose public methods can be called
    /// from a marker. One entry point for both, because which one a file is can be seen
    /// rather than asked. Deliberately not restricted by extension either: the sample
    /// exports were tab separated despite being named .csv, and the reader does not
    /// care.</summary>
    private async void OnImportFieldCatalog(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || ViewModel is not { } vm)
        {
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a field list or a script",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Field lists and scripts")
                {
                    Patterns = ["*.csv", "*.tsv", "*.txt", "*.cs"],
                },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            string text = await File.ReadAllTextAsync(path);
            vm.ImportFieldCatalog(text, Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Could not read the file: {ex.Message}";
        }
    }

    private async void OnImportGraphics(object? sender, RoutedEventArgs e)
    {
        if (await PickZplFile("Import graphics from a ZPL file") is not { } picked)
        {
            return;
        }

        (DesignerViewModel vm, string path) = picked;
        try
        {
            ZplTextRead read = await ZplTextFile.ReadFileAsync(path);
            vm.ImportGraphicsFromZpl(read.Text, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Could not read the file: {ex.Message}";
        }
    }

    /// <summary>Shared open dialog for the two ZPL import entries. Null when the window
    /// or view model is not up yet, or when the user cancels.</summary>
    private async Task<(DesignerViewModel Vm, string Path)?> PickZplFile(string title)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || ViewModel is not { } vm)
        {
            return null;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ZPL files") { Patterns = ["*.zpl", "*.ZPL", "*.prn", "*.txt"] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });

        return files.FirstOrDefault()?.TryGetLocalPath() is { } path ? (vm, path) : null;
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

            // The work is safe in its own file now, so the crash snapshot would only be a
            // false alarm on the next start.
            vm.ClearRecovery();
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

    /// <summary>
    /// Writes exactly the bytes a print would send.
    ///
    /// Distinct from Export ZPL, which writes the label the ZPL pane shows. A run whose
    /// counter this machine expands is one block per copy rather than one block and a
    /// quantity, and the timestamp on a date field is taken as the job is built. Those
    /// differences are the reason this exists: for a support ticket, a diff against what
    /// the printer actually received, or a fixture to test against later.
    /// </summary>
    private async void OnExportPrintJob(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || ViewModel is not { } vm)
        {
            return;
        }

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export the print job",
            DefaultExtension = "zpl",
            SuggestedFileName = "print-job.zpl",
            FileTypeChoices = [new FilePickerFileType("ZPL") { Patterns = ["*.zpl"] }],
        });

        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            LabelForge.Core.Zpl.PrintJobResult job = vm.BuildPrintJob();
            await LabelForge.Core.Io.ZplTextFile.WriteFileAsync(path, job.Zpl);

            // A run that fell back from a printer-side feature says so here as well as at
            // print time, since this file is the thing someone will read later.
            string warnings = job.Warnings.Count > 0
                ? " " + string.Join(" ", job.Warnings)
                : string.Empty;
            vm.StatusText =
                $"Exported {Path.GetFileName(path)}: {vm.DescribeJob(job)}.{warnings}";
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Could not export the print job: {ex.Message}";
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

            case Key.D0 or Key.NumPad0 when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                Canvas.ZoomToSelection();
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
