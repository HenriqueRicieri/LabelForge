using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelForge.App.Controls;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Model;
using SkiaSharp;

internal static class MarqueeSelectionChecks
{
    public static void Run(MainWindow window, DesignerViewModel designer, Action<string, bool> check)
    {
        var view = window.GetVisualDescendants().OfType<DesignerView>().Single();
        var canvas = view.FindControl<DesignerCanvas>("Canvas")!;
        try
        {
            Load();
            string before = designer.SerializeDocument();
            string zpl = designer.GeneratedZpl;
            var underlay = designer.Underlay;
            Drag(100, 100, 250, 250, capture: "marquee-enclosed.png", crossing: false);
            check("Left-to-right marquee selects only enclosed fields", Names() == "Inside");
            check("Marquee preserves label, ZPL, bitmap and undo", before == designer.SerializeDocument() &&
                zpl == designer.GeneratedZpl && ReferenceEquals(underlay, designer.Underlay) && !designer.CanUndo);

            Load();
            Drag(250, 100, 100, 250, capture: "marquee-crossing.png", crossing: true);
            check("Right-to-left marquee also selects crossed fields", Names() == "Inside,Partial");
            Load();
            Drag(100, 250, 250, 100);
            check("Upward left-to-right marquee still requires enclosure", Names() == "Inside");
            Load();
            Drag(250, 250, 100, 100);
            check("Upward right-to-left marquee still selects crossings", Names() == "Inside,Partial");

            foreach (RawInputModifiers modifier in new[] { RawInputModifiers.Control, RawInputModifiers.Shift })
            {
                Load();
                designer.Selection.Set(designer.Document.Elements.Single(e => e.Name == "Outside"));
                Pump(100);
                Drag(100, 100, 250, 250, modifier);
                check($"{modifier} marquee adds to the existing selection", Names() == "Inside,Outside");
            }

            Load(group: true);
            Drag(100, 100, 250, 250);
            check("Enclosure does not select a partly enclosed group", designer.Selection.Count == 0);
            Load(group: true);
            Drag(250, 100, 100, 250);
            check("Crossing a group member selects the whole group", Names() == "Inside,Outside,Partial");

            Load();
            designer.Document.Elements[0].IsLocked = true;
            designer.Document.Elements[1].IsVisible = false;
            designer.NotifyDocumentEdited();
            Pump(300);
            Drag(250, 100, 100, 250);
            check("Marquee can select locked fields and ignores hidden fields", Names() == "Inside");

            Load();
            canvas.SetZoom(0.75);
            var scroll = canvas.GetScrollInfo();
            canvas.SetScrollOffsets(scroll.Horizontal.Offset + 19.25, scroll.Vertical.Offset + 11.75);
            Pump(150);
            Drag(100, 100, 250, 250);
            check("Enclosure follows printer dots after zoom and pan", Names() == "Inside");

            Load();
            designer.Selection.Set(designer.Document.Elements.Single(e => e.Name == "Outside"));
            Drag(100, 100, 100, 100, RawInputModifiers.Control);
            check("An empty additive marquee preserves the selection", Names() == "Outside");
            Drag(100, 100, 100, 100);
            check("A plain click on empty stock clears the selection", designer.Selection.Count == 0);

            Load();
            designer.Selection.Set(designer.Document.Elements.Single(e => e.Name == "Outside"));
            window.MouseDown(At(100, 100), MouseButton.Left);
            window.MouseMove(At(250, 250), RawInputModifiers.LeftMouseButton);
            Pump(80);
            var replacement = new LabelDocument { WidthMm = 75, HeightMm = 50, Dpmm = 8 };
            replacement.Elements.Add(new BoxElement { X = 120, Y = 120, WidthDots = 80, HeightDots = 40 });
            designer.LoadDocument(replacement, path: null);
            Pump(300);
            window.MouseUp(At(250, 250), MouseButton.Left);
            Pump(180);
            check("Replacing the document cancels the marquee without restoring old fields",
                ReferenceEquals(designer.Document, replacement) && designer.Selection.Count == 0 && !designer.CanUndo);

            Cancel(loseCapture: false);
            Cancel(loseCapture: true);
        }
        finally
        {
            designer.NewDocumentCommand.Execute(null);
            window.MouseMove(new Point(5, 5));
            canvas.ResetView();
            Pump(150);
        }

        void Cancel(bool loseCapture)
        {
            Load();
            designer.Selection.Set(designer.Document.Elements.Single(e => e.Name == "Outside"));
            Pump(100);
            IPointer? pointer = null;
            void RememberPointer(object? sender, PointerPressedEventArgs e) => pointer = e.Pointer;
            canvas.AddHandler(InputElement.PointerPressedEvent, RememberPointer, RoutingStrategies.Tunnel);
            window.MouseDown(At(100, 100), MouseButton.Left);
            canvas.RemoveHandler(InputElement.PointerPressedEvent, RememberPointer);
            window.MouseMove(At(250, 250), RawInputModifiers.LeftMouseButton);
            Pump(80);
            if (loseCapture) pointer?.Capture(null);
            else window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Pump(80);
            string reason = loseCapture ? "Capture loss" : "Escape";
            check($"{reason} restores the selection before the marquee", Names() == "Outside");
            window.MouseUp(At(250, 250), MouseButton.Left);
            Pump(180);
            check($"{reason} prevents marquee release from selecting fields", Names() == "Outside" && !designer.CanUndo);
        }

        void Load(bool group = false)
        {
            Guid? id = group ? Guid.NewGuid() : null;
            var document = new LabelDocument { WidthMm = 75, HeightMm = 50, Dpmm = 8 };
            document.Elements.Add(new BoxElement { Name = "Inside", X = 120, Y = 120, WidthDots = 80, HeightDots = 40, ZOrder = 0, GroupId = id });
            document.Elements.Add(new BoxElement { Name = "Partial", X = 220, Y = 170, WidthDots = 80, HeightDots = 40, ZOrder = 1 });
            document.Elements.Add(new BoxElement { Name = "Outside", X = 420, Y = 120, WidthDots = 60, HeightDots = 40, ZOrder = 2, GroupId = id });
            designer.LoadDocument(document, path: null);
            designer.Selection.Clear();
            window.MouseMove(new Point(5, 5));
            canvas.ResetView();
            canvas.SetZoom(1);
            Pump(400);
        }

        void Drag(double x1, double y1, double x2, double y2, RawInputModifiers modifiers = RawInputModifiers.None,
            string? capture = null, bool crossing = false)
        {
            window.MouseDown(At(x1, y1), MouseButton.Left, modifiers);
            window.MouseMove(At(x2, y2), RawInputModifiers.LeftMouseButton | modifiers);
            Pump(80);
            if (capture is not null)
            {
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(AppContext.BaseDirectory, capture), PngBitmapEncoderOptions.Default);
                using var stream = new MemoryStream();
                frame.Save(stream, PngBitmapEncoderOptions.Default);
                using var bitmap = SKBitmap.Decode(stream.ToArray());
                Point top = At(Math.Min(x1, x2), Math.Min(y1, y2));
                int ink = 0;
                for (int x = (int)top.X + 10; x < (int)top.X + 140; x++)
                {
                    bool blue = false;
                    for (int y = (int)top.Y - 1; y <= (int)top.Y + 1; y++)
                    {
                        SKColor pixel = bitmap.GetPixel(x, y);
                        blue |= pixel.Blue > 170 && pixel.Red < 150;
                    }
                    if (blue) ink++;
                }
                check(crossing ? "Crossing marquee uses a dashed outline" : "Enclosure marquee uses a solid outline",
                    crossing ? ink is > 30 and < 110 : ink > 120);
            }
            window.MouseUp(At(x2, y2), MouseButton.Left, modifiers);
            Pump(180);
        }

        Point At(double x, double y) => canvas.TranslatePoint(canvas.DotsToView(x, y), window)!.Value;
        string Names() => string.Join(",", designer.Selection.Items.Select(e => e.Name).Order());
    }

    private static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(10);
        }
    }
}
