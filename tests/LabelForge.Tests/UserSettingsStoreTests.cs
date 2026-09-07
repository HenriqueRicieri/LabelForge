using LabelForge.Core.Settings;

namespace LabelForge.Tests;

/// <summary>
/// The per-machine preferences file. Its contract is the one every store here follows:
/// reading degrades to defaults on any failure, because a settings file must never be the
/// reason the app will not open, and writing reports its failure instead, because a
/// preference that silently does not stick is worse than one that says so.
/// </summary>
public sealed class UserSettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"labelforge-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void WithNoFile_EverythingIsAtItsDefault()
    {
        UserSettings settings = new UserSettingsStore(_path).Load();

        // Snapping on, align-to-label off: a designer that does not snap is the surprise,
        // and lining several things up with each other is the commoner ask.
        Assert.False(settings.AlignToLabel);
        Assert.True(settings.SnapToGuides);
        Assert.True(settings.SnapToGrid);
        Assert.True(settings.SnapToObjects);
    }

    [Fact]
    public void WhatIsSavedComesBack()
    {
        var store = new UserSettingsStore(_path);

        Assert.Null(store.Save(new UserSettings
        {
            AlignToLabel = true,
            SnapToGrid = false,
        }));

        UserSettings restored = store.Load();
        Assert.True(restored.AlignToLabel);
        Assert.False(restored.SnapToGrid);
        Assert.True(restored.SnapToGuides);
    }

    [Fact]
    public void ARuinedFileReadsAsDefaults()
    {
        File.WriteAllText(_path, "{ this is not json");

        UserSettings settings = new UserSettingsStore(_path).Load();

        Assert.False(settings.AlignToLabel);
        Assert.True(settings.SnapToGuides);
    }

    [Fact]
    public void SavingCreatesTheDirectoryItNeeds()
    {
        string nested = Path.Combine(
            Path.GetTempPath(), $"labelforge-{Guid.NewGuid():N}", "settings.json");
        try
        {
            var store = new UserSettingsStore(nested);
            Assert.Null(store.Save(new UserSettings { AlignToLabel = true }));
            Assert.True(store.Load().AlignToLabel);
        }
        finally
        {
            string? directory = Path.GetDirectoryName(nested);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
