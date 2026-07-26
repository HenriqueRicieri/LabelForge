using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// Crash recovery. The whole thing rests on one idea: the snapshot file's existence is
/// the crash signal, so a session that ends properly has to leave nothing behind.
/// </summary>
public sealed class RecoveryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"lf-recovery-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private static string Lfl(string text)
    {
        var document = new LabelDocument { WidthMm = 80, HeightMm = 40, Dpmm = 8 };
        document.Elements.Add(new TextElement { X = 10, Y = 10, Text = text });
        return LabelDocumentJson.Serialize(document);
    }

    /// <summary>A session that died leaves its snapshot behind, and that is what makes it
    /// findable: nothing else records that the crash happened.</summary>
    [Fact]
    public void ASnapshotFromADeadSession_IsOffered()
    {
        CrashRemnant("crashed", Lfl("work in progress"), @"C:\labels\pedido.lfl");

        using var next = new RecoveryStore(_directory, "next");
        RecoverySnapshot found = Assert.Single(next.FindAbandoned());

        Assert.Contains("work in progress", found.Lfl, StringComparison.Ordinal);
        Assert.Equal(@"C:\labels\pedido.lfl", found.OriginalPath);
        Assert.Equal(
            "work in progress",
            Assert.IsType<TextElement>(
                Assert.Single(LabelDocumentJson.Deserialize(found.Lfl).Elements)).Text);
    }

    /// <summary>The case worth recovering most: a document that was never saved anywhere,
    /// so the snapshot is the only copy that exists.</summary>
    [Fact]
    public void ADocumentThatWasNeverSaved_IsStillOffered()
    {
        CrashRemnant("crashed", Lfl("untitled"));

        using var next = new RecoveryStore(_directory, "next");

        Assert.Null(Assert.Single(next.FindAbandoned()).OriginalPath);
    }

    /// <summary>A clean exit leaves nothing, so the next start says nothing. Without this
    /// every ordinary shutdown would look like a crash.</summary>
    [Fact]
    public void ASessionThatEndedProperly_LeavesNothingToOffer()
    {
        var tidy = new RecoveryStore(_directory, "tidy");
        tidy.Save(Lfl("finished"), null);
        tidy.Dispose();

        using var next = new RecoveryStore(_directory, "next");

        Assert.Empty(next.FindAbandoned());
    }

    /// <summary>Saving the document to its own file makes the snapshot a false alarm, so
    /// the caller clears it and the next start stays quiet.</summary>
    [Fact]
    public void ClearingAfterARealSave_RemovesTheOffer()
    {
        var session = new RecoveryStore(_directory, "session");
        session.Save(Lfl("saved elsewhere"), @"C:\labels\a.lfl");
        session.Clear();
        session.Dispose();

        using var next = new RecoveryStore(_directory, "next");

        Assert.Empty(next.FindAbandoned());
    }

    /// <summary>
    /// A second window must not offer to recover the first window's live work. The lock
    /// is what tells them apart: one that cannot be taken belongs to a session still
    /// running.
    /// </summary>
    [Fact]
    public void AnotherRunningSessionsWork_IsNotOffered()
    {
        using var live = new RecoveryStore(_directory, "live");
        live.Save(Lfl("still being edited"), null);

        using var other = new RecoveryStore(_directory, "other");

        Assert.Empty(other.FindAbandoned());
    }

    /// <summary>And a session never offers its own snapshot back to itself.</summary>
    [Fact]
    public void ASessionIgnoresItsOwnSnapshot()
    {
        using var session = new RecoveryStore(_directory, "session");
        session.Save(Lfl("mine"), null);

        Assert.Empty(session.FindAbandoned());
    }

    [Fact]
    public void ARecoveredSnapshot_IsNotOfferedTwice()
    {
        CrashRemnant("crashed", Lfl("once"));

        using var next = new RecoveryStore(_directory, "next");
        RecoverySnapshot found = Assert.Single(next.FindAbandoned());
        next.Discard(found.SnapshotPath);

        Assert.Empty(next.FindAbandoned());
    }

    /// <summary>The newest first, because after several crashes the last one is the work
    /// somebody actually wants back.</summary>
    [Fact]
    public void SeveralSnapshots_AreOfferedNewestFirst()
    {
        foreach (string id in new[] { "older", "newer" })
        {
            CrashRemnant(id, Lfl(id));

            // The timestamp comes from the file, whose resolution is coarse enough to
            // need separating deliberately rather than by luck.
            File.SetLastWriteTimeUtc(
                Path.Combine(_directory, id + ".recovery.json"),
                id == "older" ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                              : new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        using var next = new RecoveryStore(_directory, "next");
        IReadOnlyList<RecoverySnapshot> found = next.FindAbandoned();

        Assert.Equal(2, found.Count);
        Assert.Contains("newer", found[0].Lfl, StringComparison.Ordinal);
    }

    /// <summary>A snapshot that cannot be read is the same as no snapshot as far as the
    /// next action goes, and must never stop the app from starting.</summary>
    [Fact]
    public void ACorruptSnapshot_IsIgnoredRatherThanThrown()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "broken.recovery.json"), "{ not json");

        using var next = new RecoveryStore(_directory, "next");

        Assert.Empty(next.FindAbandoned());
    }

    /// <summary>Nothing to recover from is the ordinary case and must be silent.</summary>
    [Fact]
    public void AMissingDirectory_IsNotAFailure() =>
        Assert.Empty(
            new RecoveryStore(Path.Combine(_directory, "never-created"), "next").FindAbandoned());

    /// <summary>Writing failures are reported, not thrown: losing the safety net is a
    /// smaller problem than an editor that falls over while someone is working.</summary>
    [Fact]
    public void AnUnwritableLocation_IsReportedRatherThanThrown()
    {
        // A path with a file where the directory should be cannot be created.
        Directory.CreateDirectory(_directory);
        string blocker = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocker, "not a directory");

        using var store = new RecoveryStore(Path.Combine(blocker, "inside"), "session");

        Assert.NotNull(store.Save(Lfl("x"), null));
    }

    /// <summary>
    /// Produces what a crashed session leaves on disk: its snapshot, and no lock, because
    /// the operating system releases the lock when the process dies but nothing deletes
    /// the snapshot.
    ///
    /// Built from what Save actually writes rather than hand-rolled, so the format stays
    /// under test, and without any test-only hook on the store: a real session writes it,
    /// ends cleanly, and the bytes are put back under a name nothing holds.
    /// </summary>
    private void CrashRemnant(string id, string lfl, string? originalPath = null)
    {
        string written;
        using (var session = new RecoveryStore(_directory, id + "-live"))
        {
            Assert.Null(session.Save(lfl, originalPath));
            written = File.ReadAllText(session.SnapshotPath);
        }

        File.WriteAllText(Path.Combine(_directory, id + ".recovery.json"), written);
    }
}
