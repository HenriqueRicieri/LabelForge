using System.Text.Json;

namespace LabelForge.Core.Io;

/// <param name="SnapshotPath">The file the snapshot lives in, so it can be discarded
/// once the user has decided what to do with it.</param>
/// <param name="Lfl">The document, in exactly the format a .lfl holds.</param>
/// <param name="OriginalPath">Where the document came from, or null when it had never
/// been saved. That is the case worth recovering most, since there is nothing else.</param>
/// <param name="SavedAtUtc">When the snapshot was taken, so the offer can say how much
/// work is in it rather than asking blind.</param>
public sealed record RecoverySnapshot(
    string SnapshotPath,
    string Lfl,
    string? OriginalPath,
    DateTime SavedAtUtc);

/// <summary>
/// Periodic snapshots of the open document, so a crash costs the last few seconds rather
/// than the afternoon.
///
/// The whole design rests on one idea: the snapshot file's *existence* is the crash
/// signal. A session that ends properly deletes its own file, so anything left behind
/// belongs to a session that did not. That is why <see cref="Clear"/> matters as much as
/// <see cref="Save"/>, and why saving the document to a real file clears it too: the work
/// is safe elsewhere and a stale offer on next start would be a false alarm.
///
/// Each session owns its own snapshot and holds a lock beside it for as long as it runs.
/// Without that, a second window would find the first one's live snapshot and offer to
/// recover a document nobody lost. A lock that can be taken means its owner is gone.
///
/// Nothing here is allowed to break the app. Writing reports its failure rather than
/// throwing, because a snapshot that cannot be written is a smaller problem than an
/// editor that falls over while someone is working; reading degrades to no offer at all,
/// since a corrupt snapshot is indistinguishable from no snapshot as far as the user's
/// next action goes.
/// </summary>
public sealed class RecoveryStore : IDisposable
{
    private const string SnapshotExtension = ".recovery.json";
    private const string LockExtension = ".lock";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private sealed record Envelope(string? OriginalPath, string Lfl);

    private readonly string _directory;
    private readonly string _snapshotPath;
    private readonly string _lockPath;
    private FileStream? _lock;
    private bool _disposed;

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelForge", "recovery");

    /// <param name="directory">Override for tests; defaults to the per-user location.</param>
    /// <param name="sessionId">Names this session's files. Defaults to a fresh identity,
    /// which is what makes two windows independent.</param>
    public RecoveryStore(string? directory = null, string? sessionId = null)
    {
        _directory = directory ?? DefaultDirectory;
        string id = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
        _snapshotPath = Path.Combine(_directory, id + SnapshotExtension);
        _lockPath = Path.Combine(_directory, id + LockExtension);
    }

    public string SnapshotPath => _snapshotPath;

    /// <summary>
    /// Writes the current document over this session's snapshot.
    /// </summary>
    /// <returns>Why it could not be written, or null. The caller keeps working either
    /// way; all that is lost is the safety net, and saying so once is better than saying
    /// nothing or saying it every thirty seconds.</returns>
    public string? Save(string lfl, string? originalPath)
    {
        ArgumentNullException.ThrowIfNull(lfl);
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            Directory.CreateDirectory(_directory);
            TakeLock();
            File.WriteAllText(
                _snapshotPath,
                JsonSerializer.Serialize(new Envelope(originalPath, lfl), Options));
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Drops this session's snapshot, which is what says the work is not lost.
    ///
    /// Called on a clean exit and on every real save. The second is the one that is easy
    /// to forget: a document written to its own file is safe, and leaving the snapshot
    /// behind would greet the next start with an offer to recover something nobody lost.
    /// </summary>
    public void Clear()
    {
        try
        {
            if (File.Exists(_snapshotPath))
            {
                File.Delete(_snapshotPath);
            }
        }
        catch (Exception)
        {
            // A snapshot that cannot be deleted becomes a false offer next time, which is
            // a nuisance rather than a loss, and there is nothing useful to do about it.
        }
    }

    /// <summary>
    /// Snapshots left by sessions that are no longer running, newest first.
    ///
    /// A snapshot whose lock cannot be taken belongs to a window that is still open, so
    /// it is somebody's current work rather than a crash remnant and is left alone.
    /// </summary>
    public IReadOnlyList<RecoverySnapshot> FindAbandoned()
    {
        var found = new List<RecoverySnapshot>();
        try
        {
            if (!Directory.Exists(_directory))
            {
                return found;
            }

            foreach (string path in Directory.GetFiles(_directory, "*" + SnapshotExtension))
            {
                if (string.Equals(path, _snapshotPath, StringComparison.OrdinalIgnoreCase) ||
                    !IsAbandoned(path))
                {
                    continue;
                }

                if (Read(path) is { } snapshot)
                {
                    found.Add(snapshot);
                }
            }
        }
        catch (Exception)
        {
            return found;
        }

        return found.OrderByDescending(s => s.SavedAtUtc).ToArray();
    }

    /// <summary>Removes a snapshot the user has finished with, whether it was recovered
    /// or thrown away. Both are decisions; only an undecided one is worth keeping.</summary>
    public void Discard(string snapshotPath)
    {
        try
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }

            string owner = snapshotPath[..^SnapshotExtension.Length] + LockExtension;
            if (File.Exists(owner))
            {
                File.Delete(owner);
            }
        }
        catch (Exception)
        {
            // Same reasoning as Clear: a leftover file is a nuisance, never a loss.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
        _lock?.Dispose();
        _lock = null;

        try
        {
            if (File.Exists(_lockPath))
            {
                File.Delete(_lockPath);
            }
        }
        catch (Exception)
        {
            // The lock is only meaningful while this process lives; a stale one is taken
            // by the next start, which is exactly how abandonment is detected.
        }
    }

    private void TakeLock() =>
        _lock ??= new FileStream(
            _lockPath, FileMode.Create, FileAccess.Write, FileShare.Read);

    /// <summary>True when nothing holds the snapshot's lock, which means the session that
    /// wrote it is gone. A snapshot with no lock file at all counts as abandoned: the
    /// process died before it could be created, and the work still deserves offering.</summary>
    private static bool IsAbandoned(string snapshotPath)
    {
        string lockPath = snapshotPath[..^SnapshotExtension.Length] + LockExtension;
        if (!File.Exists(lockPath))
        {
            return true;
        }

        try
        {
            using var _ = new FileStream(
                lockPath, FileMode.Open, FileAccess.Write, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static RecoverySnapshot? Read(string path)
    {
        try
        {
            Envelope? envelope =
                JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path), Options);

            return string.IsNullOrWhiteSpace(envelope?.Lfl)
                ? null
                : new RecoverySnapshot(
                    path, envelope.Lfl, envelope.OriginalPath, File.GetLastWriteTimeUtc(path));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
