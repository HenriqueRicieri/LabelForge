using LabelForge.Core.Fields;
using LabelForge.Core.Io;
using LabelForge.Core.Media;
using LabelForge.Core.Settings;

namespace LabelForge.Tests;

public sealed class UserDataPathsTests : IDisposable
{
    private readonly string _local = Path.Combine(Path.GetTempPath(), $"lf-user-data-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_local))
        {
            Directory.Delete(_local, recursive: true);
        }
    }

    [Theory]
    [InlineData("media")]
    [InlineData("catalogs")]
    [InlineData("settings")]
    [InlineData("recovery")]
    public void DefaultStoresStayOutsideInstallerDirectory(string store)
    {
        string path = store switch
        {
            "media" => UserMediaStore.DefaultFilePath,
            "catalogs" => FieldCatalogStore.DefaultFilePath,
            "settings" => UserSettingsStore.DefaultFilePath,
            _ => RecoveryStore.DefaultDirectory,
        };
        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabelForge.UserData");
        Assert.Equal(dataDirectory, Path.GetDirectoryName(path));
    }

    [Fact]
    public void MigratedDataSurvivesRemovalOfInstallerDirectory()
    {
        string legacy = Path.Combine(_local, "LabelForge");
        Directory.CreateDirectory(legacy);
        foreach (string name in new[] { "recent-files.json", "user-settings.json", "user-media.json", "field-catalogs.json" })
        {
            File.WriteAllText(Path.Combine(legacy, name), $"synthetic {name}");
        }
        File.WriteAllText(Path.Combine(legacy, "Update.exe"), "installer binary");

        Assert.Empty(UserDataPaths.MigrateLegacy(_local));
        Directory.Delete(legacy, recursive: true);

        foreach (string name in new[] { "recent-files.json", "user-settings.json", "user-media.json", "field-catalogs.json" })
        {
            Assert.Equal($"synthetic {name}", File.ReadAllText(Path.Combine(_local, "LabelForge.UserData", name)));
        }
        Assert.False(File.Exists(Path.Combine(_local, "LabelForge.UserData", "Update.exe")));
        Assert.Empty(UserDataPaths.MigrateLegacy(_local));
    }

    [Fact]
    public void MigrationDoesNotReplaceNewerData()
    {
        string legacy = Path.Combine(_local, "LabelForge");
        string current = Path.Combine(_local, "LabelForge.UserData");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "user-settings.json"), "older");
        File.WriteAllText(Path.Combine(current, "user-settings.json"), "newer");

        Assert.Empty(UserDataPaths.MigrateLegacy(_local));

        Assert.Equal("newer", File.ReadAllText(Path.Combine(current, "user-settings.json")));
        Assert.Equal("older", File.ReadAllText(Path.Combine(legacy, "user-settings.json")));
    }

    [Fact]
    public void MigrationMovesAbandonedRecoveryButLeavesLiveSessions()
    {
        string recoveryDirectory = Path.Combine(_local, "LabelForge", "recovery");
        string envelope;
        using (var fixture = new RecoveryStore(recoveryDirectory, "fixture"))
        {
            Assert.Null(fixture.Save("synthetic label", null));
            envelope = File.ReadAllText(fixture.SnapshotPath);
        }
        string abandoned = Path.Combine(recoveryDirectory, "abandoned.recovery.json");
        File.WriteAllText(abandoned, envelope);
        File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddDays(-1));
        DateTime savedAt = File.GetLastWriteTimeUtc(abandoned);
        using var live = new RecoveryStore(recoveryDirectory, "live");
        Assert.Null(live.Save("active synthetic label", null));

        Assert.Empty(UserDataPaths.MigrateLegacy(_local));
        string migrated = Path.Combine(_local, "LabelForge.UserData", "recovery", "abandoned.recovery.json");
        Assert.Equal(envelope, File.ReadAllText(migrated));
        Assert.Equal(savedAt, File.GetLastWriteTimeUtc(migrated));
        Assert.False(File.Exists(abandoned));
        Assert.True(File.Exists(live.SnapshotPath));
        Assert.False(File.Exists(Path.Combine(_local, "LabelForge.UserData", "recovery", "live.recovery.json")));
    }

    [Fact]
    public void FailedMigrationPreservesOriginalAndReportsError()
    {
        string legacy = Path.Combine(_local, "LabelForge");
        Directory.CreateDirectory(legacy);
        string file = Path.Combine(legacy, "recent-files.json");
        File.WriteAllText(file, "synthetic recent files");
        File.WriteAllText(Path.Combine(_local, "LabelForge.UserData"), "blocks target directory");

        Assert.Single(UserDataPaths.MigrateLegacy(_local));
        Assert.Equal("synthetic recent files", File.ReadAllText(file));
    }
}
