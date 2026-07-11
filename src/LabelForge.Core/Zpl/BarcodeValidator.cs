using LabelForge.Core.Model;

namespace LabelForge.Core.Zpl;

/// <summary>
/// Design-time validation of barcode data against its symbology. Returns a single
/// human-readable warning, or null when the data is encodable. The designer surfaces
/// this so un-encodable data produces a clear message instead of a silent blank
/// preview (a linear barcode that cannot encode its data makes the engine degrade to
/// an empty image). Template-marker data (##...##) is left to the preview substitutor
/// and is not judged here.
/// </summary>
public static class BarcodeValidator
{
    // Code 39 encodes uppercase A-Z, digits, and a small set of symbols.
    private const string Code39Charset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    public static string? Validate(BarcodeSymbology symbology, string? data)
    {
        data ??= string.Empty;

        // Markers are placeholders substituted with sample values before rendering,
        // so there is nothing to validate at design time.
        if (data.Contains("##"))
        {
            return null;
        }

        if (data.Length == 0)
        {
            return "Enter barcode data.";
        }

        switch (symbology)
        {
            case BarcodeSymbology.Ean13:
                if (data.Length is not (12 or 13) || !data.All(char.IsAsciiDigit))
                {
                    return "EAN-13 needs 12 digits (or 13 with the check digit).";
                }

                if (data.Length == 13 && !BarcodeCheckDigit.IsValidEan13(data))
                {
                    return $"EAN-13 check digit should be {BarcodeCheckDigit.ModuloTen(data[..12])}.";
                }

                break;

            case BarcodeSymbology.UpcA:
                if (data.Length is not (11 or 12) || !data.All(char.IsAsciiDigit))
                {
                    return "UPC-A needs 11 digits (or 12 with the check digit).";
                }

                if (data.Length == 12 && !BarcodeCheckDigit.IsValidUpcA(data))
                {
                    return $"UPC-A check digit should be {BarcodeCheckDigit.ModuloTen(data[..11])}.";
                }

                break;

            case BarcodeSymbology.Code39:
            {
                char[] invalid = data.Where(c => !Code39Charset.Contains(c)).Distinct().ToArray();
                if (invalid.Length > 0)
                {
                    return $"Code 39 cannot encode: {string.Join(' ', invalid)} (use A-Z, 0-9, - . space $ / + %).";
                }

                break;
            }

            case BarcodeSymbology.Code128:
                if (data.Any(c => c > 127))
                {
                    return "Code 128 encodes ASCII characters only.";
                }

                break;
        }

        return null;
    }
}
