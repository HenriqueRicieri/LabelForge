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

    /// <summary>Validates a field, reading the settings that change what can be encoded
    /// off the element itself.</summary>
    public static string? Validate(BarcodeElement element, MarkerSyntax? markers = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Validate(element.Symbology, element.Data, markers, element.AddCheckDigit);
    }

    public static string? Validate(
        BarcodeSymbology symbology, string? data, MarkerSyntax? markers = null,
        bool addCheckDigit = false)
    {
        data ??= string.Empty;
        MarkerSyntax syntax = markers ?? MarkerSyntax.Default;

        // A complete marker is a placeholder substituted with a sample value before
        // rendering, so there is nothing to validate at design time. Match what the
        // substitutor actually replaces: an opening delimiter followed by a closing one.
        // A lone delimiter is literal data and falls through to normal validation.
        int open = data.IndexOf(syntax.Open, StringComparison.Ordinal);
        if (open >= 0 &&
            data.IndexOf(syntax.Close, open + syntax.Open.Length, StringComparison.Ordinal) >= 0)
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

            case BarcodeSymbology.Interleaved2of5:
                if (!data.All(char.IsAsciiDigit))
                {
                    return "Interleaved 2 of 5 encodes digits only.";
                }

                // An odd count is not an error to a printer: the manual says it "adds a
                // leading 0 (zero)", so the label prints and scans as a longer number
                // than the one that was typed. It is an error to the offline renderer,
                // which refuses the field and returns no image at all, so a single odd
                // field blanks the whole preview. Both are worth one sentence, because
                // the first is silent and the second looks like the app being broken.
                if (data.Length % 2 == 1)
                {
                    return "Interleaved 2 of 5 encodes digits in pairs, so the printer adds "
                           + $"a leading zero and this scans as {Interleaved2of5.Encoded(data, addCheckDigit)}. "
                           + "The preview stays blank until the count is even.";
                }

                break;
        }

        return null;
    }
}
