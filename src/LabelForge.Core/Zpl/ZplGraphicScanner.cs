using System.Globalization;
using System.Text.RegularExpressions;

namespace LabelForge.Core.Zpl;

/// <summary>A graphic downloaded to printer memory by ~DG and drawn later by name.</summary>
/// <param name="Name">The storage name, normalized (no device prefix, no .GRF suffix).</param>
/// <param name="TotalBytes">Byte count of the stored bitmap.</param>
/// <param name="BytesPerRow">Row stride in bytes; width is eight times this.</param>
/// <param name="Data">The raw payload, still compressed.</param>
public sealed record ZplGraphicDefinition(string Name, int TotalBytes, int BytesPerRow, string Data);

/// <summary>Somewhere a graphic is drawn on a label: either a ^XG recall of a
/// downloaded graphic, or a ^GF field carrying its own bitmap.</summary>
/// <param name="Name">The recalled name, or null for an inline ^GF field.</param>
/// <param name="X">Field origin in dots, including any ^LH shift in force.</param>
/// <param name="Y">Field origin in dots, including any ^LH shift in force.</param>
/// <param name="MagnificationX">^XG horizontal magnification, 1 for an inline field.</param>
/// <param name="MagnificationY">^XG vertical magnification, 1 for an inline field.</param>
/// <param name="Inline">The bitmap carried by a ^GF field, or null for a recall.</param>
/// <param name="LabelIndex">Which ^XA block the placement sits in, counting from zero.</param>
public sealed record ZplGraphicPlacement(
    string? Name,
    int X,
    int Y,
    int MagnificationX,
    int MagnificationY,
    ZplGraphicDefinition? Inline,
    int LabelIndex);

/// <summary>What a ZPL stream says about graphics.</summary>
/// <param name="Definitions">Every distinct ~DG download, in the order first seen.</param>
/// <param name="Placements">Every ^XG recall and ^GF field, in document order.</param>
/// <param name="UnresolvedNames">Names recalled by ^XG that the stream never downloads.
/// These live in the printer's memory from an earlier job, so the bitmap simply is not
/// in the file; a reader has to say so rather than pretend the label is complete.</param>
public sealed record ZplGraphicScan(
    IReadOnlyList<ZplGraphicDefinition> Definitions,
    IReadOnlyList<ZplGraphicPlacement> Placements,
    IReadOnlyList<string> UnresolvedNames);

/// <summary>
/// Finds the graphics in a ZPL stream: ~DG downloads, ^XG recalls, and inline ^GF
/// fields, each with the field origin in force where it is drawn.
///
/// This reads foreign ZPL rather than our own, so it is deliberately forgiving. It
/// tracks ^FO/^FT origins and the ^LH label home, resets the origin at ^FS the way a
/// printer does, and counts ^XA blocks so a multi-label file can be attributed. It
/// never throws: a header it cannot parse is skipped, and a recall with no matching
/// download is reported instead of dropped.
/// </summary>
public static partial class ZplGraphicScanner
{
    /// <summary>~DG name, total bytes, bytes per row. The payload runs from the end of
    /// this header to the next command, since graphic data contains no ^ or ~.</summary>
    [GeneratedRegex(@"~DG(?<name>[^,\^~\r\n]{1,128}),\s*(?<total>\d+)\s*,\s*(?<row>\d+)\s*,",
        RegexOptions.IgnoreCase)]
    private static partial Regex DownloadHeader();

    /// <summary>The commands that decide where a graphic lands, in document order.</summary>
    [GeneratedRegex(
        @"\^F(?<origin>[OT])(?<ox>-?\d+),(?<oy>-?\d+)"
        + @"|\^LH(?<lx>-?\d+),(?<ly>-?\d+)"
        + @"|\^XG(?<recall>[^,\^~\r\n]{0,128})(?:,(?<mx>\d+))?(?:,(?<my>\d+))?"
        + @"|\^GF(?<fmt>[ABC]?),(?<gtotal>\d+),(?<gbin>\d+),(?<grow>\d+),"
        + @"|(?<fs>\^FS)"
        + @"|(?<xa>\^XA)",
        RegexOptions.IgnoreCase)]
    private static partial Regex PlacementCommands();

    public static ZplGraphicScan Scan(string zpl)
    {
        if (string.IsNullOrEmpty(zpl))
        {
            return new ZplGraphicScan([], [], []);
        }

        List<ZplGraphicDefinition> definitions = ReadDownloads(zpl);
        List<ZplGraphicPlacement> placements = ReadPlacements(zpl);

        var known = new HashSet<string>(definitions.Select(d => d.Name), StringComparer.Ordinal);
        var unresolved = new List<string>();
        var seenUnresolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (ZplGraphicPlacement placement in placements)
        {
            if (placement.Name is { } name && !known.Contains(name) && seenUnresolved.Add(name))
            {
                unresolved.Add(name);
            }
        }

        return new ZplGraphicScan(definitions, placements, unresolved);
    }

    /// <summary>
    /// Rewrites every ~DG and ^XG name to the fully qualified R:NAME.GRF form.
    ///
    /// A printer needs no such help: it defaults an unqualified name to DRAM and .GRF,
    /// so "~DGLOGO" and "^XGLOGO" refer to the same graphic. BinaryKits 1.3.1 does not
    /// apply that default consistently, and a label written the short way (which is how
    /// the Atak corpus and plenty of other real ZPL is written) renders with every logo
    /// missing and no error to say so. Qualifying both sides makes them agree.
    ///
    /// This is a renderer workaround, not a transform of anyone's label: it is applied
    /// on the way into the preview only. Nothing we save, export, or print goes through
    /// it, so a file the user opens comes back out byte for byte.
    /// </summary>
    public static string QualifyGraphicNames(string zpl)
    {
        if (string.IsNullOrEmpty(zpl) ||
            (!zpl.Contains("~DG", StringComparison.OrdinalIgnoreCase) &&
             !zpl.Contains("^XG", StringComparison.OrdinalIgnoreCase)))
        {
            return zpl;
        }

        return GraphicNameReference().Replace(zpl, static match =>
        {
            string name = NormalizeName(match.Groups["gname"].Value);
            return name.Length == 0
                ? match.Value
                : $"{match.Groups["cmd"].Value}R:{name}.GRF,";
        });
    }

    /// <summary>The name argument of either graphic command, up to its first comma.</summary>
    [GeneratedRegex(@"(?<cmd>~DG|\^XG)(?<gname>[^,\^~\r\n]{1,128}),", RegexOptions.IgnoreCase)]
    private static partial Regex GraphicNameReference();

    /// <summary>
    /// Reduces every graphic's payload to the data itself, dropping the framing the file
    /// wrote around it: the whitespace it wraps and indents with, and the "//" comment
    /// lines that follow it.
    ///
    /// Graphic data runs from the end of the command's header to the next ^ or ~, so
    /// anything a file writes in that gap lands inside the payload. A printer does not
    /// care, since it consumes the byte count the header declared and ignores the rest,
    /// and this reader does not either, since it skips whitespace and drops characters
    /// that are not part of the format. BinaryKits does: one tab or one comment makes it
    /// throw on the whole ~DG, so the graphic is never stored and every ^XG that recalls
    /// it draws nothing at all, with a single diagnostic to say so.
    ///
    /// That is not a rare shape. The corpus writes comments after a download routinely
    /// ("//CHAMADA", "// SISBI IMAGEM"), which cost 160.zpl its only stamp and
    /// NOVASUINO.zpl all six of its logos; 440.zpl ends a payload with two tab
    /// characters, which cost it one. Line breaks are the one piece of framing the engine
    /// already copes with, and they are removed here too because it makes no difference
    /// to it and one rule is easier to keep true than two.
    ///
    /// The comment half is skipped for a :B64: or :Z64: payload, because "//" is a legal
    /// pair of base64 characters and cutting there would corrupt the image. Whitespace
    /// still goes, which is what decoding the base64 needs anyway.
    /// </summary>
    public static string CompactGraphicData(string zpl)
    {
        if (string.IsNullOrEmpty(zpl))
        {
            return zpl;
        }

        var sb = new System.Text.StringBuilder(zpl.Length);
        int copied = 0;
        foreach (Match match in GraphicDataHeader().Matches(zpl))
        {
            int start = match.Index + match.Length;
            if (start < copied)
            {
                // A header inside a payload already dealt with is not a real one.
                continue;
            }

            int end = start + PayloadLength(zpl, start);
            string payload = zpl[start..end];
            string compacted = Compact(payload);
            if (string.Equals(compacted, payload, StringComparison.Ordinal))
            {
                continue;
            }

            sb.Append(zpl, copied, start - copied).Append(compacted);
            copied = end;
        }

        return copied == 0 ? zpl : sb.Append(zpl, copied, zpl.Length - copied).ToString();
    }

    /// <summary>The header of either command that carries graphic data: ~DG's download
    /// and ^GF's inline field.</summary>
    [GeneratedRegex(
        @"~DG[^,\^~\r\n]{1,128},\s*\d+\s*,\s*\d+\s*,"
        + @"|\^GF[ABC]?,\d+,\d+,\d+,",
        RegexOptions.IgnoreCase)]
    private static partial Regex GraphicDataHeader();

    private static string Compact(string payload)
    {
        bool base64 = payload.Contains(":B64:", StringComparison.OrdinalIgnoreCase)
                      || payload.Contains(":Z64:", StringComparison.OrdinalIgnoreCase);

        var sb = new System.Text.StringBuilder(payload.Length);
        for (int i = 0; i < payload.Length; i++)
        {
            char c = payload[i];
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (!base64 && c == '/' && i + 1 < payload.Length && payload[i + 1] == '/')
            {
                while (i < payload.Length && payload[i] is not ('\r' or '\n'))
                {
                    i++;
                }

                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Strips the device prefix and .GRF suffix so "R:LOGO.GRF", "E:LOGO.GRF"
    /// and "LOGO" are one graphic. Zebra defaults an unqualified name to R: and .GRF,
    /// which is why the corpus writes the bare form and drivers write the long one.</summary>
    public static string NormalizeName(string name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length > 1 && trimmed[1] == ':')
        {
            trimmed = trimmed[2..];
        }

        if (trimmed.EndsWith(".GRF", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.ToUpperInvariant();
    }

    private static List<ZplGraphicDefinition> ReadDownloads(string zpl)
    {
        var definitions = new List<ZplGraphicDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in DownloadHeader().Matches(zpl))
        {
            if (!TryInt(match.Groups["total"].Value, out int total) ||
                !TryInt(match.Groups["row"].Value, out int bytesPerRow) ||
                total <= 0 || bytesPerRow <= 0)
            {
                continue;
            }

            string name = NormalizeName(match.Groups["name"].Value);
            if (name.Length == 0)
            {
                continue;
            }

            // A file that repeats its downloads once per label block (the Atak corpus
            // does) describes one graphic, not several.
            if (!seen.Add(name))
            {
                continue;
            }

            definitions.Add(new ZplGraphicDefinition(
                name, total, bytesPerRow, ReadPayload(zpl, match.Index + match.Length)));
        }

        return definitions;
    }

    private static List<ZplGraphicPlacement> ReadPlacements(string zpl)
    {
        var placements = new List<ZplGraphicPlacement>();
        int x = 0;
        int y = 0;
        int homeX = 0;
        int homeY = 0;
        int labelIndex = -1;
        int inlineCount = 0;

        foreach (Match match in PlacementCommands().Matches(zpl))
        {
            if (match.Groups["xa"].Success)
            {
                labelIndex++;
                homeX = 0;
                homeY = 0;
                x = 0;
                y = 0;
                continue;
            }

            if (match.Groups["fs"].Success)
            {
                // The origin belongs to the field that just ended.
                x = 0;
                y = 0;
                continue;
            }

            if (match.Groups["origin"].Success)
            {
                TryInt(match.Groups["ox"].Value, out x);
                TryInt(match.Groups["oy"].Value, out y);
                continue;
            }

            if (match.Groups["lx"].Success)
            {
                TryInt(match.Groups["lx"].Value, out homeX);
                TryInt(match.Groups["ly"].Value, out homeY);
                continue;
            }

            int block = Math.Max(labelIndex, 0);

            if (match.Groups["recall"].Success)
            {
                string name = NormalizeName(match.Groups["recall"].Value);
                if (name.Length == 0)
                {
                    continue;
                }

                placements.Add(new ZplGraphicPlacement(
                    name,
                    homeX + x,
                    homeY + y,
                    Magnification(match.Groups["mx"].Value),
                    Magnification(match.Groups["my"].Value),
                    Inline: null,
                    block));
                continue;
            }

            if (match.Groups["gtotal"].Success &&
                TryInt(match.Groups["gtotal"].Value, out int total) &&
                TryInt(match.Groups["grow"].Value, out int bytesPerRow) &&
                total > 0 && bytesPerRow > 0)
            {
                var inline = new ZplGraphicDefinition(
                    $"GF{++inlineCount}", total, bytesPerRow,
                    ReadPayload(zpl, match.Index + match.Length));
                placements.Add(new ZplGraphicPlacement(
                    Name: null, homeX + x, homeY + y, 1, 1, inline, block));
            }
        }

        return placements;
    }

    /// <summary>Graphic data runs to the next command. Hex, the compression scheme and
    /// the base64 envelopes all avoid ^ and ~, so those two characters end the payload.
    /// The framing a file writes around the data comes out here rather than being left
    /// for the decoder to drop: "//CHAMADA" is four hex digits and two repeat counts to
    /// anything reading it as graphic data, and only the row count saves it today.</summary>
    private static string ReadPayload(string zpl, int start) =>
        Compact(zpl.Substring(start, PayloadLength(zpl, start)));

    private static int PayloadLength(string zpl, int start)
    {
        int end = zpl.AsSpan(start).IndexOfAny('^', '~');
        return end < 0 ? zpl.Length - start : end;
    }

    /// <summary>^XG magnification is 1 to 10; an absent or unreadable value means 1.</summary>
    private static int Magnification(string value) =>
        TryInt(value, out int parsed) ? Math.Clamp(parsed, 1, 10) : 1;

    private static bool TryInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
}
