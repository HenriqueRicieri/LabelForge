using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// Repeating an image on a label should cost the payload once. These cover the rule
/// (one placement stays inline, two or more share a download), the ordering ~DG needs,
/// what a multi-copy run does, and the part that could silently break the designer:
/// the canvas underlay has to draw the shared form exactly like the inline one.
/// </summary>
public sealed class GraphicReuseTests
{
    private static ImageElement Stamp(int x, int y, byte[]? data = null) => new()
    {
        ImageData = data ?? TestImages.HalfBlackPng(),
        SourcePixelWidth = 8,
        SourcePixelHeight = 1,
        WidthDots = 32,
        HeightDots = 16,
        Dithering = DitherMode.Threshold,
        X = x,
        Y = y,
    };

    private static LabelDocument Label(params Element[] elements)
    {
        var document = new LabelDocument { WidthMm = 60, HeightMm = 40, Dpmm = 8 };
        foreach (Element element in elements)
        {
            document.Elements.Add(element);
        }

        return document;
    }

    [Fact]
    public void OneImage_StaysInlineAndTouchesNoPrinterMemory()
    {
        string zpl = new ZplGenerator().Generate(Label(Stamp(10, 10)));

        Assert.Contains("^GFA,", zpl, StringComparison.Ordinal);
        Assert.DoesNotContain("~DG", zpl, StringComparison.Ordinal);
        Assert.DoesNotContain("^XG", zpl, StringComparison.Ordinal);
        Assert.StartsWith("^XA", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedImage_DownloadsOnceAndRecallsPerPlacement()
    {
        string zpl = new ZplGenerator().Generate(Label(Stamp(10, 10), Stamp(10, 200), Stamp(300, 10)));

        Assert.Equal(1, Occurrences(zpl, "~DG"));
        Assert.Equal(3, Occurrences(zpl, "^XGR:LFG0.GRF,1,1"));
        Assert.DoesNotContain("^GFA,", zpl, StringComparison.Ordinal);

        // The printer has to receive the download before the block that recalls it.
        Assert.True(zpl.IndexOf("~DG", StringComparison.Ordinal) < zpl.IndexOf("^XA", StringComparison.Ordinal));
        Assert.Contains("^FO10,200^XGR:LFG0.GRF,1,1^FS", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void DifferentImages_DoNotShareADownload()
    {
        byte[] other = TestImages.SolidPng(8, 8, 0, 0, 0);
        string zpl = new ZplGenerator().Generate(
            Label(Stamp(10, 10), Stamp(10, 100), Stamp(200, 10, other), Stamp(200, 100, other)));

        Assert.Equal(2, Occurrences(zpl, "~DG"));
        Assert.Equal(2, Occurrences(zpl, "^XGR:LFG0.GRF,1,1"));
        Assert.Equal(2, Occurrences(zpl, "^XGR:LFG1.GRF,1,1"));
    }

    [Fact]
    public void SameBytesAtDifferentSizes_AreDifferentGraphics()
    {
        ImageElement bigger = Stamp(200, 10);
        bigger.WidthDots = 64;

        // Two placements each, but the two sizes rasterize to different bits.
        string zpl = new ZplGenerator().Generate(
            Label(Stamp(10, 10), Stamp(10, 100), bigger, Stamp(200, 100)));

        // The 32-wide stamp is placed three times and shares; the 64-wide one is alone.
        Assert.Equal(1, Occurrences(zpl, "~DG"));
        Assert.Equal(3, Occurrences(zpl, "^XGR:LFG0.GRF,1,1"));
        Assert.Equal(1, Occurrences(zpl, "^GFA,"));
    }

    [Fact]
    public void Naming_IsStableAcrossRuns()
    {
        LabelDocument document = Label(Stamp(10, 10), Stamp(10, 200));

        Assert.Equal(
            new ZplGenerator().Generate(document, new GenerationContext()),
            new ZplGenerator().Generate(document, new GenerationContext()));
    }

    [Fact]
    public void UndecodableRepeatedImage_EmitsNothingInsteadOfADanglingRecall()
    {
        byte[] garbage = [1, 2, 3, 4];
        string zpl = new ZplGenerator().Generate(Label(Stamp(10, 10, garbage), Stamp(10, 200, garbage)));

        Assert.DoesNotContain("~DG", zpl, StringComparison.Ordinal);
        Assert.DoesNotContain("^XG", zpl, StringComparison.Ordinal);
        Assert.DoesNotContain("^GFA", zpl, StringComparison.Ordinal);
    }

    /// <summary>A run numbered by the PC sends one block per copy. The download belongs
    /// to the first block only; the graphic is still in memory for the rest of the
    /// stream, and repeating it per copy is what this whole feature exists to avoid.</summary>
    [Fact]
    public void MultiCopyRun_DownloadsOnlyInTheFirstBlock()
    {
        LabelDocument document = Label(
            Stamp(10, 10),
            Stamp(10, 200),
            new TextElement { Text = "##N##", X = 10, Y = 300, FontHeightDots = 30 });
        document.Variables["N"] = new VariableDefinition
        {
            Kind = VariableKind.Counter,

            // Numbered here, which is what forces one block per copy.
            UsePrinterCounter = false,
        };
        document.Print.Copies = 4;

        PrintJobResult job = PrintJob.Build(document, new DateTime(2026, 7, 25, 9, 0, 0));

        Assert.Equal(4, job.Labels);
        Assert.Equal(4, Occurrences(job.Zpl, "^XA"));
        Assert.Equal(1, Occurrences(job.Zpl, "~DG"));
        Assert.Equal(8, Occurrences(job.Zpl, "^XGR:LFG0.GRF,1,1"));
        Assert.True(
            job.Zpl.IndexOf("~DG", StringComparison.Ordinal)
            < job.Zpl.IndexOf("^XGR:LFG0.GRF", StringComparison.Ordinal));
    }

    /// <summary>Any block generated on its own has to carry its own download, or the
    /// ZPL pane and the file export would show something a printer cannot draw.</summary>
    [Fact]
    public void ASingleBlockIsAlwaysSelfContained()
    {
        LabelDocument document = Label(Stamp(10, 10), Stamp(10, 200));
        document.Print.Copies = 5;

        PrintJobResult job = PrintJob.Build(document, DateTime.Now);

        Assert.Equal(1, Occurrences(job.Zpl, "^XA"));
        Assert.Equal(1, Occurrences(job.Zpl, "~DG"));
        Assert.Contains("^PQ5", job.Zpl, StringComparison.Ordinal);
    }

    /// <summary>
    /// The canvas draws whatever the offline renderer makes of the generated ZPL. If
    /// BinaryKits placed a recalled graphic even slightly differently from an inline
    /// one, the designer would show a layout that does not print. Same document, both
    /// forms, compared pixel for pixel.
    /// </summary>
    [Fact]
    public void Renderer_DrawsARecalledGraphicExactlyLikeAnInlineOne()
    {
        var renderer = new BinaryKitsRenderer();

        // Two placements share a download; one placement each does not.
        string shared = new ZplGenerator().Generate(Label(Stamp(24, 16), Stamp(24, 120)));
        string inline = new ZplGenerator().Generate(Label(Stamp(24, 16)))
                        + new ZplGenerator().Generate(Label(Stamp(24, 120)));

        Assert.Contains("~DG", shared, StringComparison.Ordinal);
        Assert.DoesNotContain("~DG", inline, StringComparison.Ordinal);

        byte[] fromShared = Draw(renderer, shared);

        // The two single-image labels overlay to the same ink as the shared one.
        byte[] first = Draw(renderer, new ZplGenerator().Generate(Label(Stamp(24, 16))));
        byte[] second = Draw(renderer, new ZplGenerator().Generate(Label(Stamp(24, 120))));

        Assert.Equal(Overlay(first, second), fromShared);
    }

    private static byte[] Draw(BinaryKitsRenderer renderer, string zpl)
    {
        RenderResult result = renderer.Render(zpl, 60, 40, 8);
        Assert.Empty(result.Errors);
        using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
        Assert.NotNull(bitmap);
        return bitmap.GetPixelSpan().ToArray();
    }

    /// <summary>Darker pixel wins, which is what two black-on-white fields on one label
    /// come to when they do not overlap.</summary>
    private static byte[] Overlay(byte[] a, byte[] b)
    {
        Assert.Equal(a.Length, b.Length);
        var result = new byte[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            result[i] = Math.Min(a[i], b[i]);
        }

        return result;
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0;
        int at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
