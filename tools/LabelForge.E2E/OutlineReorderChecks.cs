using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelForge.App.Controls;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Model;
using SkiaSharp;

internal static class OutlineReorderChecks
{
    public static void Run(MainWindow window, DesignerViewModel designer, Action<string, bool> check)
    {
        var view = window.GetVisualDescendants().OfType<DesignerView>().Single();
        var tabs = view.FindControl<TabControl>("InspectorTabs")!;
        int oldTab = tabs.SelectedIndex;
        tabs.SelectedIndex = 1;
        var list = view.FindControl<ListBox>("ElementsList")!;
        var canvas = view.FindControl<DesignerCanvas>("Canvas")!;
        Load();
        check("Outline starts with the front element", Names() == "White,Black,Back");
        check("Front white box covers the black box", Pixel() > 240);
        Drag("Black", "White", true);
        check("Dragging a row changes the outline order", Names() == "Black,White,Back");
        check("Dragging a row changes the canvas ink", Pixel() < 20);
        check("Dragging retains z-order values", Values() == "10,20,30");
        check("Dragging keeps the moved element selected", designer.Selection.Primary?.Name == "Black");
        designer.UndoCommand.Execute(null);
        Pump(650);
        check("One undo restores the stacking order", Names() == "White,Black,Back" && Pixel() > 240);
        check("One drag records one undo step", !designer.CanUndo);
        designer.RedoCommand.Execute(null);
        Pump(650);
        check("Redo restores the dragged order", Names() == "Black,White,Back" && Pixel() < 20);

        Load();
        Point start = RowPoint("Black", 0.5);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(0, 2), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Vector(0, 2), MouseButton.Left);
        Pump(150);
        check("A short pointer movement only selects a row", Names() == "White,Black,Back" && !designer.CanUndo);
        start = RowPoint("Black", 0.5);
        var target = RowPoint("White", 0.2);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(target, RawInputModifiers.LeftMouseButton);
        Pump(100);
        check("Dragging shows an insertion marker", view.FindControl<Border>("OutlineDropMarker")?.IsVisible == true);
        using (var frame = window.CaptureRenderedFrame())
            frame!.Save(Path.Combine(AppContext.BaseDirectory, "designer-outline-drag.png"), PngBitmapEncoderOptions.Default);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.MouseUp(target, MouseButton.Left);
        Pump(150);
        check("Escape cancels a row drag without undo", Names() == "White,Black,Back" && !designer.CanUndo);
        check("Cancellation clears the insertion marker", view.FindControl<Border>("OutlineDropMarker")?.IsVisible != true);
        start = RowPoint("Black", 0.5);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(200, 200), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(200, 200), MouseButton.Left);
        Pump(150);
        check("Dropping outside the list does not reorder", Names() == "White,Black,Back" && !designer.CanUndo);

        Load(group: true);
        Drag("Black", "Back", false);
        check("Dragging a member moves the entire group", Names() == "Back,White,Black");
        check("The outline keeps one contiguous group header", designer.Outline.Count(r => r.IsGroupHeader) == 1);
        check("Dragging a group retains its members and values", designer.Selection.Count == 2 && Values() == "10,20,30");
        designer.UndoCommand.Execute(null);
        Pump(650);
        check("Undo restores the whole group", Names() == "White,Black,Back" && !designer.CanUndo);
        Load(group: true);
        Drag("Back", "Black", true);
        check("Dropping beside a member does not split its group", Names() == "Back,White,Black" && designer.Outline.Count(r => r.IsGroupHeader) == 1);
        Load();
        designer.Document.Elements.Single(e => e.Name == "Black").IsVisible = false;
        designer.NotifyDocumentEdited();
        Pump(600);
        Drag("Black", "White", true);
        check("A hidden row can still be reordered", Names() == "Black,White,Back" && !designer.Document.Elements.Single(e => e.Name == "Black").IsVisible);
        Load(group: true, locked: true);
        Drag("Black", "Back", false);
        check("A locked member blocks dragging its group", Names() == "White,Black,Back" && !designer.CanUndo);
        Load();
        var toggle = Row("Black").GetVisualDescendants().OfType<CheckBox>().First();
        var togglePoint = toggle.TranslatePoint(new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window)!.Value;
        window.MouseDown(togglePoint, MouseButton.Left);
        window.MouseUp(togglePoint, MouseButton.Left);
        Pump(500);
        check("The row visibility control still toggles", !designer.Document.Elements.Single(e => e.Name == "Black").IsVisible);
        check("The visibility control does not reorder", Names() == "White,Black,Back");
        var longDocument = new LabelDocument();
        for (int i = 0; i < 40; i++)
            longDocument.Elements.Add(new BoxElement { Name = $"Layer {i}", ZOrder = i, X = 40, Y = 40, WidthDots = 30, HeightDots = 30 });
        designer.LoadDocument(longDocument, path: null);
        Pump(650);
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroll.Offset = default;
        Pump(100);
        start = RowPoint("Layer 39", 0.5);
        var edge = list.TranslatePoint(new Point(50, list.Bounds.Height - 10), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        for (int i = 0; i < 35; i++)
        {
            window.MouseMove(edge, RawInputModifiers.LeftMouseButton);
            Pump(30);
        }
        check("A row drag scrolls a long outline", scroll.Offset.Y > 100);
        window.MouseUp(edge, MouseButton.Left);
        Pump(650);
        check("A row can move beyond the initial viewport", designer.Outline.ToList().FindIndex(r => r.Element.Name == "Layer 39") > 5);
        designer.UndoCommand.Execute(null);
        Pump(650);
        check("Scrolling during a drag still records one undo step", designer.Outline.First().Element.Name == "Layer 39" && !designer.CanUndo);
        scroll.Offset = default;
        Pump(100);
        start = RowPoint("Layer 39", 0.5);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(RowPoint("Layer 37", 0.8), RawInputModifiers.LeftMouseButton);
        tabs.SelectedIndex = 0;
        window.MouseUp(start, MouseButton.Left);
        Pump(100);
        check("Changing inspector tabs cancels an active drag", !designer.CanUndo && view.FindControl<Border>("OutlineDropMarker")?.IsVisible != true);
        designer.NewDocumentCommand.Execute(null);
        tabs.SelectedIndex = oldTab;
        canvas.ResetView();
        Pump(200);

        void Load(bool group = false, bool locked = false)
        {
            var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
            document.Elements.Add(new BoxElement { Name = "Back", X = 300, Y = 80, WidthDots = 30, HeightDots = 30, ZOrder = 10 });
            var black = new BoxElement { Name = "Black", X = 80, Y = 80, WidthDots = 120, HeightDots = 120, ThicknessDots = 120, ZOrder = 20 };
            var white = new BoxElement { Name = "White", X = 80, Y = 80, WidthDots = 120, HeightDots = 120, ThicknessDots = 120, IsWhite = true, ZOrder = 30, IsLocked = locked };
            if (group) black.GroupId = white.GroupId = Guid.NewGuid();
            document.Elements.Add(black);
            document.Elements.Add(white);
            designer.LoadDocument(document, path: null);
            canvas.ResetView();
            Pump(650);
        }
        string Names() => string.Join(",", designer.Outline.Where(r => !r.IsGroupHeader).Select(r => r.Element.Name));
        string Values() => string.Join(",", designer.Document.Elements.Select(e => e.ZOrder).Order());
        ListBoxItem Row(string name) => list.GetVisualDescendants().OfType<ListBoxItem>()
            .First(r => r.DataContext is ElementOutlineViewModel vm && !vm.IsGroupHeader && vm.Element.Name == name);
        Point RowPoint(string name, double fraction)
        {
            var row = Row(name);
            return row.TranslatePoint(new Point(50, row.Bounds.Height * fraction), window)!.Value;
        }
        void Drag(string source, string target, bool above)
        {
            window.MouseDown(RowPoint(source, 0.5), MouseButton.Left);
            var destination = RowPoint(target, above ? 0.2 : 0.8);
            window.MouseMove(destination, RawInputModifiers.LeftMouseButton);
            Pump(100);
            window.MouseUp(destination, MouseButton.Left);
            Pump(650);
        }
        byte Pixel()
        {
            using var frame = window.CaptureRenderedFrame();
            using var stream = new MemoryStream();
            frame!.Save(stream, PngBitmapEncoderOptions.Default);
            using var pixels = SKBitmap.Decode(stream.ToArray());
            Point point = canvas.TranslatePoint(canvas.DotsToView(140, 140), window)!.Value;
            return pixels.GetPixel((int)point.X, (int)point.Y).Red;
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
