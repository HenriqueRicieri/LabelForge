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

internal static class SelectionScaleChecks
{
    public static void Run(MainWindow window, DesignerViewModel designer, Action<string, bool> check)
    {
        var canvas = window.GetVisualDescendants().OfType<DesignerView>().Single()
            .FindControl<DesignerCanvas>("Canvas")!;
        var snapping = (designer.SnapToGrid, designer.SnapToGuides, designer.SnapToObjects);
        designer.SnapToGrid = designer.SnapToGuides = designer.SnapToObjects = false;
        try
        {
            Load();
            string original = designer.SerializeDocument();
            Drag(400, 320, 600, 480, capture: "selection-scale.png");
            check("Selection corner scales both boxes and their spacing", Boxes(200, 160, 160, 120, 520, 360, 80, 120));
            check("Selection scaling keeps both fields selected", designer.Selection.Count == 2);
            designer.UndoCommand.Execute(null);
            check("One undo restores the whole scaled selection", designer.SerializeDocument() == original && !designer.CanUndo);
            designer.RedoCommand.Execute(null);
            check("Redo restores the whole scaled selection", Boxes(200, 160, 160, 120, 520, 360, 80, 120));

            Load(group: true);
            var group = designer.Document.Elements[0].GroupId;
            Drag(400, 320, 600, 480);
            check("A group scales together without changing membership", Boxes(200, 160, 160, 120, 520, 360, 80, 120) &&
                designer.Document.Elements.All(e => e.GroupId == group));

            Load();
            Drag(200, 160, 100, 80);
            check("Scaling from the top left keeps the opposite corner fixed", Boxes(100, 80, 120, 90, 340, 230, 60, 90));
            Load();
            Drag(400, 240, 500, 240);
            check("A selection edge scales only its axis", Boxes(200, 160, 120, 60, 440, 260, 60, 60));
            Load();
            Drag(400, 320, 500, 400, RawInputModifiers.Control);
            check("Control scales the selection around its centre", Boxes(100, 80, 160, 120, 420, 280, 80, 120));
            Load();
            Drag(400, 320, 500, 360, RawInputModifiers.Shift);
            check("Shift scales selection axes independently", Boxes(200, 160, 120, 75, 440, 285, 60, 75));
            Load();
            Drag(400, 320, 500, 340, RawInputModifiers.Control | RawInputModifiers.Shift);
            check("Control and Shift combine centre scaling with independent axes", Boxes(100, 140, 160, 75, 420, 265, 80, 75));

            Load(guides: true);
            Drag(400, 320, 498, 358, RawInputModifiers.Shift, snap: true);
            check("Snapping preserves independent selection axes", Boxes(200, 160, 120, 75, 440, 285, 60, 75));
            Load(guides: true);
            Drag(400, 240, 498, 240, RawInputModifiers.Control, snap: true);
            check("Snapping preserves the selection centre", Boxes(100, 160, 160, 60, 420, 260, 80, 60));

            foreach (bool lostCapture in new[] { false, true })
            {
                Load();
                original = designer.SerializeDocument();
                IPointer? pointer = null;
                void Remember(object? sender, PointerPressedEventArgs e) => pointer = e.Pointer;
                canvas.AddHandler(InputElement.PointerPressedEvent, Remember);
                window.MouseDown(At(400, 320), MouseButton.Left);
                canvas.RemoveHandler(InputElement.PointerPressedEvent, Remember);
                window.MouseMove(At(600, 480), RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
                Pump(120);
                check($"{(lostCapture ? "Capture loss" : "Escape")} starts with resized fields", Boxes(200, 160, 160, 120, 520, 360, 80, 120));
                if (lostCapture) pointer?.Capture(null);
                else window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.MouseUp(At(600, 480), MouseButton.Left);
                Pump(200);
                check($"{(lostCapture ? "Capture loss" : "Escape")} restores all scaled fields without undo", designer.SerializeDocument() == original && !designer.CanUndo);
            }

            Load();
            original = designer.SerializeDocument();
            window.MouseDown(At(400, 320), MouseButton.Left);
            window.MouseMove(At(600, 480), RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            window.MouseMove(At(400, 320), RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            window.MouseUp(At(400, 320), MouseButton.Left);
            Pump(200);
            check("Returning a scale gesture to its start leaves no edit", designer.SerializeDocument() == original && !designer.CanUndo);

            Load();
            original = designer.SerializeDocument();
            window.MouseDown(At(400, 320), MouseButton.Left);
            window.MouseMove(At(600, 480), RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            designer.Selection.Set(designer.Document.Elements[0]);
            window.MouseUp(At(600, 480), MouseButton.Left);
            Pump(200);
            check("Changing selection cancels the scale gesture", designer.SerializeDocument() == original && !designer.CanUndo);

            Load();
            window.MouseDown(At(400, 320), MouseButton.Left);
            window.MouseMove(At(600, 480), RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            designer.NewDocumentCommand.Execute(null);
            window.MouseUp(At(600, 480), MouseButton.Left);
            Pump(200);
            check("Changing documents cancels selection scaling", designer.Document.Elements.Count == 0 && !designer.CanUndo);

            Load(group: true, locked: true);
            original = designer.SerializeDocument();
            Drag(400, 320, 600, 480);
            check("A locked group member prevents scaling the group", designer.SerializeDocument() == original && !designer.CanUndo);

            var dense = new LabelDocument { WidthMm = 800d / 24, HeightMm = 640d / 24, Dpmm = 24, CheckQuietZones = false };
            var box = new BoxElement { X = 200, Y = 160, WidthDots = 80, HeightDots = 60 };
            var qr = new QrCodeElement { X = 360, Y = 260, Data = "Scale", Magnification = 3, ZOrder = 1 };
            dense.Elements.Add(box);
            dense.Elements.Add(qr);
            designer.LoadDocument(dense, path: null);
            designer.Selection.SetMany(dense.Elements);
            canvas.ResetView();
            canvas.SetZoom(0.5);
            Pump(500);
            original = designer.SerializeDocument();
            DotRect denseBounds = LabelForge.Core.Editing.SelectionScale.GetBounds(dense.Elements.ToArray());
            Point corner = At(denseBounds.X + denseBounds.Width, denseBounds.Y + denseBounds.Height);
            Point enlarged = At(denseBounds.X + denseBounds.Width * 1.1, denseBounds.Y + denseBounds.Height * 1.1);
            window.MouseDown(corner, MouseButton.Left, RawInputModifiers.Alt);
            check("600 dpi selection scaling prepares cached preview layers", Until(() => designer.GesturePreview?.Moving is not null));
            var moving = designer.GesturePreview?.Moving;
            window.MouseMove(enlarged, RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            check("600 dpi selection scaling redraws the moving fields", Until(() => designer.GesturePreview?.Moving is not null && !ReferenceEquals(moving, designer.GesturePreview.Moving)));
            check("Selection scaling keeps a QR on its module step", qr.Magnification == 3 && box.WidthDots == 88 && qr.X > 360);
            using (var frame = window.CaptureRenderedFrame())
                frame!.Save(Path.Combine(AppContext.BaseDirectory, "selection-scale-quantized.png"), PngBitmapEncoderOptions.Default);
            window.MouseUp(enlarged, MouseButton.Left, RawInputModifiers.Alt);
            check("Releasing a scaled selection refreshes the full preview", Until(() => designer.GesturePreview is null) && designer.CanUndo);
            designer.UndoCommand.Execute(null);
            check("Undo restores the quantized selection exactly", designer.SerializeDocument() == original && !designer.CanUndo);

            Load(density: 12, pitch: 2.5);
            designer.SnapToGrid = true;
            canvas.Focus();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            check("Arrow moves the selection by one grid pitch", designer.Document.Elements[0].X == 230 && designer.Document.Elements[1].X == 390);
            window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
            check("Shift arrow moves by ten grid pitches", designer.Document.Elements[0].Y == 460 && designer.Document.Elements[1].Y == 560);
            window.KeyPress(Key.Left, RawInputModifiers.Alt, PhysicalKey.ArrowLeft, null);
            check("Alt arrow keeps one-dot precision with the grid on", designer.Document.Elements[0].X == 229 && designer.Document.Elements[1].X == 389);
            window.KeyPress(Key.Left, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.ArrowLeft, null);
            check("Alt Shift arrow moves by ten dots", designer.Document.Elements[0].X == 219 && designer.Document.Elements[1].X == 379);
            designer.SnapToGrid = false;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            check("Disabling grid snapping restores one-dot nudging", designer.Document.Elements[0].X == 220);

            Load();
            designer.SnapToGrid = true;
            canvas.Focus();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            check("A hidden design grid keeps one-dot nudging", designer.Document.Elements[0].X == 201);
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

        void Load(bool group = false, bool locked = false, bool guides = false, int density = 8, double pitch = 0)
        {
            Guid? id = group ? Guid.NewGuid() : null;
            var document = new LabelDocument { WidthMm = 100, HeightMm = 80, Dpmm = density, GridPitchMm = pitch, CheckQuietZones = false };
            document.Elements.Add(new BoxElement { Name = "First", X = 200, Y = 160, WidthDots = 80, HeightDots = 60, ZOrder = 0, GroupId = id });
            document.Elements.Add(new BoxElement { Name = "Second", X = 360, Y = 260, WidthDots = 40, HeightDots = 60, ZOrder = 1, GroupId = id, IsLocked = locked });
            if (guides) { document.VerticalGuides.Add(500); document.HorizontalGuides.Add(360); }
            designer.LoadDocument(document, path: null);
            designer.Selection.SetMany(document.Elements);
            designer.SnapToGuides = guides;
            designer.SnapToGrid = false;
            window.MouseMove(new Point(5, 5));
            canvas.ResetView();
            canvas.SetZoom(0.5);
            Pump(300);
        }

        bool Boxes(int x1, int y1, int w1, int h1, int x2, int y2, int w2, int h2) =>
            designer.Document.Elements is [BoxElement a, BoxElement b] &&
            (a.X, a.Y, a.WidthDots, a.HeightDots) == (x1, y1, w1, h1) &&
            (b.X, b.Y, b.WidthDots, b.HeightDots) == (x2, y2, w2, h2);

        void Drag(int x1, int y1, int x2, int y2, RawInputModifiers modifiers = RawInputModifiers.None,
            bool snap = false, string? capture = null)
        {
            if (!snap) modifiers |= RawInputModifiers.Alt;
            window.MouseDown(At(x1, y1), MouseButton.Left, modifiers);
            window.MouseMove(At(x2, y2), RawInputModifiers.LeftMouseButton | modifiers);
            Pump(120);
            if (capture is not null)
            {
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(AppContext.BaseDirectory, capture), PngBitmapEncoderOptions.Default);
            }
            window.MouseUp(At(x2, y2), MouseButton.Left, modifiers);
            Pump(200);
        }

        bool Until(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(5)) Pump(30);
            return condition();
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
