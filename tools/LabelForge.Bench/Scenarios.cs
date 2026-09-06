using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Bench;

/// <summary>What gets timed. One synthetic label at the three densities the app offers,
/// so a clean clone measures something, plus the heaviest real labels when the local-only
/// corpus is present, because a real label is what the designer is slow on.</summary>
internal sealed record Scenario(string Name, LabelDocument Document);

internal static class Scenarios
{
    /// <summary>The synthetic label at 203, 300 and 600 dpi, then the corpus labels with
    /// the most elements. Chosen by element count rather than by name: the corpus is a
    /// customer's and its file names do not belong in committed source.</summary>
    public static List<Scenario> Build(int corpusCount)
    {
        var scenarios = new List<Scenario>();
        foreach (int dpmm in new[] { 8, 12, 24 })
        {
            LabelDocument synthetic = Synthetic(dpmm);
            scenarios.Add(new Scenario(
                $"synthetic {synthetic.Elements.Count} elements, "
                    + $"{synthetic.WidthMm:0}x{synthetic.HeightMm:0} mm @ {dpmm} dpmm",
                synthetic));
        }

        if (corpusCount > 0 && LocalCorpusDirectory() is { } corpus)
        {
            var imported = new List<Scenario>();
            foreach (string path in Directory.EnumerateFiles(corpus, "*.*")
                         .Where(p => p.EndsWith(".zpl", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                LabelDocument document;
                try
                {
                    document = ZplDocumentImport.FromZpl(ZplTextFile.ReadFile(path).Text, 8).Document;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"skipped {Path.GetFileName(path)}: {ex.Message}");
                    continue;
                }

                if (document.Elements.Count == 0)
                {
                    continue;
                }

                imported.Add(new Scenario(
                    $"{Path.GetFileName(path)}, {document.Elements.Count} elements @ 8", document));
            }

            scenarios.AddRange(imported
                .OrderByDescending(s => s.Document.Elements.Count)
                .Take(corpusCount));
        }

        return scenarios;
    }

    /// <summary>The local-only Atak corpus, or null when it is absent. Walks up from the
    /// binary the way the test project's TestCorpus does, so nothing hard-codes a path.</summary>
    private static string? LocalCorpusDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "exemplos zpl");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>A 100x150 mm label with forty elements: the mix a real label has, which
    /// is text mostly, several barcodes, a couple of 2D symbols and the boxes and rules
    /// that frame them. Sized in millimeters so the three densities draw the same picture.</summary>
    private static LabelDocument Synthetic(int dpmm)
    {
        var document = new LabelDocument { WidthMm = 100, HeightMm = 150, Dpmm = dpmm };
        int Dots(double mm) => Units.MmToDots(mm, dpmm);
        int z = 0;

        for (int i = 0; i < 20; i++)
        {
            document.Elements.Add(new TextElement
            {
                X = Dots(5 + (i % 2) * 48),
                Y = Dots(5 + (i / 2) * 6),
                Text = $"FIELD {i}: Rua das Palmeiras {482 + i}",
                FontHeightDots = Dots(3.5),
                ZOrder = z++,
            });
        }

        for (int i = 0; i < 8; i++)
        {
            document.Elements.Add(new BarcodeElement
            {
                X = Dots(5 + (i % 2) * 48),
                Y = Dots(70 + (i / 2) * 14),
                Data = $"789{1000000000 + i * 7919}",
                HeightDots = Dots(8),
                ModuleWidthDots = 2,
                ZOrder = z++,
            });
        }

        for (int i = 0; i < 3; i++)
        {
            document.Elements.Add(new QrCodeElement
            {
                X = Dots(5 + i * 30),
                Y = Dots(128),
                Data = $"https://example.test/item/{i}",
                Magnification = 4,
                ZOrder = z++,
            });
        }

        document.Elements.Add(new DataMatrixElement
        {
            X = Dots(80), Y = Dots(128), Data = "0104012345678901", ModuleSizeDots = 4, ZOrder = z++,
        });

        for (int i = 0; i < 6; i++)
        {
            document.Elements.Add(new BoxElement
            {
                X = Dots(3),
                Y = Dots(3 + i * 24),
                WidthDots = Dots(94),
                HeightDots = Dots(22),
                ThicknessDots = 2,
                ZOrder = z++,
            });
        }

        document.Elements.Add(new LineElement
        {
            X = Dots(3), Y = Dots(66), LengthDots = Dots(94), ThicknessDots = 2, ZOrder = z++,
        });
        document.Elements.Add(new LineElement
        {
            X = Dots(50), Y = Dots(3), LengthDots = Dots(60), ThicknessDots = 2,
            IsVertical = true, ZOrder = z++,
        });

        return document;
    }
}
