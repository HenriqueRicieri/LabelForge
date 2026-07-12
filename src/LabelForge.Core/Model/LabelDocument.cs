using System.Text.Json.Serialization;

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

    public IList<Element> Elements { get; init; } = new List<Element>();

    /// <summary>Vertical alignment guides: X positions in dots. Design aids only,
    /// never printed; they ride the undo/save pipeline like any document change.</summary>
    public IList<int> VerticalGuides { get; init; } = new List<int>();

    /// <summary>Horizontal alignment guides: Y positions in dots.</summary>
    public IList<int> HorizontalGuides { get; init; } = new List<int>();

    [JsonIgnore]
    public int WidthDots => Units.MmToDots(WidthMm, Dpmm);

    [JsonIgnore]
    public int HeightDots => Units.MmToDots(HeightMm, Dpmm);
}
