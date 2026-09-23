using System.Globalization;
using System.Text.RegularExpressions;

namespace LabelForge.Core.Zpl;

/// <summary>The printer dimensions in force for one label block, in dots.</summary>
public readonly record struct ZplLabelSize(int? PrintWidthDots, int? LabelLengthDots);

/// <summary>
/// Reads the dimensions in force at each ^XA block. BinaryKits ignores ^PW and ^LL,
/// so the viewer supplies the selected label's size to the renderer itself.
/// </summary>
public static partial class ZplSizeScanner
{
    [GeneratedRegex(
        @"\^(?<start>XA)|\^(?<end>XZ)|\^PW(?<width>\d+)|\^LL(?<height>\d+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SizeCommands();

    public static ZplLabelSize ForLabel(string zpl, int labelIndex)
    {
        if (string.IsNullOrEmpty(zpl))
        {
            return default;
        }

        int? width = null;
        int? height = null;
        bool inLabel = false;
        var labels = new List<ZplLabelSize>();
        foreach (Match match in SizeCommands().Matches(zpl))
        {
            if (match.Groups["start"].Success)
            {
                if (inLabel)
                {
                    labels.Add(new ZplLabelSize(width, height));
                }

                inLabel = true;
                continue;
            }

            if (match.Groups["end"].Success)
            {
                if (inLabel)
                {
                    labels.Add(new ZplLabelSize(width, height));
                    inLabel = false;
                }

                continue;
            }

            if (match.Groups["width"].Success &&
                int.TryParse(match.Groups["width"].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int printWidth) && printWidth > 0)
            {
                width = printWidth;
            }
            else if (match.Groups["height"].Success &&
                     int.TryParse(match.Groups["height"].Value, NumberStyles.None,
                         CultureInfo.InvariantCulture, out int labelLength) && labelLength > 0)
            {
                height = labelLength;
            }
        }

        if (inLabel)
        {
            labels.Add(new ZplLabelSize(width, height));
        }

        return labels.Count == 0
            ? new ZplLabelSize(width, height)
            : labels[Math.Clamp(labelIndex, 0, labels.Count - 1)];
    }

    public static int? PrintWidthDots(string zpl) => ForLabel(zpl, 0).PrintWidthDots;

    public static int? LabelLengthDots(string zpl) => ForLabel(zpl, 0).LabelLengthDots;
}
