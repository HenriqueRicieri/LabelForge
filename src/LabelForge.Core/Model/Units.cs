namespace LabelForge.Core.Model;

/// <summary>
/// The single place millimeters are converted to printer dots. Density is dots per
/// millimeter (203 dpi = 8, 300 = 12, 600 = 24). Keeping one helper avoids the
/// off-by-one-dot drift that comes from rounding in several places.
/// </summary>
public static class Units
{
    public static int MmToDots(double mm, int dpmm) =>
        (int)Math.Round(mm * dpmm, MidpointRounding.AwayFromZero);

    public static double DotsToMm(int dots, int dpmm) =>
        dpmm == 0 ? 0 : (double)dots / dpmm;
}
