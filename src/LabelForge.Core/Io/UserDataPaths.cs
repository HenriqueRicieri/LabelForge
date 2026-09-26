namespace LabelForge.Core.Io;

public static class UserDataPaths
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabelForge.UserData");

    public static string FilePath(string name) => Path.Combine(DirectoryPath, name);

    public static IReadOnlyList<string> MigrateLegacy(string? localApplicationData = null)
    {
        string local = localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string legacy = Path.Combine(local, "LabelForge");
        string target = Path.Combine(local, "LabelForge.UserData");
        var errors = new List<string>();
        foreach (string name in new[] { "recent-files.json", "user-settings.json", "user-media.json", "field-catalogs.json" })
        {
            Move(Path.Combine(legacy, name), Path.Combine(target, name));
        }
        using var recovery = new RecoveryStore(Path.Combine(legacy, "recovery"));
        foreach (RecoverySnapshot snapshot in recovery.FindAbandoned())
        {
            Move(snapshot.SnapshotPath, Path.Combine(target, "recovery", Path.GetFileName(snapshot.SnapshotPath)));
        }
        return errors;

        void Move(string from, string to)
        {
            if (!File.Exists(from) || File.Exists(to))
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Could not migrate {Path.GetFileName(from)}: {exception.Message}");
            }
        }
    }
}
