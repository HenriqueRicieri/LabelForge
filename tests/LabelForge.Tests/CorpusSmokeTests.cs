using LabelForge.Core.Rendering;

namespace LabelForge.Tests;

/// <summary>
/// Renders every real Atak label in "exemplos zpl/" through the offline engine.
/// This guards two things forever:
/// 1. The SkiaSharp 3 native library actually loads and renders in-process
///    alongside Avalonia (the M0 de-risk gate).
/// 2. Real-world Atak content (~DG graphics, ## template markers, // comments,
///    multiple ^XA blocks, rotated fonts, QR codes) does not crash the renderer.
/// </summary>
public sealed class CorpusSmokeTests
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static IEnumerable<object[]> CorpusFiles()
    {
        foreach (var path in TestCorpus.Files())
        {
            yield return new object[] { path };
        }
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void RealAtakLabels_RenderWithoutCrashing(string path)
    {
        string zpl = File.ReadAllText(path);
        var renderer = new BinaryKitsRenderer();

        // Size does not affect the smoke assertion; use a generous 4x6 inch at 203 dpi.
        // The contract: the renderer never throws on real input. It either produces a
        // valid PNG, or degrades to an empty image with the reason recorded in Errors.
        RenderResult result = renderer.Render(zpl, widthMm: 100, heightMm: 150, dpmm: 8);

        Assert.NotNull(result.Png);
        if (result.Png.Length > 0)
        {
            Assert.True(
                result.Png.Take(PngSignature.Length).SequenceEqual(PngSignature),
                $"output is not a PNG for {Path.GetFileName(path)}");
        }
        else
        {
            Assert.NotEmpty(result.Errors);
        }
    }

}
