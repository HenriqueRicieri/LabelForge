using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// GS1-128: application identifiers packed into one Code 128 symbol, plus the two ZPL
/// escapes that make it printable at a sane width.
/// </summary>
public sealed class Gs1Tests
{
    /// <summary>
    /// The symbol count against the rendered ink, exactly, for every case.
    ///
    /// This is what the footprint rests on, and counting characters gets it badly wrong
    /// the moment a payload uses the subset escape: the first case here is 29 characters
    /// and 14 symbols. The old formula measured it at nearly twice its real width.
    /// </summary>
    [Theory]
    [InlineData(">;>801078912345678953102001234", 14)]
    [InlineData(">;>80107891234567895", 9)]
    [InlineData(">;0107891234567895", 8)]
    [InlineData(">;>8010789123456789531020012342199", 16)]
    [InlineData(">8010789123456789531020012342199", 31)]
    [InlineData("0107891234567895", 16)]
    [InlineData("ABC12345", 8)]
    public void SymbolCount_MatchesTheRenderedInk(string data, int expectedSymbols)
    {
        Assert.Equal(expectedSymbols, Code128Encoding.CountSymbols(data));

        var element = new BarcodeElement
        {
            X = 100, Y = 100, Symbology = BarcodeSymbology.Code128, Data = data,
            HeightDots = 80, ModuleWidthDots = 2, PrintInterpretationLine = false,
        };
        var document = new LabelDocument { WidthMm = 200, HeightMm = 60, Dpmm = 8 };
        document.Elements.Add(element);

        RenderResult result = new BinaryKitsRenderer()
            .Render(new ZplGenerator().Generate(document), 200, 60, 8);
        Assert.Empty(result.Errors);

        Assert.Equal(InkWidth(result), new ElementBoundsCalculator().GetBounds(element).Width);
        Assert.Equal(Code128Encoding.WidthModules(data) * 2, InkWidth(result));
    }

    /// <summary>A payload with no escapes is unchanged, so every label that never used
    /// them keeps the footprint it had.</summary>
    [Fact]
    public void PlainData_CountsOneSymbolPerCharacter() =>
        Assert.Equal(11, Code128Encoding.CountSymbols("ABC12345678"));

    [Fact]
    public void Build_PacksFixedLengthFieldsWithNoSeparator()
    {
        string data = Gs1Payload.Build(
            [new Gs1Field("01", "07891234567895"), new Gs1Field("3102", "001234")]);

        // Subset C for the digits, one FNC1 to open, and nothing between the two fields
        // because a scanner knows how long each is.
        Assert.Equal(">;>801078912345678953102001234", data);
    }

    /// <summary>The rule that matters: a variable-length value has to be terminated, or a
    /// scanner reads it and whatever follows as one wrong value.</summary>
    [Fact]
    public void Build_SeparatesAVariableLengthFieldFromWhatFollows()
    {
        string data = Gs1Payload.Build(
            [new Gs1Field("10", "LOTE42"), new Gs1Field("01", "07891234567895")]);

        Assert.Contains("LOTE42" + Gs1Payload.Fnc1, data, StringComparison.Ordinal);
        Assert.Equal(["10", "01"], Gs1Payload.Read(data).Fields.Select(f => f.Code));
    }

    /// <summary>A trailing separator would encode a symbol that buys nothing.</summary>
    [Fact]
    public void Build_LeavesNoSeparatorAtTheEnd()
    {
        string data = Gs1Payload.Build(
            [new Gs1Field("01", "07891234567895"), new Gs1Field("21", "ABC123")]);

        Assert.False(data.EndsWith(Gs1Payload.Fnc1, StringComparison.Ordinal));
    }

    /// <summary>Letters cannot be packed two to a symbol, so a payload that mixes them
    /// moves subsets rather than paying for everything in the wider one.</summary>
    [Fact]
    public void Build_LeavesSubsetCForLetters()
    {
        string data = Gs1Payload.Build(
            [new Gs1Field("01", "07891234567895"), new Gs1Field("21", "ABC")]);

        Assert.Contains(Gs1Payload.SubsetB, data, StringComparison.Ordinal);
        Assert.True(
            Code128Encoding.CountSymbols(data) < data.Length,
            "the digits should still be packed");
    }

    [Fact]
    public void Build_AndReadBack_AgreeOnTheFields()
    {
        Gs1Field[] fields =
        [
            new("01", "07891234567895"),
            new("3102", "001234"),
            new("10", "LOTE42"),
        ];

        Gs1Reading read = Gs1Payload.Read(Gs1Payload.Build(fields));

        Assert.Equal(fields.Select(f => f.Code), read.Fields.Select(f => f.Code));
        Assert.Equal(fields.Select(f => f.Value), read.Fields.Select(f => f.Value));
        Assert.Empty(read.Problems);
    }

    /// <summary>The bracketed form is what a person reads and what belongs under the bars
    /// when a GS1-128 prints its interpretation line.</summary>
    [Fact]
    public void Describe_ProducesTheBracketedForm() =>
        Assert.Equal(
            "(01)07891234567895(3102)001234",
            Gs1Payload.Describe(">;>801078912345678953102001234"));

    [Fact]
    public void Read_ReportsAFixedLengthValueOfTheWrongSize()
    {
        Gs1Reading read = Gs1Payload.Read(">;>801078912345");

        Assert.Contains(read.Problems, p => p.Contains("(01)", StringComparison.Ordinal));
    }

    /// <summary>An identifier outside the working set is reported, not refused: the
    /// standard runs to hundreds and a payload using one still prints.</summary>
    [Fact]
    public void Read_ReportsAnIdentifierItDoesNotKnow()
    {
        Gs1Reading read = Gs1Payload.Read(">;>89912345");

        Assert.NotEmpty(read.Fields);
        Assert.Contains(read.Problems, p => p.Contains("not an identifier", StringComparison.Ordinal));
    }

    /// <summary>A template marker in place of a value is normal on a designed label and
    /// must not be reported as non-numeric.</summary>
    [Fact]
    public void Read_AcceptsAMarkerWhereAValueWillGo()
    {
        Gs1Reading read = Gs1Payload.Read(">;>801##EAN##");

        Assert.Equal("01", Assert.Single(read.Fields).Code);
        Assert.DoesNotContain(read.Problems, p => p.Contains("digits only", StringComparison.Ordinal));
    }

    [Fact]
    public void IsGs1_RecognisesAPayloadByItsOpeningSeparator()
    {
        Assert.True(Gs1Payload.IsGs1(">;>801078912345678895"));
        Assert.False(Gs1Payload.IsGs1("0107891234567895"));
    }

    /// <summary>The real label's payload, taken from the corpus, reads back as the four
    /// fields it was written to carry.</summary>
    [Fact]
    public void Read_HandlesTheShapeARealLabelUses()
    {
        Gs1Reading read = Gs1Payload.Read(">;>801##EAN##3102123456" + Gs1Payload.Fnc1 + "21##CODIGO_VOLUME##");

        Assert.Equal(["01", "3102", "21"], read.Fields.Select(f => f.Code));
    }

    /// <summary>
    /// The failure the assembler exists to prevent, seen from the reading side. Written
    /// without the separator after the batch number, the value runs on and swallows the
    /// two fields after it, so the payload reads back as one over-long field. Nothing
    /// about the barcode fails; it scans and returns the wrong thing.
    /// </summary>
    [Fact]
    public void Read_NamesAVariableFieldThatSwallowedWhatFollowed()
    {
        Gs1Reading read = Gs1Payload.Read(">;>810LOTE42>:01078912345678953102001234");

        Gs1Field only = Assert.Single(read.Fields);
        Assert.Equal("10", only.Code);
        Assert.Contains(read.Problems, p => p.Contains("separator after it is missing", StringComparison.Ordinal));
    }

    /// <summary>And the same fields assembled properly raise nothing, which is the
    /// difference the separator makes.</summary>
    [Fact]
    public void Build_ProducesThePayloadThatDoesNotSwallow()
    {
        string data = Gs1Payload.Build(
        [
            new Gs1Field("10", "LOTE42"),
            new Gs1Field("01", "07891234567895"),
            new Gs1Field("3102", "001234"),
        ]);

        Gs1Reading read = Gs1Payload.Read(data);

        Assert.Equal(["10", "01", "3102"], read.Fields.Select(f => f.Code));
        Assert.Empty(read.Problems);
    }

    private static int InkWidth(RenderResult result)
    {
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
}
