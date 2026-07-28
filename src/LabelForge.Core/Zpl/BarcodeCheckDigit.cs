using LabelForge.Core.Model;

namespace LabelForge.Core.Zpl;

/// <summary>
/// Check-digit math for the symbologies that carry one, and the design-time assistance
/// built on it.
///
/// The math is the easy half. The half worth having is knowing whose job the digit is,
/// because it differs by symbology and is invisible in the ZPL either way. EAN-13 and
/// UPC-A always carry one and the printer always computes it, from data that is one
/// digit shorter than the number a person reads off the label. Interleaved 2 of 5 only
/// carries one if ^B2 is asked for it, and ZPL's default is not to. So the same twelve
/// digits mean three different printed numbers depending on the command they are sent
/// with, and nothing on screen says so unless it is worked out and stated.
/// </summary>
public static class BarcodeCheckDigit
{
    /// <summary>
    /// Computes the modulo-10 check digit over the given leading digits: the scheme
    /// EAN-13, UPC-A and Interleaved 2 of 5 all use, and the one the ZPL manual points
    /// at from each of their pages.
    ///
    /// The weights are positional from the right of the value being checked, so the rule
    /// is the same at every length and no symbology needs its own version.
    /// </summary>
    /// <exception cref="ArgumentException">If the input is not all digits.</exception>
    public static int ModuloTen(string leadingDigits)
    {
        ArgumentNullException.ThrowIfNull(leadingDigits);
        if (leadingDigits.Length == 0 || !leadingDigits.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Barcode data must contain only digits.", nameof(leadingDigits));
        }

        // Weights alternate 3 and 1, starting with 3 applied to the right-most digit.
        int sum = 0;
        for (int i = 0; i < leadingDigits.Length; i++)
        {
            int digit = leadingDigits[leadingDigits.Length - 1 - i] - '0';
            sum += digit * (i % 2 == 0 ? 3 : 1);
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>True if the value is a valid EAN-13 (13 digits with a correct check digit).</summary>
    public static bool IsValidEan13(string value) =>
        value.Length == 13 &&
        value.All(char.IsAsciiDigit) &&
        ModuloTen(value[..12]) == value[12] - '0';

    /// <summary>True if the value is a valid UPC-A (12 digits with a correct check digit).</summary>
    public static bool IsValidUpcA(string value) =>
        value.Length == 12 &&
        value.All(char.IsAsciiDigit) &&
        ModuloTen(value[..11]) == value[11] - '0';

    /// <summary>
    /// The field's data with its check digit appended, or null when offering to add one
    /// would be wrong: a symbology that has no check digit, data that is not all digits,
    /// a template marker standing in for the value, or a field that already carries one
    /// (or is going to be given one by the printer).
    ///
    /// Appending it to the data is deliberately what this offers, rather than a flag.
    /// A check digit written into ^FD is a number the designer, the preview and the
    /// scanner all agree about; one left to the printer is a digit nothing on this side
    /// can show. For EAN-13 and UPC-A the printer recomputes it either way and discards
    /// the extra character, so this changes what is on screen and not one bar of what
    /// prints, which is exactly what makes it safe to offer.
    /// </summary>
    public static string? Complete(BarcodeElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        string data = element.Data ?? string.Empty;
        if (!IsPlainDigits(data))
        {
            return null;
        }

        return element.Symbology switch
        {
            BarcodeSymbology.Ean13 when data.Length == 12 => data + Digit(data),
            BarcodeSymbology.UpcA when data.Length == 11 => data + Digit(data),

            // ^B2 asked the printer for one, so adding a second would encode a digit of
            // check over the check digit.
            BarcodeSymbology.Interleaved2of5 when !element.AddCheckDigit => data + Digit(data),

            _ => null,
        };
    }

    /// <summary>
    /// One line saying what the label will scan as and where its check digit comes from,
    /// or empty when the symbology has none to talk about.
    ///
    /// Stated as the whole number rather than as the digit alone, because the whole
    /// number is the thing somebody checks against a purchase order, and for EAN-13 and
    /// UPC-A it is never the string in the data box: ^FD carries twelve digits of a
    /// thirteen-digit article number.
    /// </summary>
    public static string Describe(BarcodeElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        string data = element.Data ?? string.Empty;
        if (!IsPlainDigits(data))
        {
            return string.Empty;
        }

        switch (element.Symbology)
        {
            case BarcodeSymbology.Ean13 when data.Length == 12:
            case BarcodeSymbology.UpcA when data.Length == 11:
                return $"Scans as {data}{Digit(data)}; the printer adds check digit {Digit(data)}.";

            case BarcodeSymbology.Ean13 when data.Length == 13 && IsValidEan13(data):
            case BarcodeSymbology.UpcA when data.Length == 12 && IsValidUpcA(data):
                return $"Check digit {data[^1]} is correct.";

            case BarcodeSymbology.Interleaved2of5:
            {
                string encoded = Interleaved2of5.Encoded(element);
                string scans = $"Scans as {encoded}";

                if (element.AddCheckDigit)
                {
                    // Both of ^B2's silent edits can land at once, and the order matters:
                    // the printer works the digit out first and pads the result, so the
                    // zero it adds is not part of what the digit was computed over.
                    string padded = encoded.Length > data.Length + 1
                        ? " and a leading zero to make the count even"
                        : string.Empty;

                    // The offline renderer ignores ^B2's check-digit parameter, so the
                    // preview is a symbol short of what prints. The footprint follows the
                    // printer rather than the preview, because a box that says a label
                    // fits when it does not is the failure that matters.
                    return $"{scans}; the printer adds check digit {Digit(data)}{padded}. "
                           + "The preview cannot draw it, so the symbol prints wider than "
                           + "it looks.";
                }

                return encoded.Length > data.Length
                    ? $"{scans}; the printer adds a leading zero to make the count even. "
                      + $"No check digit (Mod 10 would be {Digit(data)})."
                    : $"No check digit (Mod 10 would be {Digit(data)}).";
            }

            default:
                return string.Empty;
        }
    }

    private static char Digit(string data) => (char)('0' + ModuloTen(data));

    /// <summary>
    /// Digits and nothing else, which is also what excludes a template marker: no marker
    /// syntax is made of digits, so `##EAN##` fails this test on its delimiters. That is
    /// the case that matters, because a marker stands in for a value nobody here has yet
    /// and any digit computed from it would be the check digit of the placeholder.
    /// </summary>
    private static bool IsPlainDigits(string data) =>
        data.Length > 0 && data.All(char.IsAsciiDigit);
}
