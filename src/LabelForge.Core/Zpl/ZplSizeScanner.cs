using System.Text.RegularExpressions;

namespace LabelForge.Core.Zpl;

/// <summary>
/// Reads the label dimensions declared in a ZPL stream. BinaryKits ignores ^PW and
/// ^LL, so the viewer scans them itself to size the preview to match pasted ZPL.
/// </summary>
public static partial class ZplSizeScanner
{
    [GeneratedRegex(@"\^PW(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PrintWidthRegex();

    [GeneratedRegex(@"\^LL(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LabelLengthRegex();

    /// <summary>Print width in dots from the first ^PW command, if present.</summary>
    public static int? PrintWidthDots(string zpl) => Match(PrintWidthRegex(), zpl);

    /// <summary>Label length in dots from the first ^LL command, if present.</summary>
    public static int? LabelLengthDots(string zpl) => Match(LabelLengthRegex(), zpl);

    private static int? Match(Regex regex, string zpl)
    {
        if (string.IsNullOrEmpty(zpl))
        {
            return null;
        }

        Match match = regex.Match(zpl);
        return match.Success && int.TryParse(match.Groups[1].Value, out int dots) ? dots : null;
    }
}
