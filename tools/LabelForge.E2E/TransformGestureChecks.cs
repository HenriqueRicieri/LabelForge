using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelForge.App.Controls;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Model;

internal static class TransformGestureChecks
{
    public static void Run(MainWindow window, DesignerViewModel designer, Action<string, bool> check)
    {
        var canvas = window.GetVisualDescendants().OfType<DesignerView>().Single()
            .FindControl<DesignerCanvas>("Canvas")!;
        var snapping = (designer.SnapToGrid, designer.SnapToGuides, designer.SnapToObjects);
        designer.SnapToGrid = designer.SnapToGuides = designer.SnapToObjects = false;
        try
        {
            BoxElement box = LoadBox();
            window.MouseDown(At(280, 190), MouseButton.Left,
                RawInputModifiers.Control | RawInputModifiers.Alt);
            window.MouseMove(At(300, 190), RawInputModifiers.LeftMouseButton |
                RawInputModifiers.Control | RawInputModifiers.Alt);
            window.MouseUp(At(300, 190), MouseButton.Left,
                RawInputModifiers.Control | RawInputModifiers.Alt);
            check("Control resize keeps a single field centered",
                (box.X, box.WidthDots) == (180, 120));

            var qrDocument = new LabelDocument { WidthMm = 100, HeightMm = 80, Dpmm = 8,
                CheckQuietZones = false };
            var qr = new QrCodeElement { X = 200, Y = 160, Data = "Center",
                Magnification = 3 };
            qrDocument.Elements.Add(qr);
            designer.LoadDocument(qrDocument, path: null);
            designer.Selection.Set(qr);
            canvas.ResetView();
            canvas.SetZoom(0.5);
            Pump(300);
            DotRect qrBefore = new ElementBoundsCalculator().GetBounds(qr);
            Point qrHandle = At(qrBefore.X + qrBefore.Width, qrBefore.Y + qrBefore.Height / 2.0);
            window.MouseDown(qrHandle, MouseButton.Left, RawInputModifiers.Control);
            window.MouseMove(At(qrBefore.X + qrBefore.Width + 40, qrBefore.Y + qrBefore.Height / 2.0),
                RawInputModifiers.LeftMouseButton | RawInputModifiers.Control);
            window.MouseUp(At(qrBefore.X + qrBefore.Width + 40, qrBefore.Y + qrBefore.Height / 2.0),
                MouseButton.Left, RawInputModifiers.Control);
            DotRect qrAfter = new ElementBoundsCalculator().GetBounds(qr);
            check("Control resize keeps an offset QR footprint centered",
                qr.Magnification > 3 &&
                Math.Abs(qrAfter.X + qrAfter.Width / 2.0 -
                    (qrBefore.X + qrBefore.Width / 2.0)) <= 0.5 &&
                Math.Abs(qrAfter.Y + qrAfter.Height / 2.0 -
                    (qrBefore.Y + qrBefore.Height / 2.0)) <= 0.5);

            box = LoadBox();
            designer.Document.VerticalGuides.Add(295);
            designer.SnapToGuides = true;
            window.MouseDown(At(280, 190), MouseButton.Left, RawInputModifiers.Control);
            window.MouseMove(At(300, 190), RawInputModifiers.LeftMouseButton | RawInputModifiers.Control);
            window.MouseMove(At(310, 190), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(310, 190), MouseButton.Left);
            check("Releasing Control uses the opposite resize anchor for snapping",
                (box.X, box.WidthDots) == (200, 110));
            designer.SnapToGuides = false;

            box = LoadBox();
            designer.Document.VerticalGuides.Add(285);
            designer.SnapToGuides = true;
            string beforeHandleClick = designer.SerializeDocument();
            bool couldUndoHandleClick = designer.CanUndo;
            window.MouseDown(At(280, 190), MouseButton.Left);
            window.MouseMove(At(280, 190), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(280, 190), MouseButton.Left);
            check("Clicking a resize handle does not snap or edit",
                (box.X, box.WidthDots) == (200, 80) &&
                designer.SerializeDocument() == beforeHandleClick &&
                designer.CanUndo == couldUndoHandleClick);
            designer.SnapToGuides = false;

            box = LoadBox();
            designer.Document.VerticalGuides.Add(285);
            designer.SnapToGuides = true;
            string beforeRoundTrip = designer.SerializeDocument();
            bool couldUndoRoundTrip = designer.CanUndo;
            window.MouseDown(At(280, 190), MouseButton.Left);
            window.MouseMove(At(300, 190), RawInputModifiers.LeftMouseButton);
            window.MouseMove(At(280, 190), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(280, 190), MouseButton.Left);
            check("Returning a resize to its press point ignores nearby guides",
                designer.SerializeDocument() == beforeRoundTrip &&
                designer.CanUndo == couldUndoRoundTrip);
            designer.SnapToGuides = false;

            box = LoadBox();
            window.MouseDown(At(220, 180), MouseButton.Left);
            window.MouseMove(At(230, 180), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(250, 180), MouseButton.Left);
            check("Move commits the pointer position on release", box.X == 230);

            box = LoadBox();
            window.MouseDown(At(220, 180), MouseButton.Left);
            window.MouseMove(At(250, 185), RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
            window.MouseMove(At(225, 220), RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
            window.MouseUp(At(225, 220), MouseButton.Left, RawInputModifiers.Shift);
            check("Shift move switches axis after a clear direction change",
                (box.X, box.Y) == (200, 200));

            box = LoadBox();
            window.MouseDown(At(220, 180), MouseButton.Left);
            window.MouseMove(At(250, 185), RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
            window.MouseUp(At(250, 185), MouseButton.Left);
            check("Releasing Shift before the mouse restores free movement",
                (box.X, box.Y) == (230, 165));

            box = LoadBox();
            window.MouseDown(At(220, 180), MouseButton.Left, RawInputModifiers.Alt);
            window.MouseMove(At(250, 180), RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            window.MouseUp(At(250, 180), MouseButton.Left, RawInputModifiers.Alt);
            check("Alt drag moves a field without snapping", box.X == 230);

            box = LoadBox();
            var topBox = new BoxElement { X = 200, Y = 160, WidthDots = 80,
                HeightDots = 60, ZOrder = 1 };
            designer.Document.Elements.Add(topBox);
            designer.Selection.Set(topBox);
            window.MouseDown(At(220, 180), MouseButton.Left, RawInputModifiers.Alt);
            window.MouseUp(At(220, 180), MouseButton.Left, RawInputModifiers.Alt);
            check("Alt click still selects the next overlapping field",
                ReferenceEquals(designer.Selection.Primary, box));

            box = LoadBox();
            topBox = new BoxElement { X = 200, Y = 160, WidthDots = 80,
                HeightDots = 60, ZOrder = 1 };
            designer.Document.Elements.Add(topBox);
            designer.Selection.Set(topBox);
            window.MouseDown(At(220, 180), MouseButton.Left, RawInputModifiers.Alt);
            window.MouseMove(At(250, 180), RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            window.MouseUp(At(250, 180), MouseButton.Left, RawInputModifiers.Alt);
            check("Alt drag moves the chosen field under an overlap",
                box.X == 230 && topBox.X == 200 &&
                ReferenceEquals(designer.Selection.Primary, box));

            box = LoadBox();
            window.MouseDown(At(280, 190), MouseButton.Left, RawInputModifiers.Alt);
            window.MouseMove(At(300, 190), RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            window.MouseUp(At(310, 190), MouseButton.Left, RawInputModifiers.Alt);
            check("Resize commits the pointer position on release",
                (box.X, box.WidthDots) == (200, 110));

            var document = new LabelDocument { WidthMm = 100, HeightMm = 80, Dpmm = 8,
                CheckQuietZones = false };
            var text = new TextElement { X = 200, Y = 160, Text = "Rotation",
                FontHeightDots = 40 };
            document.Elements.Add(text);
            designer.LoadDocument(document, path: null);
            designer.Selection.Set(text);
            canvas.ResetView();
            canvas.SetZoom(0.5);
            Pump(300);
            DotRect bounds = new ElementBoundsCalculator().GetBounds(text);
            string beforeRotation = designer.SerializeDocument();
            Point top = At(bounds.X + bounds.Width / 2.0, bounds.Y);
            Point center = At(bounds.X + bounds.Width / 2.0,
                bounds.Y + bounds.Height / 2.0);
            Point handle = new(top.X, top.Y - 26);
            Point quarterTurn = new(center.X + center.Y - handle.Y, center.Y);
            window.MouseDown(handle, MouseButton.Left);
            window.MouseUp(quarterTurn, MouseButton.Left);
            check("Rotation commits the pointer angle on release",
                text.Orientation == Orientation.Rotated90);

            designer.UndoCommand.Execute(null);
            check("Undo restores text after a rotation",
                designer.SerializeDocument() == beforeRotation);
            text = designer.Document.Elements.OfType<TextElement>().Single();
            designer.Selection.Set(text);
            Pump(250);
            DotRect secondStart = new ElementBoundsCalculator().GetBounds(text);
            bool couldUndo = designer.CanUndo;
            Point secondTop = At(secondStart.X + secondStart.Width / 2.0, secondStart.Y);
            Point secondCenter = At(secondStart.X + secondStart.Width / 2.0,
                secondStart.Y + secondStart.Height / 2.0);
            Point secondHandle = new(secondTop.X, secondTop.Y - 26);
            Point secondQuarter = new(secondCenter.X + secondCenter.Y - secondHandle.Y,
                secondCenter.Y);
            window.MouseDown(secondHandle, MouseButton.Left);
            window.MouseMove(secondQuarter, RawInputModifiers.LeftMouseButton);
            window.MouseMove(secondHandle, RawInputModifiers.LeftMouseButton);
            window.MouseUp(secondHandle, MouseButton.Left);
            check("Reversing a rotation restores orientation and drawn center",
                text.Orientation == Orientation.Normal &&
                new ElementBoundsCalculator().GetBounds(text) == secondStart &&
                designer.CanUndo == couldUndo);

            var lineDocument = new LabelDocument { WidthMm = 100, HeightMm = 80, Dpmm = 8,
                CheckQuietZones = false };
            designer.LoadDocument(lineDocument, path: null);
            canvas.ResetView();
            canvas.SetZoom(0.5);
            Pump(300);
            designer.AddLineCommand.Execute(null);
            window.MouseDown(At(200, 240), MouseButton.Left);
            window.MouseUp(At(200, 240), MouseButton.Left);
            Pump(700);
            var line = designer.Document.Elements.OfType<LineElement>().Single();
            DotRect lineStart = new ElementBoundsCalculator().GetBounds(line);
            Point lineTop = At(lineStart.X + lineStart.Width / 2.0, lineStart.Y);
            Point lineCenter = At(lineStart.X + lineStart.Width / 2.0,
                lineStart.Y + lineStart.Height / 2.0);
            Point lineHandle = new(lineTop.X, lineTop.Y - 26);
            Point lineQuarter = new(lineCenter.X + lineCenter.Y - lineHandle.Y,
                lineCenter.Y);
            window.MouseDown(lineHandle, MouseButton.Left);
            window.MouseMove(lineQuarter, RawInputModifiers.LeftMouseButton);
            window.MouseUp(lineQuarter, MouseButton.Left);
            Pump(700);
            DotRect lineBeforeDeselect = new ElementBoundsCalculator().GetBounds(line);
            string documentBeforeDeselect = designer.SerializeDocument();
            string zplBeforeDeselect = designer.GeneratedZpl;
            bool couldUndoBeforeDeselect = designer.CanUndo;
            window.MouseDown(At(500, 500), MouseButton.Left);
            window.MouseUp(At(500, 500), MouseButton.Left);
            Pump(700);
            check("Line rotation stays put after clicking empty canvas",
                line.IsVertical && designer.Selection.Primary is null &&
                new ElementBoundsCalculator().GetBounds(line) == lineBeforeDeselect &&
                designer.SerializeDocument() == documentBeforeDeselect &&
                designer.GeneratedZpl == zplBeforeDeselect &&
                designer.CanUndo == couldUndoBeforeDeselect);

            var textDocument = new LabelDocument { WidthMm = 100, HeightMm = 80, Dpmm = 8,
                CheckQuietZones = false };
            var styledText = new TextElement { X = 200, Y = 240, Text = "Stable",
                Orientation = Orientation.Rotated90, Anchor = FieldAnchor.Baseline,
                Font = 'A', FontHeightDots = 36 };
            textDocument.Elements.Add(styledText);
            designer.LoadDocument(textDocument, path: null);
            designer.Selection.Set(styledText);
            canvas.ResetView();
            canvas.SetZoom(0.5);
            Pump(700);
            string styledBeforeDeselect = designer.SerializeDocument();
            window.MouseDown(At(500, 500), MouseButton.Left);
            window.MouseUp(At(500, 500), MouseButton.Left);
            Pump(700);
            check("Deselecting text preserves rotation anchor and font",
                styledText.Orientation == Orientation.Rotated90 &&
                styledText.Anchor == FieldAnchor.Baseline && styledText.Font == 'A' &&
                designer.SerializeDocument() == styledBeforeDeselect);

            line = LoadLine();
            DotRect commandStart = new ElementBoundsCalculator().GetBounds(line);
            designer.Rotate90Command.Execute(null);
            Pump(300);
            check("Rotate 90 keeps a line's drawn center",
                line.IsVertical && SameCenter(commandStart,
                    new ElementBoundsCalculator().GetBounds(line)));

            line = LoadLine();
            DotRect panelStart = new ElementBoundsCalculator().GetBounds(line);
            if (designer.SelectionProperties is LinePropertiesViewModel linePanel)
                linePanel.SelectedOrientation = linePanel.Orientations[1];
            Pump(300);
            check("Panel rotation keeps a line's drawn center",
                line.IsVertical && SameCenter(panelStart,
                    new ElementBoundsCalculator().GetBounds(line)));

            line = LoadLine(length: 241);
            DotRect roundTripStart = new ElementBoundsCalculator().GetBounds(line);
            designer.Rotate90Command.Execute(null);
            designer.Rotate90Command.Execute(null);
            Pump(300);
            check("Two Rotate 90 commands return a line to its starting bounds",
                !line.IsVertical &&
                new ElementBoundsCalculator().GetBounds(line) == roundTripStart);

            line = LoadLine(length: 241);
            roundTripStart = new ElementBoundsCalculator().GetBounds(line);
            if (designer.SelectionProperties is LinePropertiesViewModel roundTripPanel)
            {
                roundTripPanel.SelectedOrientation = roundTripPanel.Orientations[1];
                roundTripPanel.SelectedOrientation = roundTripPanel.Orientations[0];
            }
            Pump(300);
            check("Two panel turns return a line to its starting bounds",
                !line.IsVertical &&
                new ElementBoundsCalculator().GetBounds(line) == roundTripStart);

            var baselineDocument = new LabelDocument { WidthMm = 100, HeightMm = 80,
                Dpmm = 8, CheckQuietZones = false };
            var baselineText = new TextElement { X = 200, Y = 260, Text = "Anchor",
                FontHeightDots = 40, Anchor = FieldAnchor.Baseline };
            baselineDocument.Elements.Add(baselineText);
            designer.LoadDocument(baselineDocument, path: null);
            designer.Selection.Set(baselineText);
            canvas.ResetView();
            canvas.SetZoom(0.5);
            Pump(300);
            DotRect baselineStart = new ElementBoundsCalculator().GetBounds(baselineText);
            if (designer.SelectionProperties is TextPropertiesViewModel textPanel)
                textPanel.SelectedOrientation = textPanel.Orientations[1];
            Pump(300);
            check("Panel rotation keeps a baseline text field centered",
                baselineText.Orientation == Orientation.Rotated90 &&
                SameCenter(baselineStart, new ElementBoundsCalculator().GetBounds(baselineText)));

            DotRect anchorStart = new ElementBoundsCalculator().GetBounds(baselineText);
            int anchoredX = baselineText.X;
            int anchoredY = baselineText.Y;
            if (designer.SelectionProperties is TextPropertiesViewModel anchorPanel)
                anchorPanel.SelectedAnchor = anchorPanel.Anchors[0];
            Pump(300);
            check("Switching text anchor keeps its drawn bounds",
                baselineText.Anchor == FieldAnchor.TopLeft &&
                new ElementBoundsCalculator().GetBounds(baselineText) == anchorStart);

            if (designer.SelectionProperties is TextPropertiesViewModel returnPanel)
                returnPanel.SelectedAnchor = returnPanel.Anchors[1];
            Pump(300);
            check("Switching text anchor back restores its position",
                baselineText.Anchor == FieldAnchor.Baseline &&
                baselineText.X == anchoredX && baselineText.Y == anchoredY &&
                new ElementBoundsCalculator().GetBounds(baselineText) == anchorStart);
        }
        finally
        {
            designer.SnapToGrid = snapping.Item1;
            designer.SnapToGuides = snapping.Item2;
            designer.SnapToObjects = snapping.Item3;
            designer.NewDocumentCommand.Execute(null);
            canvas.ResetView();
            Pump(150);
        }

        BoxElement LoadBox()
        {
            var document = new LabelDocument { WidthMm = 100, HeightMm = 80, Dpmm = 8,
                CheckQuietZones = false };
            var box = new BoxElement { X = 200, Y = 160, WidthDots = 80, HeightDots = 60 };
            document.Elements.Add(box);
            designer.LoadDocument(document, path: null);
            designer.Selection.Set(box);
            canvas.ResetView();
            canvas.SetZoom(0.5);
            Pump(300);
            return box;
        }

        Point At(double x, double y) =>
            canvas.TranslatePoint(canvas.DotsToView(x, y), window)!.Value;

        LineElement LoadLine(int length = 240)
        {
            var document = new LabelDocument { WidthMm = 100, HeightMm = 80, Dpmm = 8,
                CheckQuietZones = false };
            var line = new LineElement { X = 200, Y = 160, LengthDots = length,
                ThicknessDots = 4 };
            document.Elements.Add(line);
            designer.LoadDocument(document, path: null);
            designer.Selection.Set(line);
            canvas.ResetView();
            canvas.SetZoom(0.5);
            Pump(300);
            return line;
        }
    }

    private static bool SameCenter(DotRect a, DotRect b) =>
        Math.Abs(a.X + a.Width / 2.0 - b.X - b.Width / 2.0) <= 0.5 &&
        Math.Abs(a.Y + a.Height / 2.0 - b.Y - b.Height / 2.0) <= 0.5;

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
