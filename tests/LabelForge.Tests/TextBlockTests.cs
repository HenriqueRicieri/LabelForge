using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// Text blocks (^FB). The corpus writes them almost entirely as "one line, centred in a
/// known width", so alignment carries more weight here than word wrap, and the
/// single-line default is kept faithfully rather than raised for convenience.
/// </summary>
public sealed class TextBlockTests
{
    private static LabelDocument Label(TextElement text)
    {
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        document.Elements.Add(text);
        return document;
    }

    [Fact]
    public void APlainLine_EmitsNoFieldBlockAtAll()
    {
        string zpl = new ZplGenerator().Generate(Label(new TextElement
        {
            X = 10, Y = 20, Text = "hello", FontHeightDots = 30,
        }));

        Assert.DoesNotContain("^FB", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlock_EmitsEveryArgumentSoNothingFallsBackOnThePrinter()
    {
        string zpl = new ZplGenerator().Generate(Label(new TextElement
        {
            X = 10, Y = 20, Text = "hello", FontHeightDots = 30,
            BlockWidthDots = 400, BlockMaxLines = 3, BlockLineSpacingDots = 4,
            Justification = TextJustification.Center, BlockHangingIndentDots = 12,
        }));

        Assert.Contains("^FO10,20^A0N,30^FB400,3,4,C,12^FDhello^FS", zpl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TextJustification.Left, "L")]
    [InlineData(TextJustification.Center, "C")]
    [InlineData(TextJustification.Right, "R")]
    [InlineData(TextJustification.Justified, "J")]
    public void EachJustification_HasItsZplLetter(TextJustification justification, string letter)
    {
        string zpl = new ZplGenerator().Generate(Label(new TextElement
        {
            Text = "x", FontHeightDots = 20, BlockWidthDots = 200, Justification = justification,
        }));

        Assert.Contains($"^FB200,1,0,{letter},0", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_OfABlock_IsByteIdentical()
    {
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        document.Elements.Add(new TextElement
        {
            X = 10, Y = 20, Text = "wrap me across a few lines please", FontHeightDots = 28,
            BlockWidthDots = 300, BlockMaxLines = 4, BlockLineSpacingDots = 6,
            Justification = TextJustification.Justified, BlockHangingIndentDots = 20,
        });
        document.Elements.Add(new TextElement
        {
            X = 10, Y = 300, Text = "plain", FontHeightDots = 28,
        });

        string first = new ZplGenerator().Generate(document);
        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(first, dpmm: 8);

        Assert.Empty(imported.Warnings);
        Assert.Equal(first, new ZplGenerator().Generate(imported.Document));

        var block = Assert.IsType<TextElement>(imported.Document.Elements[0]);
        Assert.Equal(300, block.BlockWidthDots);
        Assert.Equal(4, block.BlockMaxLines);
        Assert.Equal(6, block.BlockLineSpacingDots);
        Assert.Equal(TextJustification.Justified, block.Justification);
        Assert.Equal(20, block.BlockHangingIndentDots);

        // The second field must not inherit the first one's block.
        Assert.False(((TextElement)imported.Document.Elements[1]).IsBlock);
    }

    /// <summary>The commonest form in the corpus, where the middle arguments are simply
    /// left out. Each one has to fall back on its own ZPL default.</summary>
    [Fact]
    public void TheCorpusShorthand_ParsesWithZplDefaults()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA\n^FO0,0^A0N,30^FB931,1,,C^FDcentred^FS\n^XZ");

        var text = Assert.IsType<TextElement>(Assert.Single(result.Document.Elements));
        Assert.Equal(931, text.BlockWidthDots);
        Assert.Equal(1, text.BlockMaxLines);
        Assert.Equal(0, text.BlockLineSpacingDots);
        Assert.Equal(TextJustification.Center, text.Justification);
        Assert.Equal(0, text.BlockHangingIndentDots);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Bounds_OfABlock_UseItsDeclaredWidth()
    {
        var calculator = new ElementBoundsCalculator();

        DotRect single = calculator.GetBounds(new TextElement
        {
            Text = "a very long line of text indeed", FontHeightDots = 30,
        });
        DotRect block = calculator.GetBounds(new TextElement
        {
            Text = "a very long line of text indeed", FontHeightDots = 30,
            BlockWidthDots = 200, BlockMaxLines = 5,
        });

        // Comfortably past the block's width, which is the point: an unwrapped line runs
        // as far as it likes. The bound used to be 400, which only passed because the old
        // average advance made this mostly-lowercase line half again too wide; measured,
        // it is about 330 dots.
        Assert.True(
            single.Width > block.Width + 100,
            $"an unwrapped line runs as far as it likes ({single.Width} vs {block.Width})");
        Assert.Equal(200, block.Width);
        Assert.True(block.Height > 30, "a wrapped block is taller than one line");
    }

    /// <summary>A block that cannot wrap is exactly one line tall, and the height grows
    /// by the line spacing rather than ignoring it.</summary>
    [Fact]
    public void Bounds_RespectTheLineCapAndSpacing()
    {
        var calculator = new ElementBoundsCalculator();

        DotRect capped = calculator.GetBounds(new TextElement
        {
            Text = "a very long line of text indeed", FontHeightDots = 30,
            BlockWidthDots = 200, BlockMaxLines = 1,
        });
        Assert.Equal(30, capped.Height);

        DotRect spaced = calculator.GetBounds(new TextElement
        {
            Text = "a very long line of text indeed", FontHeightDots = 30,
            BlockWidthDots = 200, BlockMaxLines = 5, BlockLineSpacingDots = 10,
        });
        DotRect tight = calculator.GetBounds(new TextElement
        {
            Text = "a very long line of text indeed", FontHeightDots = 30,
            BlockWidthDots = 200, BlockMaxLines = 5,
        });
        Assert.True(spaced.Height > tight.Height);
    }

    /// <summary>
    /// The WYSIWYG guard: alignment has to move the ink on the canvas, or the designer
    /// would show every block left-aligned while the printer centres it.
    /// </summary>
    [Fact]
    public void Renderer_MovesTheInkForEachAlignment()
    {
        var renderer = new BinaryKitsRenderer();

        int Left(TextJustification justification)
        {
            string zpl = new ZplGenerator().Generate(Label(new TextElement
            {
                X = 20, Y = 20, Text = "short", FontHeightDots = 28, FontWidthDots = 28,
                BlockWidthDots = 400, Justification = justification,
            }));

            RenderResult result = renderer.Render(zpl, 100, 60, 8);
            Assert.Empty(result.Errors);
            using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
            Assert.NotNull(bitmap);

            for (int x = 0; x < bitmap.Width; x++)
            {
                for (int y = 0; y < bitmap.Height; y++)
                {
                    if (bitmap.GetPixel(x, y).Red < 128)
                    {
                        return x;
                    }
                }
            }

            return -1;
        }

        int left = Left(TextJustification.Left);
        int centre = Left(TextJustification.Center);
        int right = Left(TextJustification.Right);

        Assert.True(left > 0, "the text must actually render");
        Assert.True(centre > left, $"centred text must sit right of left aligned ({centre} vs {left})");
        Assert.True(right > centre, $"right aligned must sit right of centred ({right} vs {centre})");

        // Right alignment lands the text against the far edge of the 400 dot block.
        Assert.InRange(right, 300, 420);
    }

    /// <summary>
    /// A limitation, pinned so it stays a known quantity: BinaryKits draws every line a
    /// block produces and ignores the maximum, while a printer stops at it. The property
    /// is still modelled faithfully, because real labels set it to 1 on purpose and
    /// raising it on their behalf would change what they print. The panel says so.
    /// </summary>
    [Fact]
    public void Renderer_IgnoresTheLineCap()
    {
        var renderer = new BinaryKitsRenderer();

        int Ink(int maxLines)
        {
            string zpl = new ZplGenerator().Generate(Label(new TextElement
            {
                X = 20, Y = 20, Text = "alpha beta gamma delta", FontHeightDots = 24,
                BlockWidthDots = 120, BlockMaxLines = maxLines,
            }));

            using SKBitmap? bitmap = SKBitmap.Decode(renderer.Render(zpl, 100, 60, 8).Png);
            Assert.NotNull(bitmap);

            int ink = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).Red < 128)
                    {
                        ink++;
                    }
                }
            }

            return ink;
        }

        Assert.Equal(Ink(9), Ink(1));
    }
}
