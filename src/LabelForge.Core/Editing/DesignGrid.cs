using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

/// <summary>
/// Where a design grid's lines fall, in dots.
///
/// One place, because the grid is drawn and snapped to and those must not disagree: a
/// line the eye can see but the pointer slips past, or the reverse, is worse than no grid.
///
/// Lines are rounded to whole dots through the same helper every other millimetre goes
/// through, so a pitch that does not divide evenly into dots still lands somewhere a ^FO
/// can express. The lines are therefore not perfectly even at every density, and that is
/// the honest outcome: the printer has no half dots either.
/// </summary>
public static class DesignGrid
{
    /// <summary>Finest pitch offered. Below this the lines are closer together than the
    /// snap threshold, so every position would be on the grid and the grid would stop
    /// meaning anything.</summary>
    public const double MinimumPitchMm = 0.5;

    /// <summary>A guard on how many lines one axis may produce, so a tiny pitch on a long
    /// roll cannot turn a render into a stall.</summary>
    public const int MaxLines = 2000;

    /// <summary>True when the document asks for a grid at all. Zero is off, which is the
    /// same "zero means leave it alone" the print settings use.</summary>
    public static bool IsEnabled(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GridPitchMm >= MinimumPitchMm;
    }

    /// <summary>Line positions in dots from 0 up to and including <paramref name="extentDots"/>
    /// when it lands on one. Empty when the grid is off.</summary>
    public static IEnumerable<int> Lines(LabelDocument document, int extentDots)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsEnabled(document) || extentDots <= 0)
        {
            yield break;
        }

        double pitch = document.GridPitchMm;
        int dpmm = Math.Max(document.Dpmm, 1);

        for (int i = 0; i <= MaxLines; i++)
        {
            int dots = Units.MmToDots(i * pitch, dpmm);
            if (dots > extentDots)
            {
                yield break;
            }

            yield return dots;
        }
    }
}
