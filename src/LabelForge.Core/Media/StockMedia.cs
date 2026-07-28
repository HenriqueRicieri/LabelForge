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
/// <param name="Across">Labels side by side across the web; 1 is an ordinary roll. 39 of
/// the catalog's entries carry more, up to 8.</param>
/// <param name="GapMm">Liner between one column and the next, which with the width gives
/// the pitch. Meaningless below 2 across.</param>
public sealed record StockMedia(
    string PartNumber,
    string Material,
    double WidthMm,
    double HeightMm,
    string SizeText,
    double RadiusMm = 0,
    bool Continuous = false,
    bool IsUserDefined = false,
    int Across = 1,
    double GapMm = 0)
{
    /// <summary>Builds one of the user's own media definitions, formatting the size
    /// text the way the catalog does so both kinds read alike in the picker.</summary>
    public static StockMedia UserDefined(
        string name,
        double widthMm,
        double heightMm,
        string material = "",
        double radiusMm = 0,
        bool continuous = false,
        int across = 1,
        double gapMm = 0) =>
        new(
            (name ?? string.Empty).Trim(),
            (material ?? string.Empty).Trim(),
            widthMm,
            heightMm,
            FormatSize(widthMm, heightMm, continuous),
            radiusMm,
            continuous,
            IsUserDefined: true,
            Math.Max(across, 1),
            gapMm);

    /// <summary>Size text in the catalog's own shape ("102mm x 152mm"). A continuous
    /// roll states only its width, because its length is whatever the content needs.</summary>
    public static string FormatSize(double widthMm, double heightMm, bool continuous) =>
        continuous
            ? FormattableString.Invariant($"{widthMm:0.##}mm continuous")
            : FormattableString.Invariant($"{widthMm:0.##}mm x {heightMm:0.##}mm");

    /// <summary>Display form used by pickers and search results. The column count is named
    /// because it is not in the size text and it changes what a run produces: two entries
    /// with the same die cut print very differently at 4 across.</summary>
    public override string ToString()
    {
        string size = Across > 1 ? $"{SizeText}, {Across} across" : SizeText;
        string text = Material.Length > 0
            ? $"{PartNumber} - {Material} ({size})"
            : $"{PartNumber} ({size})";
        return IsUserDefined ? $"{text} - my media" : text;
    }
}
