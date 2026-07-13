using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LabelForge.App.Services;

/// <summary>
/// Persists the recently opened .lfl paths (newest first, capped) in the user's
/// local application data. Read and write failures degrade to an empty list: the
/// recent menu is a convenience and must never block opening the app.
/// </summary>
public static class RecentFilesStore
{
    private const int MaxEntries = 10;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelForge", "recent-files.json");

    public static IReadOnlyList<string> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static IReadOnlyList<string> Add(string path)
    {
        List<string> entries = Load().Where(e => !PathEquals(e, path)).ToList();
        entries.Insert(0, path);
        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        Save(entries);
        return entries;
    }

    public static IReadOnlyList<string> Remove(string path)
    {
        List<string> entries = Load().Where(e => !PathEquals(e, path)).ToList();
        Save(entries);
        return entries;
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void Save(List<string> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries));
        }
        catch (Exception)
        {
            // Losing the list is acceptable; failing a save or open is not.
        }
    }
}
