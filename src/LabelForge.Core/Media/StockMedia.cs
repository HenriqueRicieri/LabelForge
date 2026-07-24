namespace LabelForge.Core.Media;

/// <summary>
/// One label stock specification: the part number printed on the roll's box, the
/// material line, and the die-cut dimensions. Used to start a label at the exact
/// physical size of the stock the user actually loads.
///
/// Two sources produce these. The embedded catalog carries official Zebra media; the
/// user's own presets carry third-party stock, which is what most print shops actually
/// run. <see cref="IsUserDefined"/> is the only difference, and it exists so the picker
/// can label them honestly and so only the user's own can be deleted.
/// </summary>
public sealed record StockMedia(
    string PartNumber,
    string Material,
    double WidthMm,
    double HeightMm,
    string SizeText,
    double RadiusMm = 0,
    bool Continuous = false,
    bool IsUserDefined = false)
{
    /// <summary>Builds one of the user's own media definitions, formatting the size
    /// text the way the catalog does so both kinds read alike in the picker.</summary>
    public static StockMedia UserDefined(
        string name,
        double widthMm,
        double heightMm,
        string material = "",
        double radiusMm = 0,
        bool continuous = false) =>
        new(
            (name ?? string.Empty).Trim(),
            (material ?? string.Empty).Trim(),
            widthMm,
            heightMm,
            FormatSize(widthMm, heightMm, continuous),
            radiusMm,
            continuous,
            IsUserDefined: true);

    /// <summary>Size text in the catalog's own shape ("102mm x 152mm"). A continuous
    /// roll states only its width, because its length is whatever the content needs.</summary>
    public static string FormatSize(double widthMm, double heightMm, bool continuous) =>
        continuous
            ? FormattableString.Invariant($"{widthMm:0.##}mm continuous")
            : FormattableString.Invariant($"{widthMm:0.##}mm x {heightMm:0.##}mm");

    /// <summary>Display form used by pickers and search results.</summary>
    public override string ToString()
    {
        string text = Material.Length > 0
            ? $"{PartNumber} - {Material} ({SizeText})"
            : $"{PartNumber} ({SizeText})";
        return IsUserDefined ? $"{text} - my media" : text;
    }
}
