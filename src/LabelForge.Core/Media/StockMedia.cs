namespace LabelForge.Core.Media;

/// <summary>
/// One official Zebra media (label stock) specification: the part number printed on
/// the roll's box, the material line, and the die-cut dimensions. Used to start a
/// label at the exact physical size of the stock the user actually loads.
/// </summary>
public sealed record StockMedia(
    string PartNumber,
    string Material,
    double WidthMm,
    double HeightMm,
    string SizeText,
    double RadiusMm = 0,
    bool Continuous = false)
{
    /// <summary>Display form used by pickers and search results.</summary>
    public override string ToString() => $"{PartNumber} - {Material} ({SizeText})";
}
