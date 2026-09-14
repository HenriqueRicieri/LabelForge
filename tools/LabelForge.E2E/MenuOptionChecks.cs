using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelForge.App.Controls;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Settings;

internal static class MenuOptionChecks
{
    public static void Run(MainWindow window, DesignerViewModel designer, string settingsPath, Action<string, bool> check)
    {
        var view = window.GetVisualDescendants().OfType<DesignerView>().Single();
        var canvas = view.FindControl<DesignerCanvas>("Canvas")!;
        var viewMenu = view.FindControl<MenuItem>("ViewOptionsMenu")!;
        var editMenu = view.FindControl<MenuItem>("EditOptionsMenu")!;
        var arrange = view.FindControl<Button>("ArrangeButton")!;
        var store = new UserSettingsStore(settingsPath);
        designer.NewDocumentCommand.Execute(null);
        designer.SnapToGuides = true;
        designer.SnapToGrid = true;
        designer.SnapToObjects = true;
        designer.AlignToLabel = false;
        Pump(300);
        string document = designer.SerializeDocument();
        bool undo = designer.CanUndo;
        foreach (var option in new[]
        {
            ("SnapGuidesMenu", (Func<bool>)(() => designer.SnapToGuides), (Func<bool>)(() => canvas.SnapToGuides), (Func<bool>)(() => store.Load().SnapToGuides)),
            ("SnapGridMenu", () => designer.SnapToGrid, () => canvas.SnapToGrid, () => store.Load().SnapToGrid),
            ("SnapObjectsMenu", () => designer.SnapToObjects, () => canvas.SnapToObjects, () => store.Load().SnapToObjects),
        })
        {
            var item = view.FindControl<MenuItem>(option.Item1)!;
            Activate(item, () => viewMenu.Open());
            check($"{option.Item1}: activation updates the setting", !option.Item2());
            check($"{option.Item1}: activation reaches the canvas", !option.Item3());
            check($"{option.Item1}: preference survives reload", !option.Item4());
            Activate(item, () => viewMenu.Open());
            check($"{option.Item1}: second activation restores snapping", option.Item2() && option.Item3() && option.Item4());
        }
        var alignEdit = view.FindControl<MenuItem>("AlignEditMenu")!;
        var alignArrange = view.FindControl<MenuItem>("AlignArrangeMenu")!;
        Activate(alignEdit, () => editMenu.Open());
        check("Edit alignment preference writes back", designer.AlignToLabel && store.Load().AlignToLabel);
        arrange.Flyout!.ShowAt(arrange);
        Pump(100);
        check("Arrange reflects the Edit alignment preference", alignArrange.IsChecked);
        arrange.Flyout.Hide();
        Activate(alignArrange, () => arrange.Flyout.ShowAt(arrange));
        check("Arrange alignment preference writes back", !designer.AlignToLabel && !store.Load().AlignToLabel);
        check("Edit reflects the Arrange alignment preference", !alignEdit.IsChecked);
        check("Preference menus preserve document and undo", designer.SerializeDocument() == document && designer.CanUndo == undo);

        var quiet = view.FindControl<MenuItem>("QuietZonesMenu")!;
        string zpl = designer.GeneratedZpl;
        Activate(quiet, () => viewMenu.Open());
        check("Quiet zones activation edits the label setting", !designer.CheckQuietZones && !designer.Document.CheckQuietZones);
        check("Quiet zones activation records undo", designer.CanUndo);
        designer.UndoCommand.Execute(null);
        Pump(250);
        check("Undo restores Quiet zones and the menu", designer.CheckQuietZones && quiet.IsChecked);
        designer.RedoCommand.Execute(null);
        Pump(250);
        check("Redo restores Quiet zones and the menu", !designer.CheckQuietZones && !quiet.IsChecked);
        check("Quiet zones leaves ZPL unchanged", designer.GeneratedZpl == zpl);
        designer.NewDocumentCommand.Execute(null);
        Pump(150);

        void Activate(MenuItem item, Action open)
        {
            int clicks = 0;
            void OnClick(object? sender, RoutedEventArgs e) => clicks++;
            item.Click += OnClick;
            open();
            Pump(100);
            item.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Pump(100);
            item.Click -= OnClick;
            check($"{item.Name}: Enter activates the menu item", clicks == 1);
            viewMenu.Close();
            editMenu.Close();
            arrange.Flyout?.Hide();
        }
    }

    private static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(20);
        }
    }
}
