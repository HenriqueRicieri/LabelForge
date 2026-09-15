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
using LabelForge.Core.Editing;
using LabelForge.Core.Model;
using SkiaSharp;

internal static class GesturePreviewChecks
{
    public static void Run(MainWindow window, DesignerViewModel designer, Action<string, bool> check)
    {
        var view = window.GetVisualDescendants().OfType<DesignerView>().Single();
        var canvas = view.FindControl<DesignerCanvas>("Canvas")!;
        var snapping = (designer.SnapToGuides, designer.SnapToGrid, designer.SnapToObjects);
        bool dotGrid = designer.ShowPrinterDotGrid;
        designer.SnapToGuides = designer.SnapToGrid = designer.SnapToObjects = false;
        designer.ShowPrinterDotGrid = false;
        try
        {
            Move();
            Cancel();
            CaptureLost();
            Resize();
            Rotate();
            Lifecycle();
            Pasteboard();
            Fallbacks();
        }
        finally
        {
            designer.EndGesturePreview(false);
            designer.SnapToGuides = snapping.Item1;
            designer.SnapToGrid = snapping.Item2;
            designer.SnapToObjects = snapping.Item3;
            designer.ShowPrinterDotGrid = dotGrid;
            designer.NewDocumentCommand.Execute(null);
            canvas.ResetView();
            Pump(200);
        }

        void Move()
        {
            var mover = Load();
            var underlay = designer.Underlay;
            Point start = At(200, 205);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(22.5, 0), RawInputModifiers.LeftMouseButton);
            check("600 dpi pointer move prepares gesture layers", Until(() => designer.GesturePreview?.Moving is not null));
            var layers = designer.GesturePreview;
            var moving = layers?.Moving;
            bool stable = layers is not null;
            foreach (int dots in new[] { 65, 100, 130 })
            {
                window.MouseMove(start + new Vector(dots * 0.75, 0), RawInputModifiers.LeftMouseButton);
                Pump(100);
                stable &= ReferenceEquals(underlay, designer.Underlay) &&
                    ReferenceEquals(moving, designer.GesturePreview?.Moving);
            }
            check("Move reuses the underlay and moving bitmap across frames", stable);
            check("Move updates the live position and compositor offset", mover.X == 290 && Math.Abs(designer.GestureOffset.X - 130) < 0.01);
            using var during = Capture(window);
            Save(during, "gesture-moving.png");
            check("Moving layer clears the previous ink position", Light(during, 200, 205));
            check("Moving layer paints the translated ink", !Light(during, 345, 205));
            check("Above layer keeps white ink over the moving layer", Light(during, 305, 205));
            window.MouseUp(start + new Vector(97.5, 0), MouseButton.Left);
            check("Release replaces the full underlay and removes gesture layers", Until(() =>
                designer.GesturePreview is null && !ReferenceEquals(underlay, designer.Underlay)));
            using var after = Capture(window);
            Save(after, "gesture-committed.png");
            check("Committed render matches gesture ink at the moved and masked positions",
                Light(after, 200, 205) && !Light(after, 345, 205) && Light(after, 305, 205));
            check("Move records an undo step", designer.CanUndo);
            designer.UndoCommand.Execute(null);
            Pump(450);
            check("One undo restores the moved element", designer.Document.Elements.Single(e => e.Name == "Moving").X == 160 && !designer.CanUndo);
        }

        void Cancel()
        {
            Load();
            string before = designer.SerializeDocument();
            Point start = At(200, 205);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(45, 15), RawInputModifiers.LeftMouseButton);
            check("Escape case starts with a live gesture preview", Until(() => designer.GesturePreview is not null));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.MouseUp(start + new Vector(45, 15), MouseButton.Left);
            Pump(450);
            check("Escape restores the snapshot without an undo step", designer.SerializeDocument() == before && !designer.CanUndo);
            check("Escape removes layers and resets their offset", designer.GesturePreview is null && designer.GestureOffset == default(Vector));
        }

        void CaptureLost()
        {
            Load();
            string before = designer.SerializeDocument();
            IPointer? pointer = null;
            void RememberPointer(object? sender, PointerPressedEventArgs e) => pointer = e.Pointer;
            canvas.AddHandler(InputElement.PointerPressedEvent, RememberPointer, RoutingStrategies.Tunnel);
            Point start = At(200, 205);
            window.MouseDown(start, MouseButton.Left);
            canvas.RemoveHandler(InputElement.PointerPressedEvent, RememberPointer);
            window.MouseMove(start + new Vector(45, 0), RawInputModifiers.LeftMouseButton);
            check("Capture loss case starts with a captured pointer and layers",
                Until(() => designer.GesturePreview is not null) && pointer?.Captured == canvas);
            pointer?.Capture(null);
            Pump(400);
            check("Capture loss restores the document and clears layers without undo",
                designer.SerializeDocument() == before && designer.GesturePreview is null && !designer.CanUndo);
            window.MouseUp(start + new Vector(45, 0), MouseButton.Left);
        }
        void Resize()
        {
            var qr = new QrCodeElement { X = 160, Y = 170, Data = "H5 RESIZE", Magnification = 4 };
            Load(qr);
            var bounds = new ElementBoundsCalculator().GetBounds(qr);
            Point handle = At(bounds.X + bounds.Width, bounds.Y + bounds.Height / 2.0);
            var underlay = designer.Underlay;
            window.MouseDown(handle, MouseButton.Left);
            check("Resize handle prepares gesture layers", Until(() => designer.GesturePreview?.Moving is not null));
            var layers = designer.GesturePreview;
            var moving = layers?.Moving;
            window.MouseMove(handle + new Vector(3.75, 0), RawInputModifiers.LeftMouseButton);
            Pump(250);
            check("Quantized resize leaves the module and moving bitmap unchanged", qr.Magnification == 4 && moving is not null && ReferenceEquals(moving, designer.GesturePreview?.Moving));
            Point enlarged = handle + new Vector(bounds.Width * 0.75 / 4, 0);
            window.MouseMove(enlarged, RawInputModifiers.LeftMouseButton);
            check("A module change replaces only the moving bitmap", Until(() => qr.Magnification == 5 &&
                designer.GesturePreview?.Moving is { } current && !ReferenceEquals(moving, current)) &&
                layers is not null && ReferenceEquals(layers.Below, designer.GesturePreview?.Below) &&
                ReferenceEquals(layers.Above, designer.GesturePreview?.Above) && ReferenceEquals(underlay, designer.Underlay));
            window.MouseUp(enlarged, MouseButton.Left);
            check("Resize release refreshes the full preview", Until(() => designer.GesturePreview is null && !ReferenceEquals(underlay, designer.Underlay)));

            qr = new QrCodeElement { X = 160, Y = 170, Data = "UNCHANGED", Magnification = 4 };
            Load(qr);
            bounds = new ElementBoundsCalculator().GetBounds(qr);
            handle = At(bounds.X + bounds.Width, bounds.Y + bounds.Height / 2.0);
            underlay = designer.Underlay;
            window.MouseDown(handle, MouseButton.Left);
            check("An unchanged resize can prepare layers", Until(() => designer.GesturePreview?.Moving is not null));
            window.MouseUp(handle, MouseButton.Left);
            Pump(350);
            check("Unchanged resize clears layers without undo or a new bitmap",
                designer.GesturePreview is null && !designer.CanUndo && ReferenceEquals(underlay, designer.Underlay));
        }

        void Rotate()
        {
            var text = new TextElement { X = 160, Y = 170, Text = "TURN", FontHeightDots = 42 };
            Load(text);
            var bounds = new ElementBoundsCalculator().GetBounds(text);
            Point top = canvas.DotsToView(bounds.X + bounds.Width / 2.0, bounds.Y);
            Point handle = canvas.TranslatePoint(new Point(top.X, top.Y - 26), window)!.Value;
            var underlay = designer.Underlay;
            window.MouseDown(handle, MouseButton.Left);
            check("Rotation handle prepares gesture layers", Until(() => designer.GesturePreview?.Moving is not null));
            var layers = designer.GesturePreview;
            var moving = layers?.Moving;
            Point turned = handle + new Vector(70, 70);
            window.MouseMove(turned, RawInputModifiers.LeftMouseButton);
            check("Rotation rerenders the moving layer and retains static layers", Until(() => text.Orientation != Orientation.Normal &&
                designer.GesturePreview?.Moving is { } current && !ReferenceEquals(moving, current)) &&
                layers is not null && ReferenceEquals(layers.Below, designer.GesturePreview?.Below) &&
                ReferenceEquals(layers.Above, designer.GesturePreview?.Above) && ReferenceEquals(underlay, designer.Underlay));
            window.MouseUp(turned, MouseButton.Left);
            check("Rotation release removes layers and refreshes the full preview", Until(() =>
                designer.GesturePreview is null && !ReferenceEquals(underlay, designer.Underlay)));
        }

        void Lifecycle()
        {
            var mover = Load();
            designer.BeginGesturePreview(GestureKind.Move, [mover], ElementSnapshot.Capture([mover]));
            designer.EndGesturePreview(false);
            Pump(450);
            check("An ended gesture cannot publish its pending layers", designer.GesturePreview is null);
            designer.BeginGesturePreview(GestureKind.Move, [mover], ElementSnapshot.Capture([mover]));
            var replacement = new LabelDocument { WidthMm = 20, HeightMm = 15, Dpmm = 24 };
            designer.LoadDocument(replacement, path: null);
            Pump(500);
            check("Document replacement discards in-flight gesture layers", ReferenceEquals(designer.Document, replacement) && designer.GesturePreview is null);
            mover = Load();
            designer.BeginGesturePreview(GestureKind.Move, [mover], ElementSnapshot.Capture([mover]));
            check("A fresh gesture works after cancellation and document replacement", Until(() => designer.GesturePreview?.Moving is not null));
            designer.EndGesturePreview(false);
            Pump(200);
        }

        void Pasteboard()
        {
            var mover = Load(new BoxElement { X = -470, Y = 170, WidthDots = 80, HeightDots = 70, ThicknessDots = 70 });
            designer.BeginGesturePreview(GestureKind.Move, [mover], ElementSnapshot.Capture([mover]));
            check("A field inside the pasteboard can use gesture layers", Until(() => designer.GesturePreview?.Moving is not null));
            var underlay = designer.Underlay;
            mover.X = -490;
            designer.GestureOffset = new Vector(-20, 0);
            designer.NotifyDocumentPreview();
            check("Crossing the pasteboard origin resumes the full renderer", Until(() =>
                designer.GesturePreview is null && !ReferenceEquals(underlay, designer.Underlay)));
            check("Off-label movement keeps the placement warning live", !string.IsNullOrWhiteSpace(designer.PlacementWarning));
            designer.EndGesturePreview(true);
            designer.NotifyDocumentEdited();
            Pump(200);
        }
        void Fallbacks()
        {
            foreach (int density in new[] { 8, 12 })
            {
                var mover = Load(density: density);
                Fallback($"{density} dpmm keeps the regular preview", [mover]);
            }
            var current = Load();
            designer.Document.IsContinuous = true;
            Fallback("Continuous stock keeps the regular preview", [current]);
            current = Load();
            designer.Document.Print.ReverseAll = true;
            Fallback("Label reverse keeps the regular preview", [current]);
            current = Load();
            current.IsReversed = true;
            Fallback("A reversed moving field keeps the regular preview", [current]);
            current = Load();
            designer.Document.Elements[^1].IsReversed = true;
            Fallback("A reversed upper field keeps the regular preview", [current]);
            Load();
            Fallback("A noncontiguous selection keeps the regular preview", [designer.Document.Elements[0], designer.Document.Elements[2]]);
            current = Load(new TextElement { X = 160, Y = 170, Text = "Wrapped text", FontHeightDots = 30, BlockWidthDots = 150, BlockMaxLines = 2 });
            Fallback("Field-block text keeps the regular preview", [current]);
            current = Load(new TextElement { X = 160, Y = 170, Text = "Bitmap font", Font = 'A', FontHeightDots = 30 });
            Fallback("A fixed bitmap font keeps the regular preview", [current]);
        }

        void Fallback(string name, IReadOnlyList<Element> moving)
        {
            designer.NotifyDocumentEdited();
            Pump(250);
            var underlay = designer.Underlay;
            designer.BeginGesturePreview(GestureKind.Move, moving, ElementSnapshot.Capture(moving));
            foreach (var element in moving) element.X += 12;
            designer.NotifyDocumentPreview();
            bool showedLayers = false;
            for (int i = 0; i < 15; i++)
            {
                Pump(20);
                showedLayers |= designer.GesturePreview is not null;
            }
            check(name, !showedLayers && designer.GesturePreview is null && !ReferenceEquals(underlay, designer.Underlay));
            designer.EndGesturePreview(true);
            designer.NotifyDocumentEdited();
            Pump(100);
        }

        Element Load(Element? mover = null, int density = 24)
        {
            designer.EndGesturePreview(false);
            mover ??= new BoxElement { X = 160, Y = 170, WidthDots = 80, HeightDots = 70, ThicknessDots = 70 };
            mover.Name = "Moving";
            mover.ZOrder = 1;
            var document = new LabelDocument { WidthMm = 720d / density, HeightMm = 576d / density, Dpmm = density, CheckQuietZones = false };
            document.Elements.Add(new BoxElement { X = 80, Y = 90, WidthDots = 500, HeightDots = 350, ThicknessDots = 4, ZOrder = 0 });
            document.Elements.Add(mover);
            document.Elements.Add(new BoxElement { X = 290, Y = 170, WidthDots = 30, HeightDots = 70, ThicknessDots = 30, IsWhite = true, ZOrder = 2 });
            designer.LoadDocument(document, path: null);
            designer.Selection.Set(mover);
            window.MouseMove(new Point(5, 5));
            canvas.ResetView();
            canvas.SetZoom(0.75);
            Pump(550);
            return mover;
        }

        Point At(double x, double y) => canvas.TranslatePoint(canvas.DotsToView(x, y), window)!.Value;
        bool Light(SKBitmap bitmap, int x, int y)
        {
            Point at = At(x, y);
            return bitmap.GetPixel((int)Math.Round(at.X), (int)Math.Round(at.Y)).Red > 220;
        }
    }

    private static void Save(SKBitmap bitmap, string name)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, name));
        png.SaveTo(stream);
    }

    private static SKBitmap Capture(Window window)
    {
        using var frame = window.CaptureRenderedFrame();
        using var stream = new MemoryStream();
        frame!.Save(stream, PngBitmapEncoderOptions.Default);
        return SKBitmap.Decode(stream.ToArray());
    }

    private static bool Until(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.ElapsedMilliseconds < 3000) Pump(20);
        Pump(40);
        return condition();
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
