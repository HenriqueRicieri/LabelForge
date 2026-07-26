using System.Text.Json;

namespace LabelForge.Core.Fields;

/// <param name="Catalogs">The catalogs after the change, sorted by name.</param>
/// <param name="Error">Why the change could not be persisted, or null on success. The
/// in-memory list is correct either way, so the session stays usable and only has to say
/// the import will not survive a restart.</param>
public readonly record struct FieldCatalogResult(IReadOnlyList<FieldCatalog> Catalogs, string? Error);

/// <summary>
/// The field catalogs available on this machine, stored beside the media presets in
/// local application data.
///
/// Per machine rather than per document on purpose. A catalog describes a data source,
/// several labels are designed against the same one, and it is re-exported every so often
/// when a field is added; copying it into every .lfl would mean every label carrying a
/// stale copy. The document records only the catalog's name, so a label opened on a
/// machine that does not have it still opens, prints and exports, and simply says the
/// catalog is not installed.
///
/// Reading degrades to an empty list on any failure: a corrupt catalogs file must never
/// stop the app from opening. Writing reports its failure instead, because silently
/// dropping an import the user just asked for is a different thing from starting with
/// none.
/// </summary>
public sealed class FieldCatalogStore
{
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelForge", "field-catalogs.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    /// <param name="filePath">Override for tests; defaults to the per-user location.</param>
    public FieldCatalogStore(string? filePath = null) => _path = filePath ?? DefaultFilePath;

    public string FilePath => _path;

    public IReadOnlyList<FieldCatalog> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            List<FieldCatalog>? entries =
                JsonSerializer.Deserialize<List<FieldCatalog>>(File.ReadAllText(_path), Options);

            return entries is null
                ? []
                : entries
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                    .Select(c => c with { Fields = c.Fields ?? [] })
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Saves a catalog, replacing any existing one of the same name. Replacing
    /// is what re-importing means: the export gained a field and this is the same
    /// catalog, not a second one that disagrees with the first.</summary>
    public FieldCatalogResult Add(FieldCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(catalog.Name))
        {
            return new FieldCatalogResult(Load(), "A catalog needs a name.");
        }

        if (catalog.Fields.Count == 0)
        {
            return new FieldCatalogResult(
                Load(), "No fields were found in that file, so there is nothing to save.");
        }

        List<FieldCatalog> entries = Load()
            .Where(c => !NameEquals(c.Name, catalog.Name))
            .ToList();
        entries.Add(catalog with { Name = catalog.Name.Trim() });
        return Write(entries);
    }

    public FieldCatalogResult Remove(string name)
    {
        List<FieldCatalog> entries = Load().Where(c => !NameEquals(c.Name, name)).ToList();
        return Write(entries);
    }

    private FieldCatalogResult Write(List<FieldCatalog> entries)
    {
        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(entries, Options));
            return new FieldCatalogResult(entries, null);
        }
        catch (Exception ex)
        {
            return new FieldCatalogResult(entries, ex.Message);
        }
    }

    private static bool NameEquals(string a, string b) =>
        string.Equals(a.Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
}
