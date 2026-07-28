using System.Globalization;
using SkiaSharp;

namespace LabelForge.Core.Rendering;

/// <summary>How two renderings of the same ZPL differ.</summary>
/// <param name="Comparable">False when one side produced no image, or the two came out
/// different sizes. The ink counts still stand; only <paramref name="DisagreeingPixels"/>
/// needs the two to line up.</param>
/// <param name="LeftInk">Ink pixels in the first image.</param>
/// <param name="RightInk">Ink pixels in the second.</param>
/// <param name="DisagreeingPixels">Pixels one drew and the other did not, in either
/// direction. Zero only when the two are identical.</param>
/// <param name="Summary">The one line a person reads.</param>
public readonly record struct RenderDifference(
    bool Comparable,
    int LeftInk,
    int RightInk,
    int DisagreeingPixels,
    string Summary)
{
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

            return new RenderDifference(false, Ink(a), Ink(b), 0, which + ", so there is nothing to compare.");
        }

        int leftInk = Ink(a);
        int rightInk = Ink(b);

        if (a.Width != b.Width || a.Height != b.Height)
        {
            // Not a failure. The two round a label's dot size independently, so a label
            // whose millimetres do not land on whole dots comes back a pixel adrift, and
            // the ink counts are still the comparison worth having.
            return new RenderDifference(
                false,
                leftInk,
                rightInk,
                0,
                string.Format(
                    CultureInfo.CurrentCulture,
                    "Sizes differ ({0}x{1} against {2}x{3}), so only the ink is compared: "
                    + "{4:N0} dots against {5:N0}, {6:P1} apart.",
                    a.Width, a.Height, b.Width, b.Height,
                    leftInk, rightInk,
                    Difference(leftInk, rightInk)));
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

        string summary = disagreeing == 0
            ? $"Identical: {leftInk:N0} dots, pixel for pixel."
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} dots against {1:N0}, {2:P1} apart; {3:N0} pixels differ ({4:P2} of the label).",
                leftInk,
                rightInk,
                Difference(leftInk, rightInk),
                disagreeing,
                disagreeing / (double)(a.Width * a.Height));

        return new RenderDifference(true, leftInk, rightInk, disagreeing, summary);
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
