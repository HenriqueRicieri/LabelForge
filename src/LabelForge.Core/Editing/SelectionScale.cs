using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

public readonly record struct ScaleFrame(double X, double Y, double Width, double Height,
    int? SnapX = null, int? SnapY = null);

public sealed class SelectionScale
{
    private readonly record struct Entry(Element Live, Element Original, DotRect Bounds);
    private readonly Entry[] _entries;
    private readonly ElementBoundsCalculator _bounds = new();

    private SelectionScale(IReadOnlyList<Element> elements)
    {
        Elements = elements.ToArray();
        Snapshot = ElementSnapshot.Capture(Elements);
        var originals = LabelDocumentJson.DeserializeElements(Snapshot);
        _entries = Elements.Select((e, i) => new Entry(e, originals[i], _bounds.GetBounds(e))).ToArray();
        StartBounds = GetBounds(Elements);
    }

    public IReadOnlyList<Element> Elements { get; }
    public string Snapshot { get; }
    public DotRect StartBounds { get; }
    public int Constrained { get; private set; }
    public bool HasChanged => ElementSnapshot.Capture(Elements) != Snapshot;

    public static bool CanStart(LabelDocument document, IReadOnlyList<Element> elements) =>
        elements.Count > 1 && elements.All(e => document.Elements.Contains(e) && !Groups.IsHeld(document, e));

    public static SelectionScale? Start(LabelDocument document, IReadOnlyList<Element> elements) =>
        CanStart(document, elements) ? new SelectionScale(elements) : null;

    public static DotRect GetBounds(IReadOnlyList<Element> elements)
    {
        if (elements.Count == 0) return default;
        var calculator = new ElementBoundsCalculator();
        DotRect first = calculator.GetBounds(elements[0]);
        int left = first.X, top = first.Y, right = first.X + first.Width, bottom = first.Y + first.Height;
        foreach (Element element in elements.Skip(1))
        {
            DotRect bounds = calculator.GetBounds(element);
            left = Math.Min(left, bounds.X);
            top = Math.Min(top, bounds.Y);
            right = Math.Max(right, bounds.X + bounds.Width);
            bottom = Math.Max(bottom, bounds.Y + bounds.Height);
        }
        return new DotRect(left, top, right - left, bottom - top);
    }

    // Directions are -1 for the near edge, +1 for the far edge, and 0 for an unchanged axis.
    public void Apply(ScaleFrame frame, int horizontal, int vertical, bool aboutCenter)
    {
        double sx = frame.Width / Math.Max(StartBounds.Width, 1);
        double sy = frame.Height / Math.Max(StartBounds.Height, 1);
        double ax = Anchor(horizontal, aboutCenter), ay = Anchor(vertical, aboutCenter);
        Constrained = 0;
        foreach (var entry in _entries)
        {
            Element element = entry.Live;
            DotRect before = entry.Bounds;
            ElementSnapshot.CopyProperties(entry.Original, element);
            int width = Math.Max(Round(before.Width * sx), 1);
            int height = Math.Max(Round(before.Height * sy), 1);
            if (width != before.Width || height != before.Height)
            {
                bool turned = FieldRotation.Applies(element) &&
                    element.Orientation is Orientation.Rotated90 or Orientation.Rotated270;
                ElementResizer.Resize(element, turned ? height : width, turned ? width : height);
            }

            DotRect after = _bounds.GetBounds(element);
            double x = frame.X + (before.X + ax * before.Width - StartBounds.X) * sx - ax * after.Width;
            double y = frame.Y + (before.Y + ay * before.Height - StartBounds.Y) * sy - ay * after.Height;
            element.X += Round(x) - after.X;
            element.Y += Round(y) - after.Y;
            if (after.Width != width || after.Height != height) Constrained++;
        }
    }

    public ScaleFrame Snap(ScaleFrame frame, int horizontal, int vertical, bool aboutCenter,
        bool proportional, IReadOnlyList<int> targetsX, IReadOnlyList<int> targetsY, int threshold)
    {
        int edgeX = Round(horizontal < 0 ? frame.X : frame.X + frame.Width);
        int edgeY = Round(vertical < 0 ? frame.Y : frame.Y + frame.Height);
        var (dx, snapX) = GuideSnapper.Snap(edgeX, edgeX, horizontal == 0 ? [] : targetsX, threshold);
        var (dy, snapY) = GuideSnapper.Snap(edgeY, edgeY, vertical == 0 ? [] : targetsY, threshold);
        double ax = Anchor(horizontal, aboutCenter), ay = Anchor(vertical, aboutCenter);
        double anchorX = StartBounds.X + ax * StartBounds.Width;
        double anchorY = StartBounds.Y + ay * StartBounds.Height;
        double width = frame.Width, height = frame.Height;
        double span = aboutCenter ? 0.5 : 1;
        if (proportional && horizontal != 0 && vertical != 0)
        {
            if (snapX is not null && (snapY is null || Math.Abs(dx) <= Math.Abs(dy)))
            {
                width = Math.Max(Math.Abs(snapX.Value - anchorX) / span, 4);
                height = Math.Max(width * StartBounds.Height / Math.Max(StartBounds.Width, 1), 4);
                snapY = null;
            }
            else if (snapY is not null)
            {
                height = Math.Max(Math.Abs(snapY.Value - anchorY) / span, 4);
                width = Math.Max(height * StartBounds.Width / Math.Max(StartBounds.Height, 1), 4);
                snapX = null;
            }
        }
        else
        {
            if (snapX is not null) width = Math.Max(Math.Abs(snapX.Value - anchorX) / span, 4);
            if (snapY is not null) height = Math.Max(Math.Abs(snapY.Value - anchorY) / span, 4);
        }
        return new ScaleFrame(anchorX - ax * width, anchorY - ay * height, width, height, snapX, snapY);
    }

    private static double Anchor(int direction, bool aboutCenter) =>
        aboutCenter || direction == 0 ? 0.5 : direction < 0 ? 1 : 0;

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
