namespace LabelForge.Core.Model;

/// <summary>
/// The label being designed: physical size in millimeters, a target print density,
/// and the ordered elements placed on it. This is the single source of truth; the
/// canvas edits it and the ZPL generator reads it.
/// </summary>
public sealed class LabelDocument
{
    /// <summary>Physical label width in millimeters.</summary>
    public double WidthMm { get; set; } = 100;

    /// <summary>Physical label height in millimeters.</summary>
    public double HeightMm { get; set; } = 150;

    /// <summary>Print density in dots per millimeter (8 = 203 dpi, 12 = 300, 24 = 600).</summary>
    public int Dpmm { get; set; } = 8;

    public IList<Element> Elements { get; } = new List<Element>();

    public int WidthDots => Units.MmToDots(WidthMm, Dpmm);

    public int HeightDots => Units.MmToDots(HeightMm, Dpmm);
}
