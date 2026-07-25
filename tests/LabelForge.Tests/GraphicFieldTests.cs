using LabelForge.Core.Imaging;
using LabelForge.Core.Rendering;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// The GRF codec is the one place that knows ZPL's graphic data format, so both
/// directions are pinned here: our own round trip, the compression scheme as real
/// drivers write it, and - the part that actually protects WYSIWYG - proof that the
/// offline renderer draws our compressed output exactly like plain hex.
/// </summary>
public sealed class GraphicFieldTests
{
    [Fact]
    public void Encode_CollapsesABlankRowToOneCharacter()
    {
        Assert.Equal(",", GraphicField.Encode(new bool[8], 8, 1));
    }

    [Fact]
    public void Encode_CollapsesOnlyTheTrailingWhiteOfARow()
    {
        var black = new bool[16];
        black[0] = black[1] = black[2] = black[3] = true;
        black[8] = true;

        // Nibbles F,0,8,0: the run of white in the middle is written out, only the
        // one that reaches the end of the row collapses.
        Assert.Equal("F08,", GraphicField.Encode(black, 16, 1));
    }

    [Fact]
    public void Encode_RepeatsIdenticalRowsWithColon()
    {
        var black = new bool[8 * 3];
        for (int i = 0; i < black.Length; i++)
        {
            black[i] = i % 8 < 4;
        }

        // F0 per row, the trailing zero collapsing to a comma, then two repeats.
        Assert.Equal("F,::", GraphicField.Encode(black, 8, 3));
    }

    /// <summary>
    /// Solid black rows have a one-character form ("!") in ZPL, and we deliberately do
    /// not use it: BinaryKits draws it wrong. This pins that choice so a future
    /// "optimization" has to face the renderer evidence first.
    /// </summary>
    [Fact]
    public void Encode_WritesSolidBlackOutInsteadOfUsingBang()
    {
        var black = new bool[16];
        Array.Fill(black, true);

        string data = GraphicField.Encode(black, 16, 1);

        Assert.Equal("FFFF", data);
        Assert.DoesNotContain('!', data);
    }

    [Fact]
    public void Encode_NeverEmitsCountPrefixes()
    {
        var black = new bool[512 * 4];
        for (int i = 0; i < black.Length; i++)
        {
            black[i] = i % 512 < 500;
        }

        string data = GraphicField.Encode(black, 512, 4);

        Assert.DoesNotContain(data, c => c is (>= 'G' and <= 'Y') or (>= 'g' and <= 'z'));
    }

    [Theory]
    [InlineData(17, 9)]
    [InlineData(64, 40)]
    [InlineData(203, 117)]
    public void EncodeThenDecode_ReturnsTheSameMask(int width, int height)
    {
        // A deterministic pattern with solid, blank, and mixed rows.
        int stored = (width + 7) / 8 * 8;
        var black = new bool[stored * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < stored; x++)
            {
                black[y * stored + x] = ((x * 31 + y * 17) % 11) switch
                {
                    0 or 1 or 2 => true,
                    _ => y % 7 == 0,
                };
            }
        }

        string data = GraphicField.Encode(black, stored, height);
        int bytesPerRow = stored / 8;

        GraphicBitmap? decoded = GraphicField.Decode(data, bytesPerRow, bytesPerRow * height);

        Assert.NotNull(decoded);
        Assert.Equal(stored, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.Equal(black, decoded.Black);
    }

    [Fact]
    public void Decode_ReadsTheSchemeAsDriversWriteIt()
    {
        // Row 1: "M8" = seven 8s then a comma fills the eighth nibble with 0.
        // Row 2: solid black. Row 3: repeat of row 2. Row 4: blank.
        GraphicBitmap? decoded = GraphicField.Decode("M8,!:,", bytesPerRow: 4, totalBytes: 16);

        Assert.NotNull(decoded);
        Assert.Equal(32, decoded.Width);
        Assert.Equal(4, decoded.Height);

        // 8 = 1000 binary, so every fourth pixel of the first 28 is black.
        for (int x = 0; x < 28; x++)
        {
            Assert.Equal(x % 4 == 0, decoded.Black[x]);
        }

        Assert.All(Enumerable.Range(28, 4), x => Assert.False(decoded.Black[x]));
        Assert.All(Enumerable.Range(32, 32), i => Assert.True(decoded.Black[i]));
        Assert.All(Enumerable.Range(64, 32), i => Assert.True(decoded.Black[i]));
        Assert.All(Enumerable.Range(96, 32), i => Assert.False(decoded.Black[i]));
    }

    [Fact]
    public void Decode_IgnoresLineBreaksInsideTheData()
    {
        GraphicBitmap? wrapped = GraphicField.Decode("FF\r\n00", 1, 2);
        GraphicBitmap? flat = GraphicField.Decode("FF00", 1, 2);

        Assert.NotNull(wrapped);
        Assert.NotNull(flat);
        Assert.Equal(flat.Black, wrapped.Black);
    }

    [Fact]
    public void Decode_UnwrapsTheZ64Envelope()
    {
        byte[] raw = [0xFF, 0x00, 0xAA, 0x55];
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(
                   compressed, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        string payload = $":Z64:{Convert.ToBase64String(compressed.ToArray())}:1234";

        GraphicBitmap? decoded = GraphicField.Decode(payload, bytesPerRow: 1, totalBytes: 4);
        GraphicBitmap? hex = GraphicField.Decode("FF00AA55", bytesPerRow: 1, totalBytes: 4);

        Assert.NotNull(decoded);
        Assert.NotNull(hex);
        Assert.Equal(hex.Black, decoded.Black);
    }

    [Fact]
    public void Decode_RejectsSizesThatCannotDescribeAGraphic()
    {
        Assert.Null(GraphicField.Decode("FF", 0, 4));
        Assert.Null(GraphicField.Decode("FF", 4, 0));

        // Fewer total bytes than one row: there is no first row to fill.
        Assert.Null(GraphicField.Decode("FF", 8, 4));
    }

    [Fact]
    public void Decode_PadsTruncatedDataWithWhiteInsteadOfFailing()
    {
        GraphicBitmap? decoded = GraphicField.Decode("FF", bytesPerRow: 1, totalBytes: 3);

        Assert.NotNull(decoded);
        Assert.Equal(3, decoded.Height);
        Assert.All(Enumerable.Range(0, 8), i => Assert.True(decoded.Black[i]));
        Assert.All(Enumerable.Range(8, 16), i => Assert.False(decoded.Black[i]));
    }

    /// <summary>
    /// The canvas underlay is whatever the renderer makes of the ZPL we generate, so
    /// compressed graphic data is only safe if BinaryKits expands it the same way we
    /// do. Rendering both forms of one bitmap and comparing pixels is the guard.
    /// </summary>
    [Fact]
    public void Renderer_DrawsCompressedGraphicsIdenticallyToPlainHex()
    {
        const int width = 96;
        const int height = 48;
        var black = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Solid bands, blank bands, and a diagonal: exercises "!", ",", ":"
                // and the count prefixes in one image.
                black[y * width + x] = y < 8 || (y is >= 16 and < 24) || x == y * 2;
            }
        }

        string compressed = ZplImageEncoder.EncodeGfa(black, width, height);
        string plain = PlainHexGfa(black, width, height);

        Assert.Contains(":", compressed, StringComparison.Ordinal);
        Assert.Contains(",", compressed, StringComparison.Ordinal);
        Assert.True(
            compressed.Length < plain.Length,
            $"compression should shrink the payload (was {compressed.Length} vs {plain.Length})");

        var renderer = new BinaryKitsRenderer();
        byte[] fromCompressed = Render(renderer, compressed);
        byte[] fromPlain = Render(renderer, plain);

        Assert.Equal(ToPixels(fromPlain), ToPixels(fromCompressed));
    }

    private static byte[] Render(BinaryKitsRenderer renderer, string field)
    {
        RenderResult result = renderer.Render($"^XA\n^FO0,0{field}\n^XZ", 20, 12, 8);
        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Png);
        return result.Png;
    }

    private static byte[] ToPixels(byte[] png)
    {
        using SKBitmap? bitmap = SKBitmap.Decode(png);
        Assert.NotNull(bitmap);
        return bitmap.GetPixelSpan().ToArray();
    }

    /// <summary>The uncompressed form this codec replaced, kept as the comparison
    /// oracle: every nibble written out, no scheme characters.</summary>
    private static string PlainHexGfa(bool[] black, int width, int height)
    {
        int bytesPerRow = (width + 7) / 8;
        int totalBytes = bytesPerRow * height;
        var sb = new System.Text.StringBuilder();
        sb.Append("^GFA,").Append(totalBytes).Append(',').Append(totalBytes)
          .Append(',').Append(bytesPerRow).Append(',');
        for (int y = 0; y < height; y++)
        {
            for (int b = 0; b < bytesPerRow; b++)
            {
                int value = 0;
                for (int bit = 0; bit < 8; bit++)
                {
                    int x = b * 8 + bit;
                    if (x < width && black[y * width + x])
                    {
                        value |= 0x80 >> bit;
                    }
                }

                sb.Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return sb.Append("^FS").ToString();
    }
}
