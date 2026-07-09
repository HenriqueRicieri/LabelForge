namespace LabelForge.Core.Zpl;

/// <summary>
/// Check-digit math for the UPC/EAN family. The printer computes the final digit at
/// print time, but the designer uses these to validate input and to show a warning
/// when a barcode's data cannot be encoded.
/// </summary>
public static class BarcodeCheckDigit
{
    /// <summary>
    /// Computes the modulo-10 check digit used by EAN-13 and UPC-A over the given
    /// leading digits (12 for EAN-13, 11 for UPC-A).
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
}
