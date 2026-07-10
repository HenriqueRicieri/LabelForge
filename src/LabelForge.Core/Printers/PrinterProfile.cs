using LabelForge.Core.Model;

namespace LabelForge.Core.Printers;

/// <summary>
/// A Zebra printer model at a specific density. Drives label-setup defaults and
/// design-time validation (warn, never block, when a design exceeds the head).
/// </summary>
public sealed record PrinterProfile(string Id, string ModelName, int Dpi, int Dpmm, double MaxPrintWidthMm)
{
    /// <summary>Sentinel meaning "no specific printer": no density push, no validation.</summary>
    public static PrinterProfile Any { get; } = new("any", "Any printer", 0, 0, 0);

    public bool IsAny => Dpmm == 0;

    public int MaxPrintWidthDots => IsAny ? 0 : Units.MmToDots(MaxPrintWidthMm, Dpmm);

    /// <summary>Design-time warnings for a document targeted at this printer.</summary>
    public IReadOnlyList<string> Validate(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (IsAny)
        {
            return [];
        }

        var warnings = new List<string>();
        if (document.Dpmm != Dpmm)
        {
            warnings.Add($"Label density ({document.Dpmm} dpmm) differs from the printer ({Dpmm} dpmm)");
        }

        if (document.WidthMm > MaxPrintWidthMm)
        {
            warnings.Add($"Label width {document.WidthMm:0.#} mm exceeds the print head ({MaxPrintWidthMm:0.#} mm)");
        }

        return warnings;
    }

    public override string ToString() => IsAny ? ModelName : $"{ModelName} ({Dpi} dpi)";
}

/// <summary>Starter catalog of common Zebra printers; extended over time.</summary>
public static class PrinterCatalog
{
    public static IReadOnlyList<PrinterProfile> All { get; } =
    [
        PrinterProfile.Any,
        new("zd421-203", "Zebra ZD421", 203, 8, 104),
        new("zd421-300", "Zebra ZD421", 300, 12, 104),
        new("gk420d", "Zebra GK420d", 203, 8, 104),
        new("gx430t", "Zebra GX430t", 300, 12, 104),
        new("zt411-203", "Zebra ZT411", 203, 8, 104),
        new("zt411-300", "Zebra ZT411", 300, 12, 104),
        new("zt411-600", "Zebra ZT411", 600, 24, 104),
    ];
}
