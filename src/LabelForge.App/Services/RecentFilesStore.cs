using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using LabelForge.Core.Io;

namespace LabelForge.App.Services;

/// <summary>
/// Persists the recently opened .lfl paths (newest first, capped) in the user's
/// local application data. Read and write failures degrade to an empty list: the
/// recent menu is a convenience and must never block opening the app.
/// </summary>
public static class RecentFilesStore
{
    private const int MaxEntries = 10;

    private static string FilePath => UserDataPaths.FilePath("recent-files.json");

    public static IReadOnlyList<string> Load(string? storagePath = null)
    {
        try
        {
            string file = storagePath ?? FilePath;
            return File.Exists(file)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file)) ?? []
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static IReadOnlyList<string> Add(string path, string? storagePath = null)
    {
        List<string> entries = Load(storagePath).Where(e => !PathEquals(e, path)).ToList();
        entries.Insert(0, path);
        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        Save(entries, storagePath);
        return entries;
    }

    public static IReadOnlyList<string> Remove(string path, string? storagePath = null)
    {
        List<string> entries = Load(storagePath).Where(e => !PathEquals(e, path)).ToList();
        Save(entries, storagePath);
        return entries;
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void Save(List<string> entries, string? storagePath)
    {
        try
        {
            string file = storagePath ?? FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(entries));
        }
        catch (Exception)
        {
            // Losing the list is acceptable; failing a save or open is not.
        }
    }
}
