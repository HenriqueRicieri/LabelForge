using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LabelForge.Core.Imaging;
using LabelForge.Core.Zpl;

namespace LabelForge.Core.Rendering;

/// <summary>Expands magnified ^XG recalls in the copy sent to BinaryKits.
/// The library draws every recalled graphic at its stored size.</summary>
internal static partial class ZplGraphicMagnification
{
    private const long MaxExpandedPixels = 64_000_000;

    [GeneratedRegex(
        @"~DG(?<download>[^,\^~\r\n]{1,128}),\s*(?<total>\d+)\s*,\s*(?<row>\d+)\s*,"
        + @"|\^XG(?<name>[^,\^~\r\n]{1,128})(?:,(?<mx>\d+))?(?:,(?<my>\d+))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex Commands();

    public static string Expand(string zpl, List<string> warnings)
    {
        if (!zpl.Contains("^XG", StringComparison.OrdinalIgnoreCase))
        {
            return zpl;
        }

        MatchCollection commands = Commands().Matches(zpl);
        bool hasMagnification = false;
        foreach (Match command in commands)
        {
            if (command.Groups["name"].Success &&
                (Factor(command.Groups["mx"].Value) > 1 ||
                 Factor(command.Groups["my"].Value) > 1))
            {
                hasMagnification = true;
                break;
            }
        }

        if (!hasMagnification)
        {
            return zpl;
        }

        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match command in commands)
        {
            string rawName = command.Groups["download"].Success
                ? command.Groups["download"].Value
                : command.Groups["name"].Value;
            string name = ZplGraphicScanner.NormalizeName(rawName);
            if (name.Length > 0)
            {
                usedNames.Add(name);
            }
        }

        var definitions = new Dictionary<string, (ZplGraphicDefinition Graphic, int Version)>(
            StringComparer.Ordinal);
        var variants = new Dictionary<(int Version, int X, int Y), string?>();
        var downloads = new StringBuilder();
        int nextName = 0;
        int version = 0;

        string rewritten = Commands().Replace(zpl, command =>
        {
            if (command.Groups["download"].Success)
            {
                string name = ZplGraphicScanner.NormalizeName(command.Groups["download"].Value);
                if (name.Length > 0 &&
                    int.TryParse(command.Groups["total"].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out int total) &&
                    int.TryParse(command.Groups["row"].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out int row) &&
                    row > 0 && total >= row)
                {
                    int start = command.Index + command.Length;
                    int payloadLength = zpl.AsSpan(start).IndexOfAny('^', '~');
                    if (payloadLength < 0)
                    {
                        payloadLength = zpl.Length - start;
                    }

                    definitions[name] = (
                        new ZplGraphicDefinition(name, total, row,
                            zpl.Substring(start, payloadLength)),
                        ++version);
                }

                return command.Value;
            }

            int mx = Factor(command.Groups["mx"].Value);
            int my = Factor(command.Groups["my"].Value);
            if (mx == 1 && my == 1)
            {
                return command.Value;
            }

            string recalledName = ZplGraphicScanner.NormalizeName(command.Groups["name"].Value);
            if (!definitions.TryGetValue(recalledName, out var current))
            {
                return command.Value;
            }

            var key = (current.Version, mx, my);
            if (!variants.TryGetValue(key, out string? alias))
            {
                ZplGraphicDefinition definition = current.Graphic;
                long width = (long)definition.BytesPerRow * 8 * mx;
                long height = (long)(definition.TotalBytes / definition.BytesPerRow) * my;
                if (width > MaxExpandedPixels || height > MaxExpandedPixels / width)
                {
                    warnings.Add(
                        $"Graphic {recalledName} at {mx}x{my} is too large to preview at full size.");
                    variants.Add(key, null);
                    return command.Value;
                }

                GraphicBitmap? source = GraphicField.Decode(
                    definition.Data, definition.BytesPerRow, definition.TotalBytes);
                if (source is null)
                {
                    warnings.Add($"Graphic {recalledName} could not be expanded for preview.");
                    variants.Add(key, null);
                    return command.Value;
                }

                GraphicBitmap expanded = GraphicField.Magnify(source, mx, my);
                do
                {
                    alias = $"LFV{nextName++:X5}";
                }
                while (!usedNames.Add(alias));

                int rowBytes = expanded.Width / 8;
                int totalBytes = checked(rowBytes * expanded.Height);
                downloads.Append("~DGR:").Append(alias).Append(".GRF,")
                    .Append(totalBytes).Append(',').Append(rowBytes).Append(',')
                    .Append(GraphicField.Encode(expanded.Black, expanded.Width, expanded.Height))
                    .Append('\n');
                variants.Add(key, alias);
            }

            return alias is null ? command.Value : $"^XGR:{alias}.GRF,1,1";
        });

        return downloads.Length == 0 ? zpl : downloads.Append(rewritten).ToString();
    }

    private static int Factor(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? Math.Clamp(parsed, 1, 10)
            : 1;
}
