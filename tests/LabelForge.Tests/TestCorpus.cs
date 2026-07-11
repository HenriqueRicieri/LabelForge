namespace LabelForge.Tests;

/// <summary>
/// Locates ZPL test fixtures. Two sources feed the corpus tests:
/// 1. A committed, publishable synthetic set under "Fixtures/" (copied next to the
///    test assembly). Always present, so a clean clone and CI have real data to run.
/// 2. The real Atak corpus in "exemplos zpl/", which is local-only (gitignored) and
///    optional. Included when found so local runs still exercise real input.
/// Neither source throws when absent; the fixtures guarantee a non-empty corpus.
/// </summary>
internal static class TestCorpus
{
    /// <summary>Committed synthetic fixtures, copied beside the test assembly.</summary>
    public static string FixturesDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>The optional local-only Atak corpus, or null when it is not present.</summary>
    public static string? LocalCorpusDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "exemplos zpl");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    public static IEnumerable<string> Files()
    {
        var directories = new List<string>();
        if (Directory.Exists(FixturesDirectory()))
        {
            directories.Add(FixturesDirectory());
        }

        if (LocalCorpusDirectory() is { } local)
        {
            directories.Add(local);
        }

        foreach (var directory in directories)
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.*")
                         .Where(p => p.EndsWith(".zpl", StringComparison.OrdinalIgnoreCase)))
            {
                yield return path;
            }
        }
    }
}
