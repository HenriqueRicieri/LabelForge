using BinaryKits.Zpl.Viewer.ElementDrawers;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// The typeface the preview draws ZPL's scalable font 0 with.
///
/// This exists because it was not pinned, and the consequence was invisible until two
/// machines were compared. `^A0` names font 0 unambiguously, but font 0 lives in the
/// printer, so a preview has to substitute something, and the engine took whatever the
/// machine had: one fell through to Segoe UI and drew "HELLO" at 40 dots as 107 dots of
/// ink, the other to Arial and drew 131. Twenty-two per cent apart, from the same ZPL.
///
/// <see cref="TextMetrics"/> is measured from what this renderer draws, and it decides the
/// selection outline, the snap targets, how long a continuous label measures and whether a
/// field is reported as running off the edge. Unpinned, that table described one machine's
/// font folder and told every other machine that text fits when it does not.
/// </summary>
public sealed class PreviewFontTests
{
    private static int InkWidth(DrawerOptions options, string text, char font = '0', int size = 40)
    {
        var document = new LabelDocument { WidthMm = 260, HeightMm = 60, Dpmm = 8 };
        document.Elements.Add(new TextElement
        {
            X = 100, Y = 40, Text = text, Font = font, FontHeightDots = size,
        });

        RenderResult result = new BinaryKitsRenderer(options)
            .Render(new ZplGenerator().Generate(document), 260, 60, 8);
        Assert.Empty(result.Errors);

        using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
        Assert.NotNull(bitmap);

        int minX = int.MaxValue, maxX = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                }
            }
        }

        return maxX < 0 ? 0 : maxX - minX + 1;
    }

    /// <summary>The font has to actually ship. A resource that stops being embedded looks
    /// exactly like one that is there, right up until somebody measures a label on a
    /// machine with different fonts installed.</summary>
    [Fact]
    public void TheFontIsEmbeddedAndInUse()
    {
        Assert.True(
            PreviewFont.IsPinned,
            "the preview font resource is missing, so font 0 has fallen back to whatever "
            + "this machine happens to have and every text footprint is machine-dependent "
            + $"again. Embedded resources: {string.Join(", ", PreviewFont.EmbeddedResourceNames())}");

        Assert.Equal("Roboto", PreviewFont.Scalable!.FamilyName);
    }

    /// <summary>
    /// The measurement that has to be the same everywhere. These numbers are a property of
    /// the shipped font, not of the machine, so a failure here means either the font is not
    /// being used or it changed. Before pinning, this same assertion produced 107 on one
    /// machine and 131 on another.
    /// </summary>
    [Theory]
    [InlineData("HELLO", 100)]
    [InlineData("hello", 71)]
    [InlineData("WWWWW", 149)]
    [InlineData("iiiii", 40)]
    [InlineData("12345", 94)]
    [InlineData("DESCRICAO DO PRODUTO", 403)]
    public void FontZeroDrawsTheSameWidthOnEveryMachine(string text, int expected)
    {
        Assert.Equal(
            expected, InkWidth(BinaryKitsRenderer.CreateDefaultOptions(), text));
    }

    /// <summary>
    /// The pinned font is resolved by ZPL designator rather than by family name, so nothing
    /// depends on what any font is called or on which are installed. This is the test that
    /// would fail if the pinning ever went back to matching names: registering a different
    /// typeface under the same family must not change what font 0 draws.
    /// </summary>
    [Fact]
    public void PinningIgnoresWhatIsInstalled()
    {
        int pinned = InkWidth(BinaryKitsRenderer.CreateDefaultOptions(), "HELLO");

        // The engine's own preference list, which is what used to decide this. If pinning
        // works, none of these names reaches the drawer at all.
        DrawerOptions stacked = BinaryKitsRenderer.CreateDefaultOptions();
        stacked.FontManager.FontStack0 =
            ["Arial", "Helvetica", "Segoe UI", "Times New Roman", "Courier New"];

        Assert.Equal(pinned, InkWidth(stacked, "HELLO"));
    }

    /// <summary>
    /// Pinning font 0 must leave the bitmapped fonts exactly as they were. They are a
    /// separate matter: their metrics come from the manual rather than from this renderer,
    /// because the engine draws five of the eight at the wrong width, so pinning them would
    /// buy a steadier picture and no correctness at the price of bundling a second font.
    /// </summary>
    [Theory]
    [InlineData('A')]
    [InlineData('B')]
    [InlineData('D')]
    [InlineData('G')]
    public void TheBitmappedFontsAreLeftAsTheyWere(char font)
    {
        // The engine's untouched behaviour: same options, no font loader of ours.
        var untouched = new DrawerOptions
        {
            OpaqueBackground = true,
            Antialias = true,
            ReplaceDashWithEnDash = false,
            ReplaceUnderscoreWithEnSpace = false,
        };

        Assert.Equal(
            InkWidth(untouched, "HELLO", font),
            InkWidth(BinaryKitsRenderer.CreateDefaultOptions(), "HELLO", font));
    }

    /// <summary>
    /// The point of choosing this typeface rather than any redistributable one: it is close
    /// to what a printer actually lays down. The reference widths are Labelary's rendered
    /// ink for the same synthetic strings, Labelary being the thing that renders what a
    /// printer prints. Arial, which one machine had silently been using, misses these by up
    /// to 25 per cent.
    /// </summary>
    [Theory]
    [InlineData("HELLO", 101)]
    [InlineData("hello", 75)]
    [InlineData("WWWWW", 161)]
    [InlineData("12345", 92)]
    [InlineData("DESCRICAO DO PRODUTO", 412)]
    [InlineData("Industria Brasileira", 301)]
    public void ThePreviewIsCloseToWhatAPrinterPrints(string text, int labelaryInk)
    {
        int ink = InkWidth(BinaryKitsRenderer.CreateDefaultOptions(), text);
        double error = Math.Abs(ink - labelaryInk) / (double)labelaryInk;

        Assert.True(
            error <= 0.13,
            $"'{text}' drew {ink} dots against Labelary's {labelaryInk} ({error:P1} off)");
    }
}
