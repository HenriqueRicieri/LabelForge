using System.Text;

namespace LabelForge.Core.Zpl;

/// <summary>
/// What a byte means, which in ZPL is ^CI's question and nobody else's.
///
/// A ^FH escape names a BYTE, not a character: "_82" is the byte 0x82, and which letter
/// that is depends on the encoding the printer is set to. Treating the two as the same
/// number turns "Minist_82rio" into a C1 control character instead of "Ministerio" with
/// its accent, and the corpus is full of exactly that shape.
///
/// Only three encodings are modelled, and the reason is what a printer actually does with
/// the rest. ^CI0 to ^CI13 are one 850-based upper range with a handful of national
/// substitutions below 0x80, positions an escape has no reason to name, so they all read
/// the same above 0x7F and 850 is the honest answer for every one of them; it is also the
/// printer's own default, which matters because most real labels declare no ^CI at all.
/// ^CI27 is Zebra's code page 1252 and ^CI28 is UTF-8, where an escape run is a multi-byte
/// sequence and has to be decoded as one. ^CI29 upwards are the other Unicode transforms,
/// which no corpus label uses; a file that names one is read as 850 and the importer says
/// so rather than pretending.
///
/// Measured against the offline renderer rather than taken from the manual: it reads an
/// escape under no ^CI, ^CI0 and ^CI13 identically to a literal code page 850 character,
/// under ^CI27 as 1252, and under ^CI28 as UTF-8. The canvas is that renderer, so agreeing
/// with it is what stops the designer showing a different letter than it imported.
/// </summary>
public static class ZplCodePage
{
    /// <summary>Zebra's code page 850, the printer's default and this reader's.</summary>
    public const int Default = 850;

    /// <summary>The provider is asked directly instead of being registered globally: a
    /// library that mutates process-wide encoding state is a surprise for whoever hosts
    /// it, and nothing here needs the code pages to be reachable by name elsewhere. The
    /// fallback is the nearest single-byte reading rather than a throw, because this runs
    /// in a static initializer and a missing code page must not take the app down.</summary>
    private static readonly Encoding Cp850 =
        CodePagesEncodingProvider.Instance.GetEncoding(850) ?? Encoding.Latin1;

    private static readonly Encoding Cp1252 =
        CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? Encoding.Latin1;

    /// <summary>The encoding a ^CI parameter selects, falling back to the printer's
    /// default for the sets that share it and for the ones not modelled.</summary>
    public static Encoding For(int internationalSet) => internationalSet switch
    {
        27 => Cp1252,
        28 => Encoding.UTF8,
        _ => Cp850,
    };

    /// <summary>Whether <see cref="For"/> genuinely knows this set, so a caller can
    /// report the ones it is guessing at instead of silently mis-reading them.</summary>
    public static bool IsModelled(int internationalSet) =>
        internationalSet is (>= 0 and <= 13) or 27 or 28;

    /// <summary>Decodes a run of escaped bytes. A run rather than a byte at a time
    /// because UTF-8 spends several bytes on one letter; single-byte code pages give the
    /// same answer either way.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes, int internationalSet) =>
        bytes.Length == 0 ? string.Empty : For(internationalSet).GetString(bytes);
}
