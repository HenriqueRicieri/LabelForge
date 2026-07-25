namespace LabelForge.Core.Model;

/// <summary>
/// How long a label on continuous stock is. A roll with no gaps or die cuts has no
/// height of its own, so the answer is the content: the last row of ink that will
/// print, plus the blank the next label needs to start after.
///
/// This is the only place that rule lives, because it decides three things at once and
/// they must not diverge: how tall the canvas draws the label, what ^LL tells the
/// printer to advance, and what the PDF page measures.
/// </summary>
public static class ContinuousLength
{
    /// <summary>
    /// Shortest label this will report, in millimeters.
    ///
    /// A label with nothing on it still has to be a surface the first element can be
    /// dropped onto, and content plus a margin is a few millimeters of sliver until
    /// something is placed. This is a floor for the empty case, not a design minimum.
    /// </summary>
    public const double MinimumLengthMm = 10;

    /// <summary>The label length: the content, plus the blank the next label starts
    /// after, never shorter than <see cref="MinimumLengthMm"/>.</summary>
    public static double Mm(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Math.Max(
            ContentMm(document) + Math.Max(document.ContinuousMarginMm, 0), MinimumLengthMm);
    }

    /// <summary>How far down the last row of printing ink reaches, in millimeters, with
    /// no margin and no floor. Only what will actually print counts: hidden elements,
    /// elements set not to print and anything parked off the left, top or right of the
    /// label are all excluded, so the roll is not advanced for something that leaves no
    /// mark on it.</summary>
    public static double ContentMm(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var calculator = new ElementBoundsCalculator();
        int bottomDots = 0;
        foreach (Element element in document.Elements)
        {
            if (!element.IsVisible || !ElementPlacement.IsPrintable(element, document))
            {
                continue;
            }

            DotRect bounds = calculator.GetBounds(element);
            bottomDots = Math.Max(bottomDots, bounds.Y + bounds.Height);
        }

        return Units.DotsToMm(bottomDots, Math.Max(document.Dpmm, 1));
    }
}
