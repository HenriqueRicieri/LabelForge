using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

public readonly record struct SelectionMarquee(double StartX, double StartY, double EndX, double EndY)
{
    public bool Crossing => EndX < StartX;
    public double Left => Math.Min(StartX, EndX);
    public double Top => Math.Min(StartY, EndY);
    public double Right => Math.Max(StartX, EndX);
    public double Bottom => Math.Max(StartY, EndY);

    public IReadOnlyList<Element> Select(LabelDocument document)
    {
        if (Right <= Left || Bottom <= Top) return [];

        var marquee = this;
        var calculator = new ElementBoundsCalculator();
        var visible = document.Elements.Where(e => e.IsVisible)
            .Select(e => (Element: e, Bounds: calculator.GetBounds(e))).ToArray();
        var enclosedGroups = visible.Where(e => e.Element.GroupId is not null)
            .GroupBy(e => e.Element.GroupId!.Value)
            .Where(group => group.All(e => marquee.Encloses(e.Bounds)))
            .Select(group => group.Key).ToHashSet();

        var hits = visible.Where(e => marquee.Crossing
            ? marquee.Intersects(e.Bounds)
            : marquee.Encloses(e.Bounds) && (e.Element.GroupId is not { } id || enclosedGroups.Contains(id)));
        return Groups.Expand(document, hits.Select(e => e.Element));
    }

    private bool Encloses(DotRect bounds) =>
        bounds.X >= Left && bounds.Y >= Top &&
        (double)bounds.X + bounds.Width <= Right && (double)bounds.Y + bounds.Height <= Bottom;

    private bool Intersects(DotRect bounds) =>
        bounds.X < Right && (double)bounds.X + bounds.Width > Left &&
        bounds.Y < Bottom && (double)bounds.Y + bounds.Height > Top;
}
