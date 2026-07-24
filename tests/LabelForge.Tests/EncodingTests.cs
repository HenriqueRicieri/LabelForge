using System.Text;
using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// Accented text has to survive the whole path identically: generator, project file,
/// exported bytes, offline renderer, and the wire to the printer. Brazilian labels are
/// full of it, and every stage here has a plausible way to quietly corrupt it, so each
/// stage gets its own assertion rather than one end-to-end check that hides which link
/// broke.
/// </summary>
public sealed class EncodingTests
{
    private const string Accented = "Açúcar - DESCRIÇÃO - São Paulo - Jataí - Térreo";

    // ---- Reading files ------------------------------------------------------------

    [Fact]
    public void Read_DecodesPlainUtf8()
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes($"^XA^FD{Accented}^FS^XZ");

        ZplTextRead read = ZplTextFile.Read(bytes);

        Assert.Equal($"^XA^FD{Accented}^FS^XZ", read.Text);
        Assert.Equal("UTF-8", read.EncodingName);
        Assert.False(read.Recovered);
    }

    [Fact]
    public void Read_HonoursAUtf8ByteOrderMark_AndStripsIt()
    {
        byte[] bytes = [.. new UTF8Encoding(true).GetPreamble(),
                        .. Encoding.UTF8.GetBytes($"^XA^FD{Accented}^FS^XZ")];

        ZplTextRead read = ZplTextFile.Read(bytes);

        Assert.StartsWith("^XA", read.Text, StringComparison.Ordinal);
        Assert.Contains(Accented, read.Text, StringComparison.Ordinal);
        Assert.False(read.Recovered);
    }

    [Theory]
    [InlineData(false, "UTF-16 LE")]
    [InlineData(true, "UTF-16 BE")]
    public void Read_HonoursUtf16ByteOrderMarks(bool bigEndian, string expectedName)
    {
        Encoding encoding = new UnicodeEncoding(bigEndian, byteOrderMark: true);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(Accented)];

        ZplTextRead read = ZplTextFile.Read(bytes);

        Assert.Equal(Accented, read.Text);
        Assert.Equal(expectedName, read.EncodingName);
        Assert.False(read.Recovered);
    }

    [Fact]
    public void Read_RecoversLegacyLatin1_WhereTheNaiveReadWouldLoseEveryAccent()
    {
        // What a legacy Windows tool writes: one byte per character, no BOM.
        byte[] bytes = Encoding.Latin1.GetBytes($"^XA^FD{Accented}^FS^XZ");

        ZplTextRead read = ZplTextFile.Read(bytes);

        Assert.Contains(Accented, read.Text, StringComparison.Ordinal);
        Assert.Equal("Latin-1", read.EncodingName);
        Assert.True(read.Recovered);

        // The point of the exercise: the default lenient UTF-8 decode, which is what a
        // bare StreamReader does, turns every accent into a replacement character.
        Assert.Contains('�', Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain('�', read.Text);
    }

    [Fact]
    public void Read_TreatsPlainAsciiAsUtf8()
    {
        ZplTextRead read = ZplTextFile.Read("^XA^FDPLAIN^FS^XZ"u8.ToArray());

        Assert.Equal("^XA^FDPLAIN^FS^XZ", read.Text);
        Assert.False(read.Recovered);
    }

    [Fact]
    public void Read_OfNothing_IsEmptyRatherThanAFailure()
    {
        ZplTextRead read = ZplTextFile.Read([]);

        Assert.Equal(string.Empty, read.Text);
        Assert.False(read.Recovered);
    }

    // ---- Writing files ------------------------------------------------------------

    [Fact]
    public void ToBytes_IsUtf8WithNoByteOrderMark()
    {
        byte[] bytes = ZplTextFile.ToBytes($"^XA^FD{Accented}^FS^XZ");

        Assert.Equal((byte)'^', bytes[0]);
        Assert.Equal(new UTF8Encoding(false).GetBytes($"^XA^FD{Accented}^FS^XZ"), bytes);
    }

    [Fact]
    public void ToBytes_EncodesAccentsAsTwoByteUtf8()
    {
        // c-cedilla is U+00E7: 0xC3 0xA7 in UTF-8, which is what ^CI28 tells the
        // printer to expect. A single 0xE7 would be the Latin-1 form and print wrong.
        byte[] bytes = ZplTextFile.ToBytes("ç");

        Assert.Equal(new byte[] { 0xC3, 0xA7 }, bytes);
    }

    [Fact]
    public async Task WriteFileAsync_ThenRead_RoundTripsAccents()
    {
        string path = Path.Combine(Path.GetTempPath(), $"labelforge-{Guid.NewGuid():N}.zpl");
        try
        {
            await ZplTextFile.WriteFileAsync(path, $"^XA^FD{Accented}^FS^XZ");

            byte[] raw = await File.ReadAllBytesAsync(path);
            Assert.Equal((byte)'^', raw[0]);
            Assert.Equal($"^XA^FD{Accented}^FS^XZ", ZplTextFile.ReadFile(path).Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- Generator ------------------------------------------------------------------

    [Fact]
    public void Generator_DeclaresUtf8AndPassesAccentsThroughUnescaped()
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        doc.Elements.Add(new TextElement { X = 20, Y = 30, FontHeightDots = 30, Text = Accented });

        string zpl = new ZplGenerator().Generate(doc);

        Assert.Contains("^CI28", zpl, StringComparison.Ordinal);
        Assert.Contains($"^FO20,30^A0N,30^FD{Accented}^FS", zpl, StringComparison.Ordinal);

        // Accents need no field-hex escaping: ^CI28 plus UTF-8 bytes is the whole
        // mechanism. Only '^', '~' and '_' trigger ^FH.
        Assert.DoesNotContain("^FH", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_DeclaresUtf8BeforeAnyFieldData()
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        doc.Elements.Add(new TextElement { Text = Accented });

        string zpl = new ZplGenerator().Generate(doc);

        // ^CI28 selects the character set for the fields that follow it, so a field
        // emitted first would be decoded with the previous setting.
        Assert.True(
            zpl.IndexOf("^CI28", StringComparison.Ordinal) < zpl.IndexOf("^FD", StringComparison.Ordinal),
            "^CI28 must precede the first field");
    }

    [Fact]
    public void GeneratedAccentedZpl_SurvivesTheTripToTheWire()
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        doc.Elements.Add(new TextElement { X = 10, Y = 10, Text = Accented });

        string zpl = new ZplGenerator().Generate(doc);
        byte[] payload = ZplTextFile.ToBytes(zpl);

        // Decoding the payload the way a ^CI28 printer does returns exactly what was
        // generated: no BOM, no escaping, no lost characters.
        Assert.Equal(zpl, new UTF8Encoding(false).GetString(payload));
        Assert.Contains(Accented, new UTF8Encoding(false).GetString(payload), StringComparison.Ordinal);
    }

    // ---- Project file ----------------------------------------------------------------

    [Fact]
    public void Document_RoundTripsAccentedContentThroughTheProjectFormat()
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        doc.Elements.Add(new TextElement { Text = Accented });
        doc.Elements.Add(new QrCodeElement { Data = "Endereço: Jataí" });
        doc.SampleValues["DESCRIÇÃO"] = "Açúcar";

        LabelDocument reloaded = LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(doc));

        Assert.Equal(Accented, ((TextElement)reloaded.Elements[0]).Text);
        Assert.Equal("Endereço: Jataí", ((QrCodeElement)reloaded.Elements[1]).Data);
        Assert.Equal("Açúcar", reloaded.SampleValues["DESCRIÇÃO"]);
    }

    // ---- Offline renderer -------------------------------------------------------------

    [Fact]
    public void Renderer_DrawsAccentedTextDifferentlyFromItsUnaccentedTwin()
    {
        // Proves the accents reach the raster rather than being dropped on the way in.
        // It cannot prove the glyph is the right one, which is what the eye and the
        // designer-variables capture are for; it does catch silent stripping.
        var renderer = new BinaryKitsRenderer();

        RenderResult accented = renderer.Render(
            "^XA^CI28^FO20,20^A0N,40^FDDESCRIÇÃO^FS^XZ", 100, 60, 8);
        RenderResult plain = renderer.Render(
            "^XA^CI28^FO20,20^A0N,40^FDDESCRICAO^FS^XZ", 100, 60, 8);

        Assert.Empty(accented.Errors);
        Assert.NotEmpty(accented.Png);
        Assert.False(accented.Png.SequenceEqual(plain.Png), "accents were not rendered");
    }

    [Fact]
    public void Renderer_HandlesTheAccentedFixtureWithoutErrors()
    {
        string path = Path.Combine(TestCorpus.FixturesDirectory(), "accented-text.zpl");
        ZplTextRead read = ZplTextFile.ReadFile(path);

        Assert.False(read.Recovered, "the committed fixture must be valid UTF-8");
        Assert.Contains("DESCRIÇÃO", read.Text, StringComparison.Ordinal);
        Assert.DoesNotContain('�', read.Text);

        RenderResult result = new BinaryKitsRenderer().Render(read.Text, 100, 60, 8);

        Assert.NotEmpty(result.Png);
        Assert.Empty(result.Errors);
    }
}
