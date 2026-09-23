using System.Globalization;

namespace LabelForge.Core.Zpl;

/// <summary>One ZPL command and the text between it and the next one.</summary>
/// <param name="Prefix">'^' for a format command, '~' for a control command.</param>
/// <param name="Code">The two-character code, upper-cased. Font commands keep their
/// designator here ("A0"), because in ZPL that character is part of the command.</param>
/// <param name="Parameters">Everything up to the next command, untrimmed.</param>
public readonly record struct ZplCommand(char Prefix, string Code, string Parameters)
{
    /// <summary>Comma-separated arguments. Empty parameters give an empty array, so a
    /// caller can index safely through <see cref="Arg"/> without checking length.</summary>
    public string[] Arguments =>
        Parameters.Length == 0 ? [] : Parameters.Split(',');

    /// <summary>The argument at <paramref name="index"/>, or an empty string.</summary>
    public string Arg(int index)
    {
        string[] args = Arguments;
        return index >= 0 && index < args.Length ? args[index].Trim() : string.Empty;
    }

    /// <summary>The argument at <paramref name="index"/> as an integer, or
    /// <paramref name="fallback"/> when it is absent or not a number.</summary>
    public int Int(int index, int fallback = 0) =>
        int.TryParse(Arg(index), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
            out int value)
            ? value
            : fallback;
}

/// <summary>
/// Splits a ZPL stream into commands. This is the shared front end for reading ZPL we
/// did not write, so it is lenient by design: unknown codes come through as data for
/// the caller to report, and nothing here throws.
///
/// Two details of the format drive the implementation. A command's parameters run until
/// the next command, because ZPL has no terminator; and field data (^FD) ends only at a
/// '^', since a tilde inside human text is ordinary punctuation rather than a command.
/// A bare tilde can also be a command parameter, such as the ^BX escape character.
/// </summary>
public static class ZplCommandReader
{
    public static IEnumerable<ZplCommand> Read(string zpl)
    {
        if (string.IsNullOrEmpty(zpl))
        {
            yield break;
        }

        int i = 0;
        while (i < zpl.Length)
        {
            char prefix = zpl[i];
            if (prefix is not ('^' or '~') ||
                prefix == '~' && !IsControlCommand(zpl, i))
            {
                // Text outside any command: comment lines and driver preamble. Skipped
                // here; the viewer is what reports those.
                i++;
                continue;
            }

            int codeStart = i + 1;
            if (codeStart >= zpl.Length)
            {
                yield break;
            }

            int codeLength = Math.Min(2, zpl.Length - codeStart);
            string code = zpl.Substring(codeStart, codeLength).ToUpperInvariant();
            int paramStart = codeStart + codeLength;

            // ^FD holds arbitrary text, where '~' is punctuation and not a command.
            int end = NextCommand(zpl, paramStart, code == "FD");

            if (end < 0)
            {
                end = zpl.Length;
            }

            yield return new ZplCommand(prefix, code, zpl[paramStart..end]);
            i = end;
        }
    }

    private static int NextCommand(string zpl, int start, bool fieldData)
    {
        ReadOnlySpan<char> span = zpl.AsSpan();
        while (start < span.Length)
        {
            int offset = fieldData
                ? span[start..].IndexOf('^')
                : span[start..].IndexOfAny('^', '~');
            if (offset < 0)
            {
                return -1;
            }

            int candidate = start + offset;
            if (span[candidate] == '^' || IsControlCommand(zpl, candidate))
            {
                return candidate;
            }

            start = candidate + 1;
        }

        return -1;
    }

    private static bool IsControlCommand(string zpl, int index) =>
        index + 2 < zpl.Length &&
        char.IsAsciiLetterOrDigit(zpl[index + 1]) &&
        char.IsAsciiLetterOrDigit(zpl[index + 2]);
}
