using LabelForge.Core.Zpl;

namespace LabelForge.Core.Model;

/// <summary>
/// What an Interleaved 2 of 5 symbol actually encodes, and how wide the printer draws
/// it. The single place both questions are answered, for the reason
/// <see cref="Pdf417Metrics"/> exists: the selection outline, the resize gesture, the
/// quiet-zone check and the properties panel all ask, and they must not be able to
/// disagree.
///
/// The interesting part is that the digits encoded are not always the digits typed. ^B2
/// interleaves a pair of digits into one set of bars and spaces, so the count has to be
/// even, and the manual is explicit about what happens when it is not: "The printer
/// automatically adds a leading 0 (zero) if an odd number of digits is received." Add
/// <see cref="BarcodeElement.AddCheckDigit"/> and the printer appends a digit of its own
/// first, which can be what makes the count odd. Neither is visible in the ZPL, and both
/// change the number that scans, so the model works them out rather than hoping.
/// </summary>
public static class Interleaved2of5
{
    /// <summary>
    /// The characters the printer encodes: the field data, plus a Mod 10 check digit if
    /// the field asks for one, left-padded with a zero to an even count.
    ///
    /// Non-digit data is passed through untouched apart from the padding. It cannot be
    /// encoded at all and <see cref="BarcodeValidator"/> says so; guessing a check digit
    /// for it here would only make the footprint lie about a symbol that will not print.
    /// </summary>
    public static string Encoded(string? data, bool addCheckDigit)
    {
        string digits = data ?? string.Empty;

        if (addCheckDigit && digits.Length > 0 && digits.All(char.IsAsciiDigit))
        {
            digits += (char)('0' + BarcodeCheckDigit.ModuloTen(digits));
        }

        return digits.Length % 2 == 0 ? digits : "0" + digits;
    }

    /// <inheritdoc cref="Encoded(string, bool)"/>
    public static string Encoded(BarcodeElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Encoded(element.Data, element.AddCheckDigit);
    }

    /// <summary>
    /// The symbol's width in dots.
    ///
    /// Alone among the symbologies here this is not a whole number of modules, so it
    /// cannot be counted in them: the wide bar is <c>floor(ratio * module)</c> dots, which
    /// at a ratio of 2.5 and a module of 2 is 5 - half a module. Measured against the
    /// rendered ink at module widths 2 and 3, ratios 2.0 through 3.0 and 2 to 16 digits,
    /// exact every time. A pair of digits is 4 wide elements and 6 narrow ones; the start
    /// pattern is 4 narrow and the stop is one wide and two narrow.
    /// </summary>
    public static int WidthDots(BarcodeElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        int module = Math.Max(element.ModuleWidthDots, 1);
        int wide = Math.Max((int)(element.WideBarRatio * module), module);
        int pairs = Encoded(element).Length / 2;

        return (pairs * (4 * wide + 6 * module)) + wide + (6 * module);
    }
}
