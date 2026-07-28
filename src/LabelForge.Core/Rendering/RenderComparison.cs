using System.Globalization;
using LabelForge.Core.Model;
using SkiaSharp;

namespace LabelForge.Core.Rendering;

/// <summary>How two renderings of the same ZPL differ.</summary>
/// <param name="Comparable">False when one side produced no image, or the two came out
/// different sizes. The ink counts and the boxes still stand; only
/// <paramref name="DisagreeingPixels"/> needs the two to line up.</param>
/// <param name="LeftInk">Ink pixels in the first image.</param>
/// <param name="RightInk">Ink pixels in the second.</param>
/// <param name="LeftBounds">The box the first image's ink occupies.</param>
/// <param name="RightBounds">The box the second image's ink occupies.</param>
/// <param name="DisagreeingPixels">Pixels one drew and the other did not, in either
/// direction. Zero only when the two are identical.</param>
/// <param name="Summary">The one line a person reads.</param>
public readonly record struct RenderDifference(
    bool Comparable,
    int LeftInk,
    int RightInk,
    DotRect LeftBounds,
    DotRect RightBounds,
    int DisagreeingPixels,
    string Summary)
{
    /// <summary>
    /// The largest disagreement between the two ink boxes, in dots, on any edge.
    ///
    /// This is the number that matters and the ink count is not, which was learned by
    /// using this against the service rather than reasoned out. Measured on text, the two
    /// engines agree on the box to within 2 dots and disagree about the ink inside it by
    /// 60 per cent: same size, same place, lighter strokes. A label is right or wrong
    /// about where things are and how big they are; how heavy the preview draws them is
    /// cosmetic.
    /// </summary>
    public int EdgeDifferenceDots =>
        Math.Max(
            Math.Max(
                Math.Abs(LeftBounds.X - RightBounds.X),
                Math.Abs(LeftBounds.Y - RightBounds.Y)),
            Math.Max(
                Math.Abs((LeftBounds.X + LeftBounds.Width) - (RightBounds.X + RightBounds.Width)),
                Math.Abs((LeftBounds.Y + LeftBounds.Height) - (RightBounds.Y + RightBounds.Height))));

    /// <summary>How far apart the two are, as a fraction of the larger ink count. Zero
    /// when neither drew anything, which is agreement rather than a division by zero.</summary>
    public double InkDifference =>
        Math.Max(LeftInk, RightInk) is var most and > 0
            ? Math.Abs(LeftInk - RightInk) / (double)most
            : 0;
}

/// <summary>
/// Compares two rendered labels (backlog E2).
///
/// Deliberately no network and no renderer of its own: it takes two PNGs and answers how
/// they differ, so it is testable without touching Labelary and the comparison means the
/// same thing wherever the second image came from.
///
/// Ink is counted rather than colour compared, because a label is one bit deep and the
/// question is always "did the same dots go down". Two numbers that agree are not proof
/// the pictures match, which is why the per-pixel disagreement is reported too whenever
/// the sizes allow it.
/// </summary>
public static class RenderComparison
{
    /// <summary>A pixel counts as ink below this. Both engines draw black on white and
    /// antialias the edges, so a mid grey has to fall one side or the other; half way is
    /// the only choice that treats the two engines alike.</summary>
    private const byte InkThreshold = 128;

    public static RenderDifference Compare(byte[]? left, byte[]? right)
    {
        using SKBitmap? a = Decode(left);
        using SKBitmap? b = Decode(right);

        if (a is null || b is null)
        {
            string which = (a is null, b is null) switch
            {
                (true, true) => "Neither renderer produced an image",
                (true, false) => "The offline renderer produced no image",
                _ => "Labelary produced no image",
            };

            return new RenderDifference(
                false, Ink(a), Ink(b), Bounds(a), Bounds(b), 0,
                which + ", so there is nothing to compare.");
        }

        int leftInk = Ink(a);
        int rightInk = Ink(b);
        DotRect leftBox = Bounds(a);
        DotRect rightBox = Bounds(b);

        if (a.Width != b.Width || a.Height != b.Height)
        {
            // Not a failure. The two round a label's dot size independently, so a label
            // whose millimetres do not land on whole dots comes back a pixel adrift, and
            // everything except the per-pixel count is still worth having.
            var adrift = new RenderDifference(
                false, leftInk, rightInk, leftBox, rightBox, 0, string.Empty);

            return adrift with
            {
                Summary = string.Format(
                    CultureInfo.CurrentCulture,
                    "Canvas sizes differ ({0}x{1} against {2}x{3}), so the pixels cannot be "
                    + "lined up. Ink boxes are within {4} dot(s); ink {5:P1} apart.",
                    a.Width, a.Height, b.Width, b.Height,
                    adrift.EdgeDifferenceDots,
                    Difference(leftInk, rightInk)),
            };
        }

        int disagreeing = 0;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                if (IsInk(a, x, y) != IsInk(b, x, y))
                {
                    disagreeing++;
                }
            }
        }

        var difference = new RenderDifference(
            true, leftInk, rightInk, leftBox, rightBox, disagreeing, string.Empty);

        return difference with { Summary = Describe(difference, a.Width * a.Height) };
    }

    /// <summary>
    /// The one line someone reads, and it leads with geometry on purpose.
    ///
    /// The ink count alone is misleading, which was learned by running this against the
    /// service rather than worked out beforehand. On text the two engines agree on the box
    /// to within a dot or two and disagree about the ink inside it by 60 per cent: the
    /// preview draws the same letters in the same place with lighter strokes, because it
    /// substitutes a typeface for a font that lives in the printer. A label is right or
    /// wrong about where things are and how big they are, so that goes first and the
    /// weight is named for what it is.
    /// </summary>
    private static string Describe(RenderDifference difference, int pixels)
    {
        if (difference.DisagreeingPixels == 0)
        {
            return $"Identical: {difference.LeftInk:N0} dots, pixel for pixel.";
        }

        string geometry = difference.EdgeDifferenceDots == 0
            ? "Same size and position"
            : string.Format(
                CultureInfo.CurrentCulture,
                "Ink box differs by {0} dot(s)",
                difference.EdgeDifferenceDots);

        return string.Format(
            CultureInfo.CurrentCulture,
            "{0}. Ink {1:P1} apart ({2:N0} dots against {3:N0}), {4:N0} pixels different "
            + "({5:P2} of the label).",
            geometry,
            difference.InkDifference,
            difference.LeftInk,
            difference.RightInk,
            difference.DisagreeingPixels,
            difference.DisagreeingPixels / (double)pixels);
    }

    /// <summary>The box the ink occupies, which is what says whether the two put the same
    /// things in the same places. Empty when nothing was drawn.</summary>
    private static DotRect Bounds(SKBitmap? bitmap)
    {
        if (bitmap is null)
        {
            return default;
        }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (!IsInk(bitmap, x, y))
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0 ? default : new DotRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static double Difference(int left, int right) =>
        Math.Max(left, right) is var most and > 0 ? Math.Abs(left - right) / (double)most : 0;

    private static SKBitmap? Decode(byte[]? png) =>
        png is null || png.Length == 0 ? null : SKBitmap.Decode(png);

    private static int Ink(SKBitmap? bitmap)
    {
        if (bitmap is null)
        {
            return 0;
        }

        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (IsInk(bitmap, x, y))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>A transparent pixel is stock rather than ink, which matters because the
    /// two engines disagree about whether to paint the background at all.</summary>
    private static bool IsInk(SKBitmap bitmap, int x, int y)
    {
        SKColor pixel = bitmap.GetPixel(x, y);
        return pixel.Alpha >= InkThreshold && pixel.Red < InkThreshold;
    }
}
