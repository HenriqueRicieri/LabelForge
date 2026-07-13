using LabelForge.Core.Media;

namespace LabelForge.Tests;

public sealed class StockCatalogTests
{
    [Fact]
    public void All_LoadsTheEmbeddedCatalog()
    {
        Assert.True(StockCatalog.All.Count > 700, $"expected the full catalog, got {StockCatalog.All.Count}");
    }

    [Fact]
    public void All_EntriesHaveSaneData()
    {
        Assert.All(StockCatalog.All, media =>
        {
            Assert.False(string.IsNullOrWhiteSpace(media.PartNumber));
            Assert.False(string.IsNullOrWhiteSpace(media.Material));
            Assert.InRange(media.WidthMm, 1, 400);
            Assert.InRange(media.HeightMm, 1, 400);
            Assert.InRange(media.RadiusMm, 0, 10);
        });
    }

    [Fact]
    public void All_HasNoDuplicatePartNumberAndSizePairs()
    {
        var keys = StockCatalog.All
            .Select(m => (m.PartNumber.ToUpperInvariant(), m.WidthMm, m.HeightMm));
        Assert.Equal(StockCatalog.All.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Search_FindsAKnownPartNumber()
    {
        var results = StockCatalog.Search("3007301-T");

        var media = Assert.Single(results);
        Assert.Equal(75.4, media.WidthMm, 3);
        Assert.True(media.Continuous, "a 20 m receipt roll must be flagged continuous");
    }

    [Fact]
    public void Search_CombinesTermsAcrossFields()
    {
        var results = StockCatalog.Search("z-select 4000d");

        Assert.NotEmpty(results);
        Assert.All(results, m => Assert.Contains("4000D", m.Material, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Search_IsEmptyForBlankOrUnmatchedQueries()
    {
        Assert.Empty(StockCatalog.Search("   "));
        Assert.Empty(StockCatalog.Search("no-such-part-number-xyz"));
    }

    [Fact]
    public void Search_HonorsTheLimit()
    {
        Assert.Equal(5, StockCatalog.Search("Z-", limit: 5).Count);
    }
}
