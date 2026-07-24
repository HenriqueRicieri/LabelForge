using System.Globalization;
using LabelForge.Core.Media;

namespace LabelForge.Tests;

/// <summary>
/// The user's own media definitions: third-party stock the Zebra catalog does not
/// list, which is what most print shops actually run. The store sits on a file the
/// user could edit or lose, so the failure paths matter as much as the happy one:
/// reading degrades to nothing, writing reports what went wrong.
/// </summary>
public sealed class UserMediaTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"labelforge-media-{Guid.NewGuid():N}");

    private string PresetsPath => Path.Combine(_directory, "user-media.json");

    private UserMediaStore Store() => new(PresetsPath);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // ---- Storing ------------------------------------------------------------------

    [Fact]
    public void Add_ThenLoad_RoundTripsEveryField()
    {
        UserMediaStore store = Store();

        UserMediaResult result = store.Add(StockMedia.UserDefined(
            "Etiqueta 50 x 30", 50.8, 30, "Couche", radiusMm: 1.5));

        Assert.Null(result.Error);

        StockMedia saved = Assert.Single(Store().Load());
        Assert.Equal("Etiqueta 50 x 30", saved.PartNumber);
        Assert.Equal("Couche", saved.Material);
        Assert.Equal(50.8, saved.WidthMm);
        Assert.Equal(30, saved.HeightMm);
        Assert.Equal(1.5, saved.RadiusMm);
        Assert.False(saved.Continuous);
        Assert.True(saved.IsUserDefined);
    }

    [Fact]
    public void Add_WithAnExistingName_ReplacesTheDefinitionRatherThanDuplicatingIt()
    {
        UserMediaStore store = Store();
        store.Add(StockMedia.UserDefined("Bobina", 100, 50));

        // Same name in different case: a correction, not a second stock.
        UserMediaResult result = store.Add(StockMedia.UserDefined("BOBINA", 100, 75));

        StockMedia saved = Assert.Single(result.Entries);
        Assert.Equal(75, saved.HeightMm);
        Assert.Single(store.Load());
    }

    [Fact]
    public void Add_KeepsEntriesSortedByName()
    {
        UserMediaStore store = Store();
        store.Add(StockMedia.UserDefined("Zebrinha", 50, 30));
        store.Add(StockMedia.UserDefined("Alpha", 50, 30));
        UserMediaResult result = store.Add(StockMedia.UserDefined("Meio", 50, 30));

        Assert.Equal(["Alpha", "Meio", "Zebrinha"], result.Entries.Select(e => e.PartNumber));
    }

    [Fact]
    public void Add_WithoutAName_IsRefusedInsteadOfSavingSomethingUnfindable()
    {
        UserMediaStore store = Store();

        UserMediaResult result = store.Add(StockMedia.UserDefined("   ", 50, 30));

        Assert.NotNull(result.Error);
        Assert.Empty(result.Entries);
        Assert.False(File.Exists(PresetsPath));
    }

    [Fact]
    public void Remove_DropsOnlyTheNamedMedia()
    {
        UserMediaStore store = Store();
        store.Add(StockMedia.UserDefined("Keep", 50, 30));
        store.Add(StockMedia.UserDefined("Drop", 60, 40));

        UserMediaResult result = store.Remove("drop");

        Assert.Equal("Keep", Assert.Single(result.Entries).PartNumber);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Remove_OfSomethingAbsent_IsNotAnError()
    {
        UserMediaStore store = Store();
        store.Add(StockMedia.UserDefined("Keep", 50, 30));

        UserMediaResult result = store.Remove("never existed");

        Assert.Single(result.Entries);
        Assert.Null(result.Error);
    }

    // ---- Degrading -------------------------------------------------------------------

    [Fact]
    public void Load_WithNoFileYet_IsEmpty()
    {
        Assert.Empty(Store().Load());
    }

    [Fact]
    public void Load_OfACorruptFile_IsEmptyRatherThanAStartupFailure()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PresetsPath, "{ this is not the presets file }");

        Assert.Empty(Store().Load());
    }

    [Fact]
    public void Load_IgnoresEntriesWithNoName_WhichCouldNeverBeRemovedAgain()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PresetsPath, """
        [
          { "PartNumber": "", "Material": "", "WidthMm": 50, "HeightMm": 30, "SizeText": "50mm x 30mm" },
          { "PartNumber": "Real", "Material": "", "WidthMm": 50, "HeightMm": 30, "SizeText": "50mm x 30mm" }
        ]
        """);

        Assert.Equal("Real", Assert.Single(Store().Load()).PartNumber);
    }

    [Fact]
    public void Load_TreatsEverythingInTheFileAsTheUsersOwn()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PresetsPath, """
        [
          { "PartNumber": "Mine", "Material": "", "WidthMm": 50, "HeightMm": 30,
            "SizeText": "50mm x 30mm", "IsUserDefined": false }
        ]
        """);

        // Otherwise a hand-edited file could produce a preset the user cannot delete.
        Assert.True(Assert.Single(Store().Load()).IsUserDefined);
    }

    [Fact]
    public void Add_WhenTheFileCannotBeWritten_ReportsItAndStillReturnsTheEntry()
    {
        // A file where the directory should be: creating the folder must fail on any OS.
        Directory.CreateDirectory(_directory);
        string blocker = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocker, "not a directory");
        var store = new UserMediaStore(Path.Combine(blocker, "user-media.json"));

        UserMediaResult result = store.Add(StockMedia.UserDefined("Etiqueta", 50, 30));

        Assert.NotNull(result.Error);
        Assert.Equal("Etiqueta", Assert.Single(result.Entries).PartNumber);
    }

    // ---- Presentation and search -------------------------------------------------------

    [Theory]
    [InlineData(50.8, 30, false, "50.8mm x 30mm")]
    [InlineData(100, 150, false, "100mm x 150mm")]
    [InlineData(101.6, 999, true, "101.6mm continuous")]
    public void FormatSize_MatchesTheCatalogsShape(
        double width, double height, bool continuous, string expected)
    {
        Assert.Equal(expected, StockMedia.FormatSize(width, height, continuous));
    }

    [Fact]
    public void FormatSize_UsesADecimalPoint_OnAnyMachineCulture()
    {
        // pt-BR writes 50,8. A comma in the size text would be read as a separator by
        // anyone parsing it back, and would not match the catalog's own entries.
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            Assert.Equal("50.8mm x 25.4mm", StockMedia.FormatSize(50.8, 25.4, continuous: false));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void UserMedia_SaysSoInThePicker_SoItIsNotMistakenForAZebraPartNumber()
    {
        Assert.Equal(
            "Etiqueta 50 x 30 - Couche (50mm x 30mm) - my media",
            StockMedia.UserDefined("Etiqueta 50 x 30", 50, 30, "Couche").ToString());

        Assert.Equal(
            "Sem material (50mm x 30mm) - my media",
            StockMedia.UserDefined("Sem material", 50, 30).ToString());
    }

    [Fact]
    public void CatalogEntries_AreNeverMarkedAsTheUsersOwn()
    {
        Assert.All(StockCatalog.All, media => Assert.False(media.IsUserDefined));
        Assert.DoesNotContain("my media", StockCatalog.All[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePickersFilter_FindsUserMediaByNameAndMaterial()
    {
        StockMedia media = StockMedia.UserDefined("Etiqueta Filial", 50.8, 30, "Couche");

        // The same rule the Zebra catalog is searched with, so one box serves both.
        Assert.True(StockCatalog.IsMatch(media, "filial"));
        Assert.True(StockCatalog.IsMatch(media, "couche 50.8"));
        Assert.False(StockCatalog.IsMatch(media, "filial bopp"));
        Assert.False(StockCatalog.IsMatch(media, ""));
    }
}
