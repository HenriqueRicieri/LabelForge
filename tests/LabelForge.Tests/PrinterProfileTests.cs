using LabelForge.Core.Model;
using LabelForge.Core.Printers;

namespace LabelForge.Tests;

public sealed class PrinterProfileTests
{
    private static readonly PrinterProfile Zd421 = new("zd421-203", "Zebra ZD421", 203, 8, 104);

    [Fact]
    public void MaxPrintWidthDots_DerivesFromMmAndDpmm()
    {
        Assert.Equal(832, Zd421.MaxPrintWidthDots);
    }

    [Fact]
    public void Validate_WarnsOnWidthAndDensityMismatch()
    {
        var doc = new LabelDocument { WidthMm = 120, HeightMm = 60, Dpmm = 12 };

        var warnings = Zd421.Validate(doc);

        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("density"));
        Assert.Contains(warnings, w => w.Contains("print head"));
    }

    [Fact]
    public void Validate_IsCleanForAFittingDocument()
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        Assert.Empty(Zd421.Validate(doc));
    }

    [Fact]
    public void AnyPrinter_NeverWarns()
    {
        var doc = new LabelDocument { WidthMm = 500, HeightMm = 60, Dpmm = 24 };
        Assert.Empty(PrinterProfile.Any.Validate(doc));
        Assert.True(PrinterProfile.Any.IsAny);
    }

    [Fact]
    public void Catalog_StartsWithAny_AndHasUniqueIds()
    {
        Assert.Same(PrinterProfile.Any, PrinterCatalog.All[0]);
        Assert.Equal(PrinterCatalog.All.Count, PrinterCatalog.All.Select(p => p.Id).Distinct().Count());
    }
}
