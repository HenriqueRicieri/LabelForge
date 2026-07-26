namespace LabelForge.Core.Zpl;

/// <param name="Code">The AI itself, as printed in brackets: "01", "10", "3102".</param>
/// <param name="Name">What it means, in plain words.</param>
/// <param name="Length">Length of the value in characters, or 0 when it varies.</param>
/// <param name="NumericOnly">True when the value must be digits.</param>
public sealed record Gs1ApplicationIdentifier(
    string Code,
    string Name,
    int Length,
    bool NumericOnly)
{
    /// <summary>True when the value has no fixed length, which is what decides whether a
    /// separator has to follow it.</summary>
    public bool IsVariableLength => Length == 0;

    public override string ToString() =>
        IsVariableLength ? $"({Code}) {Name}" : $"({Code}) {Name}, {Length} characters";
}

/// <summary>
/// The GS1 application identifiers a label is likely to carry.
///
/// Deliberately a working set rather than the full standard, which runs to hundreds. The
/// ones here are what shipping, product and traceability labels actually use, and the
/// list is data: an unknown AI is reported rather than rejected, so a payload using one
/// outside this set still reads and still prints.
///
/// The distinction that matters most is fixed against variable length, because it decides
/// where a separator is required. Get that wrong and the barcode scans as one long
/// wrong-valued field rather than failing outright, which is the worst way for a label to
/// be broken.
/// </summary>
public static class Gs1Catalog
{
    public static IReadOnlyList<Gs1ApplicationIdentifier> All { get; } =
    [
        new("00", "Serial shipping container code (SSCC)", 18, true),
        new("01", "Global trade item number (GTIN)", 14, true),
        new("02", "GTIN of contained trade items", 14, true),
        new("10", "Batch or lot number", 0, false),
        new("11", "Production date (YYMMDD)", 6, true),
        new("12", "Due date (YYMMDD)", 6, true),
        new("13", "Packaging date (YYMMDD)", 6, true),
        new("15", "Best before date (YYMMDD)", 6, true),
        new("16", "Sell by date (YYMMDD)", 6, true),
        new("17", "Expiry date (YYMMDD)", 6, true),
        new("20", "Product variant", 2, true),
        new("21", "Serial number", 0, false),
        new("22", "Consumer product variant", 0, false),
        new("30", "Count of items", 0, true),
        new("37", "Count of trade items in a logistic unit", 0, true),
        new("240", "Additional product identification", 0, false),
        new("241", "Customer part number", 0, false),
        new("251", "Reference to the source entity", 0, false),
        new("400", "Customer purchase order number", 0, false),
        new("401", "Consignment number (GINC)", 0, false),
        new("402", "Shipment number (GSIN)", 17, true),
        new("410", "Ship to global location number", 13, true),
        new("412", "Purchased from global location number", 13, true),
        new("414", "Physical location global location number", 13, true),
        new("420", "Ship to postal code, same country", 0, false),

        // The measurement AIs end in a decimal-place digit: 3102 is a net weight in
        // kilograms with two decimals. The family is listed rather than each member,
        // because the last digit is a parameter and not part of the identity.
        new("3100", "Net weight, kg (0 decimals)", 6, true),
        new("3101", "Net weight, kg (1 decimal)", 6, true),
        new("3102", "Net weight, kg (2 decimals)", 6, true),
        new("3103", "Net weight, kg (3 decimals)", 6, true),
        new("3200", "Net weight, lb (0 decimals)", 6, true),
        new("3201", "Net weight, lb (1 decimal)", 6, true),
        new("3202", "Net weight, lb (2 decimals)", 6, true),
        new("3920", "Amount payable (0 decimals)", 0, true),
        new("3922", "Amount payable (2 decimals)", 0, true),
    ];

    public static Gs1ApplicationIdentifier? Find(string code) =>
        All.FirstOrDefault(ai => string.Equals(ai.Code, code, StringComparison.Ordinal));
}
