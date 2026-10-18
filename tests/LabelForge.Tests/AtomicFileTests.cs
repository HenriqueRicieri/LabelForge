using LabelForge.Core.Io;

namespace LabelForge.Tests;

/// <summary>
/// A save that fails part way must leave the previous file intact. Truncation by a full
/// disk cannot be produced in a test; what can be checked is that the target is only
/// ever replaced by a complete file, and that a failed write leaves the old contents and
/// no temporary file behind.
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "lf-atomic-" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void WritesANewFile()
    {
        string path = Path.Combine(_directory, "label.lfl");

        AtomicFile.WriteAllText(path, "new");

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task ReplacesAnExistingFileWithoutLeavingATemporaryOne()
    {
        string path = Path.Combine(_directory, "label.lfl");
        File.WriteAllText(path, "old");

        await AtomicFile.WriteAllTextAsync(path, "new");

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void WritesUtf8WithoutAByteOrderMark()
    {
        string path = Path.Combine(_directory, "label.lfl");

        AtomicFile.WriteAllText(path, "Ação");

        Assert.Equal("Ação"u8.ToArray(), File.ReadAllBytes(path));
    }

    /// <summary>A target someone else holds open cannot be replaced; the old contents stay
    /// and the temporary file is cleaned up.</summary>
    [Fact]
    public void AFailedReplaceKeepsTheOldContents()
    {
        string path = Path.Combine(_directory, "label.lfl");
        File.WriteAllText(path, "old");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => AtomicFile.WriteAllText(path, "new"));
        }

        Assert.Equal("old", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_directory));
    }
}
