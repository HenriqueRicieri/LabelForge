namespace LabelForge.Core.Model;

/// <param name="Code">The symbol whose quiet zone is not clear.</param>
/// <param name="Intruder">The element sitting in it, or null when the problem is that
/// the quiet zone runs off the label instead.</param>
/// <param name="Zone">The blank the symbol needed, for a caller that wants to draw it.</param>
public sealed record QuietZoneFinding(Element Code, Element? Intruder, DotRect Zone);

/// <summary>
/// Finds symbols whose quiet zone is not clear. A barcode that cannot be read on the
/// first pass is a label that failed, and the two ways that happens on a design are a
/// neighbour crowding the symbol and the symbol sitting flush against the edge of the
/// stock, so both are reported.
///
/// Design-time only: a quiet zone is blank, so nothing here changes a single byte of
/// the generated ZPL. It is measured from the same footprints the canvas draws its
/// outlines from, which are close to the ink but not the ink itself, so this is a
/// warning worth reading rather than a measurement to certify a label by. Square-cornered
/// hollow boxes use their border strips instead of treating the empty interior as ink.
/// </summary>
public static class QuietZoneChecker
{
    public static IReadOnlyList<QuietZoneFinding> Check(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.CheckQuietZones)
        {
            return [];
        }

        var calculator = new ElementBoundsCalculator();

        // Only what prints can crowd anything, and only what prints needs to be read.
        Element[] printing = document.Elements
            .Where(e => e.IsVisible && ElementPlacement.IsPrintable(e, document))
            .ToArray();

        var findings = new List<QuietZoneFinding>();
        foreach (Element code in printing.Where(QuietZone.Applies))
        {
            QuietZoneMargin margin = QuietZone.For(code);
            if (margin.IsEmpty)
            {
                continue;
            }

            DotRect zone = margin.Around(calculator.GetBounds(code));

            // Off the stock is reported once for the symbol, and only sideways when the
            // stock is continuous, where there is no bottom edge to run off.
            bool offLabel = zone.X < 0 || zone.X + zone.Width > document.WidthDots ||
                            (!document.IsContinuous &&
                             (zone.Y < 0 || zone.Y + zone.Height > document.HeightDots));
            if (offLabel)
            {
                findings.Add(new QuietZoneFinding(code, null, zone));
            }

            foreach (Element other in printing)
            {
                if (!ReferenceEquals(other, code) &&
                    CrowdsZone(other, calculator.GetBounds(other), zone))
                {
                    findings.Add(new QuietZoneFinding(code, other, zone));
                }
            }
        }

        return findings;
    }

    private static bool CrowdsZone(Element element, DotRect bounds, DotRect zone)
    {
        if (!zone.Intersects(bounds)) return false;
        if (element is not BoxElement { CornerRoundness: 0, ThicknessDots: > 0 } box)
            return true;

        int thickness = box.ThicknessDots;
        if (2L * thickness >= bounds.Width || 2L * thickness >= bounds.Height)
            return true;

        // A square-cornered frame only touches a zone that reaches past its empty interior.
        return zone.X < (long)bounds.X + thickness ||
               zone.Y < (long)bounds.Y + thickness ||
               (long)zone.X + zone.Width > (long)bounds.X + bounds.Width - thickness ||
               (long)zone.Y + zone.Height > (long)bounds.Y + bounds.Height - thickness;
    }
}
