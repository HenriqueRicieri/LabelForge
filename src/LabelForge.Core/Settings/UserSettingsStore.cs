using System.Text.Json;

namespace LabelForge.Core.Settings;

/// <summary>
/// Preferences that belong to the person rather than to the label. Nothing here is ever
/// saved into a .lfl or reaches the ZPL: a label opened on another machine must behave the
/// same whatever that machine snaps to.
/// </summary>
public sealed record UserSettings
{
    /// <summary>Align and distribute against the label rather than within the selection,
    /// which is Photoshop's align-to-canvas. Off by default, because aligning several
    /// elements to each other is the commoner ask and is what this app has always done.
    /// </summary>
    public bool AlignToLabel { get; init; }

    /// <summary>Snapping, each on by default: a designer that does not snap is the surprise,
    /// and Alt is still the momentary way out of all three.</summary>
    public bool SnapToGuides { get; init; } = true;

    public bool SnapToGrid { get; init; } = true;

    public bool SnapToObjects { get; init; } = true;
}

/// <summary>
/// The settings above, stored per machine in local application data, following
/// <see cref="LabelForge.Core.Media.UserMediaStore"/> in every respect that matters.
///
/// Reading degrades to the defaults on any failure: a missing, unreadable or corrupt
/// settings file must never stop the app from opening, and every value in it has a sensible
/// default anyway. Writing reports its failure instead, because a preference silently not
/// sticking is worse than one that says it did not.
/// </summary>
public sealed class UserSettingsStore
{
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelForge", "user-settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    /// <param name="filePath">Override for tests and the harness; defaults to the per-user
    /// location. Every per-machine store here is injectable for the same reason: a test run
    /// that writes to the real file overwrites what the user has saved.</param>
    public UserSettingsStore(string? filePath = null) => _path = filePath ?? DefaultFilePath;

    public string FilePath => _path;

    public UserSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path), Options)
                  ?? new UserSettings()
                : new UserSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UserSettings();
        }
    }

    /// <returns>Why the settings could not be written, or null on success.</returns>
    public string? Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }
}
