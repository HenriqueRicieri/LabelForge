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

internal static class SpacingChecks
{
    public static void Run(MainWindow window, DesignerViewModel designer, Action<string, bool> check)
    {
        var canvas = window.GetVisualDescendants().OfType<DesignerView>().Single()
            .FindControl<DesignerCanvas>("Canvas")!;
        var snapping = (designer.SnapToGrid, designer.SnapToGuides, designer.SnapToObjects);
        try
        {
            Load();
            string original = designer.SerializeDocument();
            Move(246, 180, capture: "spacing-horizontal.png");
            check("A dragged field snaps to equal horizontal gaps", Moving().X == 250);
            designer.UndoCommand.Execute(null);
            check("Spacing drag is one undo step", designer.SerializeDocument() == original && !designer.CanUndo);
            designer.RedoCommand.Execute(null);
            check("Redo restores the equal spacing", Moving().X == 250);

            Load(vertical: true);
            Move(180, 246, capture: "spacing-vertical.png");
            check("A dragged field snaps to equal vertical gaps", Moving().Y == 250);

            Load(start: 500);
            Move(696, 180, capture: "spacing-repeat.png");
            check("A drag repeats the gap after two neighbours", Moving().X == 700);
            Load(first: 400, second: 640, start: 200);
            Move(156, 180);
            check("A drag repeats the gap before two neighbours", Moving().X == 160);

            Load(group: true);
            Guid? group = Moving().GroupId;
            Move(246, 180);
            check("Spacing moves the whole group by its outer bounds", Moving().X == 250 &&
                designer.Document.Elements[3].X == 280 && designer.Document.Elements[3].GroupId == group);

            Load();
            designer.SnapToObjects = false;
            Move(246, 180);
            check("Snap to objects controls equal spacing", Moving().X == 246);
            Load();
            Move(246, 180, RawInputModifiers.Alt, release: false);
            check("Alt bypasses equal spacing", Moving().X == 246);
            check("Distance readouts remain visible during a free drag", HasSpacingInk());
            window.MouseUp(At(286, 210), MouseButton.Left);
            Pump(200);
            check("Distance readouts disappear on release", !HasSpacingInk());
            Load();
            designer.Document.Elements[1].IsVisible = false;
            Move(246, 180);
            check("Hidden fields do not attract spacing", Moving().X == 246);

            Load();
            Move(246, 185, RawInputModifiers.Shift);
            check("Spacing honours the horizontal axis lock", Moving().X == 250 && Moving().Y == 180);
            Load();
            designer.Document.VerticalGuides.Add(247);
            designer.SnapToGuides = true;
            Move(246, 180);
            check("A closer guide wins over equal spacing", Moving().X == 247);
            Load();
            designer.Document.HorizontalGuides.Add(185);
            designer.SnapToGuides = true;
            Move(246, 185, RawInputModifiers.Shift);
            check("A nearby guide cannot move the locked axis", Moving().X == 250 && Moving().Y == 180);

            foreach (bool loseCapture in new[] { false, true })
            {
                Load();
                original = designer.SerializeDocument();
                IPointer? pointer = null;
                void Remember(object? sender, PointerPressedEventArgs e) => pointer = e.Pointer;
                canvas.AddHandler(InputElement.PointerPressedEvent, Remember);
                Move(246, 180, release: false);
                canvas.RemoveHandler(InputElement.PointerPressedEvent, Remember);
                check($"{(loseCapture ? "Capture loss" : "Escape")} starts with an equal-spacing snap", Moving().X == 250);
                if (loseCapture) pointer?.Capture(null);
                else window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.MouseUp(At(276, 210), MouseButton.Left);
                Pump(150);
                check($"{(loseCapture ? "Capture loss" : "Escape")} clears the spacing readouts", !HasSpacingInk());
                check($"{(loseCapture ? "Capture loss" : "Escape")} cancels the spacing edit", designer.SerializeDocument() == original && !designer.CanUndo);
            }

            Load();
            Move(246, 121);
            check("Spacing remains valid after snapping the other axis", Moving().X == 246 && Moving().Y == 120);

            Load(group: true);
            designer.Document.Elements[3].IsLocked = true;
            original = designer.SerializeDocument();
            Move(246, 180);
            check("A locked member holds the spacing group", designer.SerializeDocument() == original && !designer.CanUndo);
        }
        finally
        {
            designer.SnapToGrid = snapping.Item1;
            designer.SnapToGuides = snapping.Item2;
            designer.SnapToObjects = snapping.Item3;
            designer.NewDocumentCommand.Execute(null);
            window.MouseMove(new Point(5, 5));
            canvas.ResetView();
            Pump(200);
        }

        bool HasSpacingInk()
        {
            using var frame = window.CaptureRenderedFrame();
            using var stream = new MemoryStream();
            frame!.Save(stream, PngBitmapEncoderOptions.Default);
            using var pixels = SKBitmap.Decode(stream.ToArray());
            return pixels.Pixels.Any(c => c.Red > 200 && c.Green < 120 && c.Blue > 100 && c.Blue < 200);
        }

        BoxElement Moving() => (BoxElement)designer.Document.Elements[2];
        void Load(bool vertical = false, bool group = false, int first = 100, int second = 400, int start = 220)
        {
            var doc = new LabelDocument { WidthMm = 100, HeightMm = 110, Dpmm = 8, CheckQuietZones = false };
            foreach (int position in new[] { first, second, start })
                doc.Elements.Add(new BoxElement { X = vertical ? 180 : position, Y = vertical ? position : 180,
                    WidthDots = 60, HeightDots = 60, ZOrder = doc.Elements.Count });
            if (group)
            {
                doc.Elements[2].GroupId = Guid.NewGuid();
                ((BoxElement)doc.Elements[2]).WidthDots = 30;
                doc.Elements.Add(new BoxElement { X = start + 30, Y = 180, WidthDots = 30, HeightDots = 60,
                    GroupId = doc.Elements[2].GroupId, ZOrder = 3 });
            }
            designer.LoadDocument(doc, path: null);
            designer.Selection.SetMany(doc.Elements.Skip(2));
            designer.SnapToGrid = designer.SnapToGuides = false;
            designer.SnapToObjects = true;
            window.MouseMove(new Point(5, 5));
            canvas.ResetView();
            canvas.SetZoom(0.5);
            Pump(350);
        }
        void Move(int x, int y, RawInputModifiers modifiers = RawInputModifiers.None, bool release = true, string? capture = null)
        {
            var field = Moving();
            int offsetX = field.WidthDots >= 50 ? 40 : 20, offsetY = field.HeightDots / 2;
            window.MouseDown(At(field.X + offsetX, field.Y + offsetY), MouseButton.Left, modifiers & ~RawInputModifiers.Alt);
            window.MouseMove(At(x + offsetX, y + offsetY), RawInputModifiers.LeftMouseButton | modifiers);
            Pump(180);
            if (capture is not null)
            {
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(AppContext.BaseDirectory, capture), PngBitmapEncoderOptions.Default);
            }
            if (release)
            {
                window.MouseUp(At(x + offsetX, y + offsetY), MouseButton.Left, modifiers);
                Pump(200);
            }
        }
        Point At(double x, double y) => canvas.TranslatePoint(canvas.DotsToView(x, y), window)!.Value;
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
