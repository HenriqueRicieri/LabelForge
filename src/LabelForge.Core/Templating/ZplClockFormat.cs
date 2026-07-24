using System.Text;

namespace LabelForge.Core.Templating;

/// <summary>
/// Translates the .NET date/time format a clock variable is edited with into the
/// placeholders a Zebra Real Time Clock understands after a ^FC command, so the field
/// can be handed to the printer instead of being stamped into the ZPL.
///
/// Only tokens with an exact ZPL counterpart translate. Month names, AM/PM, day names,
/// stray letters, and a literal '%' (which would be read as a clock placeholder) have
/// no faithful equivalent, so they fail the translation and the caller formats the
/// value in software instead of printing something different from the preview.
/// </summary>
public static class ZplClockFormat
{
    /// <summary>The clock indicator character we declare with ^FC.</summary>
    public const char Indicator = '%';

    /// <summary>Ready-made formats offered by the designer. Every one of them
    /// translates, so picking a preset never costs the printer-clock option.</summary>
    public static IReadOnlyList<string> Presets { get; } =
    [
        "dd/MM/yyyy",
        "dd/MM/yy",
        "yyyy-MM-dd",
        "ddMMyy",
        "yyyyMMdd",
        "HH:mm",
        "HH:mm:ss",
        "dd/MM/yyyy HH:mm",
        "yyyy-MM-dd HH:mm:ss",
    ];

    // Keyed by the whole repeat run, not by a prefix: "dddd" is a weekday name, not two
    // days, so it must fail rather than quietly translate to "%d%d". Note the case
    // inversion between the dialects: .NET writes the month MM and the minute mm, ZPL
    // writes %m and %M.
    private static readonly Dictionary<string, string> Tokens = new(StringComparer.Ordinal)
    {
        ["yyyy"] = "%Y",
        ["yy"] = "%y",
        ["MM"] = "%m",
        ["dd"] = "%d",
        ["HH"] = "%H",
        ["mm"] = "%M",
        ["ss"] = "%S",
    };

    /// <summary>Characters allowed to pass through as themselves. Deliberately narrow:
    /// an unrecognized character means we cannot promise the printer would render the
    /// same string.</summary>
    private const string AllowedLiterals = "/-:. ";

    /// <summary>Converts a .NET format to its ^FC equivalent.</summary>
    /// <returns>False when any part of the format has no ZPL counterpart, in which case
    /// the field must be formatted in software.</returns>
    public static bool TryTranslate(string netFormat, out string zplFormat)
    {
        zplFormat = string.Empty;
        if (string.IsNullOrEmpty(netFormat))
        {
            return false;
        }

        var sb = new StringBuilder(netFormat.Length * 2);
        int i = 0;
        while (i < netFormat.Length)
        {
            char c = netFormat[i];
            if (char.IsAsciiLetter(c))
            {
                // Consume the whole run of the same letter: how many times a letter
                // repeats is what distinguishes a token from a wider one.
                int end = i;
                while (end < netFormat.Length && netFormat[end] == c)
                {
                    end++;
                }

                if (!Tokens.TryGetValue(netFormat[i..end], out string? token))
                {
                    return false;
                }

                sb.Append(token);
                i = end;
                continue;
            }

            if (!AllowedLiterals.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }

            sb.Append(c);
            i++;
        }

        zplFormat = sb.ToString();
        return true;
    }
}
