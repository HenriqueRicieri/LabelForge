namespace LabelForge.Core.Io;

/// <summary>
/// Writes a file so that, whatever happens during the write, the path holds either the
/// previous contents or the new ones, never a truncated mix.
///
/// File.WriteAllText opens the target truncated and then writes, so a full disk, a
/// dropped network share or a crash part way leaves an empty or half-written file where
/// the last good version was. Here the text goes to a temporary file beside the target
/// (same directory, so the same volume) and only a complete file replaces it.
/// </summary>
public static class AtomicFile
{
    /// <summary>Writes UTF-8 without a byte order mark, as File.WriteAllText does.</summary>
    public static void WriteAllText(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        string temporary = TemporaryFor(path);
        try
        {
            File.WriteAllText(temporary, contents);
            Commit(temporary, path);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <inheritdoc cref="WriteAllText"/>
    public static async Task WriteAllTextAsync(
        string path, string contents, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        string temporary = TemporaryFor(path);
        try
        {
            await File.WriteAllTextAsync(temporary, contents, cancellationToken).ConfigureAwait(false);
            Commit(temporary, path);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static string TemporaryFor(string path)
    {
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full) ?? ".";
        return Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
    }

    private static void Commit(string temporary, string path)
    {
        if (File.Exists(path))
        {
            // Replace keeps the target's attributes and permissions, which a plain move
            // would lose.
            File.Replace(temporary, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporary, path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temporary file left behind is clutter, not a loss.
        }
    }
}
