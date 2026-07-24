using System.Text.Json;

namespace LabelForge.Core.Media;

/// <summary>The result of changing the saved presets.</summary>
/// <param name="Entries">The presets after the change, sorted by name.</param>
/// <param name="Error">Why the change could not be persisted, or null on success. The
/// in-memory list is still correct either way, so the UI stays usable and only has to
/// tell the user their preset will not survive a restart.</param>
public readonly record struct UserMediaResult(IReadOnlyList<StockMedia> Entries, string? Error);

/// <summary>
/// The user's own media definitions, stored per machine in local application data.
/// Most print shops run third-party stock whose dimensions are perfectly well known
/// but which no Zebra part number describes, so the designer's picker searches these
/// alongside the official catalog.
///
/// Reading degrades to an empty list on any failure: a missing, unreadable, or corrupt
/// presets file must never stop the app from opening. Writing reports its failure
/// instead, because silently dropping a preset the user just asked to save is a
/// different thing from starting out with none.
/// </summary>
public sealed class UserMediaStore
{
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelForge", "user-media.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    /// <param name="filePath">Override for tests; defaults to the per-user location.</param>
    public UserMediaStore(string? filePath = null) => _path = filePath ?? DefaultFilePath;

    public string FilePath => _path;

    public IReadOnlyList<StockMedia> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            List<StockMedia>? entries =
                JsonSerializer.Deserialize<List<StockMedia>>(File.ReadAllText(_path), Options);
            if (entries is null)
            {
                return [];
            }

            // Everything in this file is the user's, whatever the file claims, and an
            // entry with no name could never be found or removed again.
            return entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.PartNumber))
                .Select(entry => entry with { IsUserDefined = true })
                .OrderBy(entry => entry.PartNumber, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Saves a preset, replacing any existing one of the same name so a
    /// correction updates the definition instead of leaving two that differ.</summary>
    public UserMediaResult Add(StockMedia media)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (string.IsNullOrWhiteSpace(media.PartNumber))
        {
            return new UserMediaResult(Load(), "A media needs a name.");
        }

        List<StockMedia> entries = Load()
            .Where(entry => !NameEquals(entry.PartNumber, media.PartNumber))
            .ToList();
        entries.Add(media with { IsUserDefined = true });
        return Write(entries);
    }

    public UserMediaResult Remove(string name)
    {
        List<StockMedia> entries = Load()
            .Where(entry => !NameEquals(entry.PartNumber, name))
            .ToList();
        return Write(entries);
    }

    private UserMediaResult Write(List<StockMedia> entries)
    {
        entries.Sort((a, b) => string.Compare(a.PartNumber, b.PartNumber, StringComparison.OrdinalIgnoreCase));
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(entries, Options));
            return new UserMediaResult(entries, null);
        }
        catch (Exception ex)
        {
            return new UserMediaResult(entries, ex.Message);
        }
    }

    private static bool NameEquals(string a, string b) =>
        string.Equals(a.Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
}
