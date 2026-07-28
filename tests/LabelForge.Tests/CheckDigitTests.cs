using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// Interleaved 2 of 5 and the check digits the UPC/EAN family and ^B2 carry.
///
/// A check digit is invisible in the ZPL whichever symbology carries it, so every fact
/// here is about a difference between what is typed and what scans. Getting one wrong
/// does not make a barcode fail: it makes it scan as a different, valid number, which is
/// the worst way for a label to be broken.
/// </summary>
public sealed class CheckDigitTests
{
    private static BarcodeElement Itf(
        string data, int module = 2, double ratio = 3.0, bool checkDigit = false) => new()
    {
        X = 100, Y = 100, Symbology = BarcodeSymbology.Interleaved2of5, Data = data,
        HeightDots = 80, ModuleWidthDots = module, WideBarRatio = ratio,
        AddCheckDigit = checkDigit, PrintInterpretationLine = false,
    };

    private static RenderResult Render(BarcodeElement element)
    {
        var document = new LabelDocument { WidthMm = 200, HeightMm = 60, Dpmm = 8 };
        document.Elements.Add(element);
        return new BinaryKitsRenderer()
            .Render(new ZplGenerator().Generate(document), 200, 60, 8);
    }

    /// <summary>
    /// The symbol's width against the ink the renderer lays down, which is the only way
    /// this can be checked: arithmetic that agrees with itself proves nothing about where
    /// the dots land.
    ///
    /// Interleaved 2 of 5 is the one symbology here whose width is not a whole number of
    /// modules. Its wide bar is floor(ratio * module) dots, so at a ratio of 2.5 and a
    /// module of 2 a wide bar is 5 dots - two and a half modules - and counting modules
    /// the way every other symbology here does cannot express the answer. Ratios 2.2 and
    /// 2.8 are in the list because they pin the truncation: they draw as 2.0 and 2.5, not
    /// as the nearest whole number of dots.
    /// </summary>
    [Theory]
    [InlineData("12", 2, 3.0, 54)]
    [InlineData("1234", 2, 3.0, 90)]
    [InlineData("123456", 2, 3.0, 126)]
    [InlineData("12345678", 2, 3.0, 162)]
    [InlineData("1234567890", 2, 3.0, 198)]
    [InlineData("123456789012", 2, 3.0, 234)]
    [InlineData("12345678901231", 2, 3.0, 270)]
    [InlineData("1234567890123456", 2, 3.0, 306)]
    [InlineData("12345678", 2, 2.0, 128)]
    [InlineData("12345678", 2, 2.2, 128)]
    [InlineData("12345678", 2, 2.5, 145)]
    [InlineData("12345678", 2, 2.8, 145)]
    [InlineData("12345678", 3, 3.0, 243)]
    [InlineData("12345678901231", 1, 3.0, 135)]
    public void InterleavedFootprint_MatchesTheRenderedInk(
        string data, int module, double ratio, int expectedWidth)
    {
        BarcodeElement element = Itf(data, module, ratio);
        RenderResult result = Render(element);
        Assert.Empty(result.Errors);

        (int inkX, int inkWidth) = InkColumns(result);

        Assert.Equal(100, inkX);
        Assert.Equal(expectedWidth, inkWidth);
        Assert.Equal(expectedWidth, new ElementBoundsCalculator().GetBounds(element).Width);
    }

    /// <summary>
    /// An odd digit count is not an error to a printer. The manual is explicit: "The
    /// printer automatically adds a leading 0 (zero) if an odd number of digits is
    /// received", so the label prints and scans as a longer number than the one typed.
    ///
    /// The offline renderer refuses it instead, and refuses the whole label rather than
    /// the field: one odd barcode and there is no image at all. That is why it is worth a
    /// warning of its own rather than being left to look like the app failing.
    /// </summary>
    [Fact]
    public void AnOddDigitCount_PadsOnAPrinterAndBlanksThePreview()
    {
        Assert.Equal("01234567", Interleaved2of5.Encoded("1234567", addCheckDigit: false));

        RenderResult result = Render(Itf("1234567"));

        Assert.Empty(result.Png);
        Assert.NotEmpty(result.Errors);

        string? warning = BarcodeValidator.Validate(Itf("1234567"));
        Assert.NotNull(warning);
        Assert.Contains("01234567", warning, StringComparison.Ordinal);
    }

    /// <summary>
    /// ^B2's check-digit parameter is ignored by the offline renderer: the same eight
    /// digits draw byte-identically whether the command asks for one or not. A printer
    /// does not ignore it, and the extra digit makes the count odd, so it also gets the
    /// leading zero - two more digits, one more pair of bars, a symbol 36 dots wider than
    /// the preview at this size.
    ///
    /// The footprint deliberately follows the printer rather than the ink. A box that is
    /// wider than the preview is visible and explained; a box that says a label fits when
    /// the printed symbol does not is the failure that matters, and it is silent.
    /// </summary>
    [Fact]
    public void TheRendererIgnoresTheCheckDigit_SoTheFootprintFollowsThePrinter()
    {
        RenderResult plain = Render(Itf("12345678"));
        RenderResult asked = Render(Itf("12345678", checkDigit: true));

        Assert.Equal(plain.Png, asked.Png);

        var bounds = new ElementBoundsCalculator();
        Assert.Equal(162, bounds.GetBounds(Itf("12345678")).Width);
        Assert.Equal(198, bounds.GetBounds(Itf("12345678", checkDigit: true)).Width);

        // The order is the printer's: the digit is worked out over the data, and the zero
        // that evens the count is added afterwards, so it is not part of what was checked.
        Assert.Equal("0123456784", Interleaved2of5.Encoded("12345678", addCheckDigit: true));
        Assert.Equal(4, BarcodeCheckDigit.ModuloTen("12345678"));
    }

    /// <summary>ITF-14, which is this symbology at fourteen digits and the reason it is
    /// worth having: thirteen digits of GTIN and the check digit that completes it.</summary>
    [Fact]
    public void Itf14_IsThirteenDigitsPlusItsCheckDigit()
    {
        BarcodeElement element = Itf("1234567890123");

        Assert.Equal("12345678901231", BarcodeCheckDigit.Complete(element));

        element.Data = "12345678901231";
        Assert.Null(BarcodeValidator.Validate(element));
        Assert.Equal(14, Interleaved2of5.Encoded(element).Length);
    }

    /// <summary>Digits only, and the message names the symbology rather than leaving a
    /// blank preview to explain itself.</summary>
    [Fact]
    public void InterleavedRejectsAnythingButDigits()
    {
        Assert.NotNull(BarcodeValidator.Validate(Itf("12AB")));
        Assert.Empty(Render(Itf("12AB")).Png);

        // A marker is substituted before anything is rendered, so it is not judged here.
        Assert.Null(BarcodeValidator.Validate(
            BarcodeSymbology.Interleaved2of5, "##CAIXA##"));
    }

    /// <summary>
    /// What ^FD actually carries for the UPC/EAN family, measured rather than read off
    /// the manual, because it is what makes offering to complete the number safe.
    ///
    /// The manual says field data is "limited to exactly 12 characters" for ^BE and 11
    /// for ^BU, and that ZPL "truncates or pads on the left with zeros". Rendered, a full
    /// thirteen-digit EAN draws byte-identically to its first twelve digits: the check
    /// digit sent is discarded and recomputed. So writing it into the data changes what a
    /// person reads on screen and not one bar of what prints.
    /// </summary>
    [Fact]
    public void TheUpcEanFamilyTruncatesTheCheckDigitAndRecomputesIt()
    {
        static byte[] Png(BarcodeSymbology symbology, string data)
        {
            var document = new LabelDocument { WidthMm = 100, HeightMm = 40, Dpmm = 8 };
            document.Elements.Add(new BarcodeElement
            {
                X = 50, Y = 50, Symbology = symbology, Data = data,
                HeightDots = 80, ModuleWidthDots = 2, PrintInterpretationLine = false,
            });

            return new BinaryKitsRenderer()
                .Render(new ZplGenerator().Generate(document), 100, 40, 8).Png;
        }

        Assert.Equal(
            Png(BarcodeSymbology.Ean13, "590123412345"),
            Png(BarcodeSymbology.Ean13, "5901234123457"));
        Assert.Equal(
            Png(BarcodeSymbology.UpcA, "03600029145"),
            Png(BarcodeSymbology.UpcA, "036000291452"));

        // Left-padding is the other half of the same sentence: eleven digits and the same
        // eleven behind a zero are one symbol.
        Assert.Equal(
            Png(BarcodeSymbology.Ean13, "59012341234"),
            Png(BarcodeSymbology.Ean13, "059012341234"));
    }

    /// <summary>
    /// Completing the number is offered exactly where it is both possible and useful, and
    /// nowhere else. The cases that return nothing are the point: a value that already
    /// carries its digit, a marker standing in for one, a symbology with no check digit,
    /// and a ^B2 that has already asked the printer for one.
    /// </summary>
    [Theory]
    [InlineData(BarcodeSymbology.Ean13, "590123412345", false, "5901234123457")]
    [InlineData(BarcodeSymbology.UpcA, "03600029145", false, "036000291452")]
    [InlineData(BarcodeSymbology.Interleaved2of5, "1234567890123", false, "12345678901231")]
    [InlineData(BarcodeSymbology.Ean13, "5901234123457", false, null)]
    [InlineData(BarcodeSymbology.UpcA, "036000291452", false, null)]
    [InlineData(BarcodeSymbology.Interleaved2of5, "1234567890123", true, null)]
    [InlineData(BarcodeSymbology.Ean13, "##EAN##", false, null)]
    [InlineData(BarcodeSymbology.Code128, "12345678", false, null)]
    [InlineData(BarcodeSymbology.Code39, "12345678", false, null)]
    [InlineData(BarcodeSymbology.Ean13, "", false, null)]
    public void CompleteOffersTheCheckDigitOnlyWhereItBelongs(
        BarcodeSymbology symbology, string data, bool checkDigit, string? expected)
    {
        var element = new BarcodeElement
        {
            Symbology = symbology, Data = data, AddCheckDigit = checkDigit,
        };

        Assert.Equal(expected, BarcodeCheckDigit.Complete(element));
    }

    /// <summary>
    /// The line the panel shows. It states the whole number rather than the digit alone,
    /// because the whole number is the thing somebody checks against a purchase order,
    /// and for EAN-13 and UPC-A it is never the string in the data box.
    /// </summary>
    [Fact]
    public void DescribeNamesTheNumberThatScans()
    {
        Assert.Contains(
            "5901234123457",
            BarcodeCheckDigit.Describe(new BarcodeElement
            {
                Symbology = BarcodeSymbology.Ean13, Data = "590123412345",
            }),
            StringComparison.Ordinal);

        Assert.Contains(
            "correct",
            BarcodeCheckDigit.Describe(new BarcodeElement
            {
                Symbology = BarcodeSymbology.Ean13, Data = "5901234123457",
            }),
            StringComparison.Ordinal);

        // ^B2 with no check digit asked for has none at all, which is worth saying
        // outright: it is the default, and it is not what a GTIN needs.
        Assert.Contains(
            "No check digit",
            BarcodeCheckDigit.Describe(Itf("12345678")),
            StringComparison.Ordinal);

        Assert.Contains(
            "0123456784",
            BarcodeCheckDigit.Describe(Itf("12345678", checkDigit: true)),
            StringComparison.Ordinal);

        // Nothing to say about a symbology that carries no check digit, or about a value
        // nobody here has yet.
        Assert.Empty(BarcodeCheckDigit.Describe(new BarcodeElement { Data = "12345678" }));
        Assert.Empty(BarcodeCheckDigit.Describe(Itf("##CAIXA##")));
    }

    /// <summary>ISO/IEC 16390 asks for ten modules either side, the same as Code 128 and
    /// Code 39, and it scales with the module width like every other one here.</summary>
    [Fact]
    public void InterleavedReservesTenModulesEitherSide()
    {
        QuietZoneMargin margin = QuietZone.For(Itf("12345678", module: 3));

        Assert.Equal(30, margin.Left);
        Assert.Equal(30, margin.Right);
        Assert.Equal(0, margin.Top);
        Assert.Equal(0, margin.Bottom);
    }

    /// <summary>
    /// Both of ^B2's arguments survive a full cycle, including the wide-bar ratio, which
    /// rides on ^BY rather than on the barcode command. A symbology that reads a ratio
    /// and does not state one is drawn at the printer's default of 3.0, which is a wider
    /// symbol than the model measured.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTripThroughZpl_IsByteIdentical(bool checkDigit)
    {
        var document = new LabelDocument { WidthMm = 100, HeightMm = 40, Dpmm = 8 };
        document.Elements.Add(Itf("12345678901231", module: 3, ratio: 2.5, checkDigit: checkDigit));

        string first = new ZplGenerator().Generate(document);
        Assert.Contains("^BY3,2.5", first, StringComparison.Ordinal);
        Assert.Contains(checkDigit ? ",N,Y" : ",N,N", first, StringComparison.Ordinal);

        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(first, dpmm: 8);
        Assert.Empty(imported.Warnings);

        var barcode = Assert.IsType<BarcodeElement>(Assert.Single(imported.Document.Elements));
        Assert.Equal(BarcodeSymbology.Interleaved2of5, barcode.Symbology);
        Assert.Equal(2.5, barcode.WideBarRatio);
        Assert.Equal(checkDigit, barcode.AddCheckDigit);
        Assert.Equal(first, new ZplGenerator().Generate(imported.Document));
    }

    /// <summary>The .lfl carries the flag too, or a saved label would come back printing
    /// a different number.</summary>
    [Fact]
    public void RoundTripThroughTheProjectFile_KeepsTheCheckDigit()
    {
        var document = new LabelDocument();
        document.Elements.Add(Itf("12345678901231", checkDigit: true));

        LabelDocument restored = LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(document));

        var barcode = Assert.IsType<BarcodeElement>(Assert.Single(restored.Elements));
        Assert.Equal(BarcodeSymbology.Interleaved2of5, barcode.Symbology);
        Assert.True(barcode.AddCheckDigit);
    }

    /// <summary>
    /// The ratio is stated for Interleaved 2 of 5 and Code 39 and for nothing else, so
    /// every label written before this symbology existed generates the bytes it always
    /// did.
    /// </summary>
    [Fact]
    public void OtherSymbologiesStillStateNoRatio()
    {
        var document = new LabelDocument { WidthMm = 100, HeightMm = 40, Dpmm = 8 };
        document.Elements.Add(new BarcodeElement
        {
            X = 10, Y = 10, Symbology = BarcodeSymbology.Code128, Data = "ABC",
            ModuleWidthDots = 2, WideBarRatio = 2.5,
        });

        Assert.Contains("^BY2^", new ZplGenerator().Generate(document), StringComparison.Ordinal);
    }

    private static (int X, int Width) InkColumns(RenderResult result)
    {
        using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
        Assert.NotNull(bitmap);

        int minX = int.MaxValue, maxX = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red >= 128)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
            }
        }

        return maxX < 0 ? (0, 0) : (minX, maxX - minX + 1);
    }
}
