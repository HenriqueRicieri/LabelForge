using System.Reflection;

namespace LabelForge.Tests;

/// <summary>Locates the real Atak label corpus ("exemplos zpl/") used as test fixtures.</summary>
internal static class TestCorpus
{
    public static string Directory()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "exemplos zpl");
            if (System.IO.Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the 'exemplos zpl' corpus folder above the test assembly.");
    }

    public static IEnumerable<string> Files() =>
        System.IO.Directory.EnumerateFiles(Directory(), "*.*")
            .Where(p => p.EndsWith(".zpl", StringComparison.OrdinalIgnoreCase));
}
