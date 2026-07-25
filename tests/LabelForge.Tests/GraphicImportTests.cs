using LabelForge.Core.Imaging;
using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// Reading someone else's label: finding its graphics, recovering the bitmaps, and
/// turning them into elements the designer can move. The inputs here are shaped like
/// real driver output, because that is the only kind this code ever sees.
/// </summary>
public sealed class GraphicImportTests
{
    /// <summary>A 32x4 checker as a ~DG payload, written the terse way a driver does.</summary>
    private const string Checker = "F0F0F0F0" + "0F0F0F0F" + ":" + "F0F0F0F0";

    private static string Download(string name) => $"~DG{name},16,4,{Checker}";

    [Fact]
    public void Scan_FindsADownloadAndWhereItIsDrawn()
    {
        string zpl = $"{Download("LOGO")}\n^XA\n^FO120,60^XGLOGO,1,1^FS\n^XZ";

        ZplGraphicScan scan = ZplGraphicScanner.Scan(zpl);

        ZplGraphicDefinition definition = Assert.Single(scan.Definitions);
        Assert.Equal("LOGO", definition.Name);
        Assert.Equal(16, definition.TotalBytes);
        Assert.Equal(4, definition.BytesPerRow);

        ZplGraphicPlacement placement = Assert.Single(scan.Placements);
        Assert.Equal("LOGO", placement.Name);
        Assert.Equal(120, placement.X);
        Assert.Equal(60, placement.Y);
        Assert.Empty(scan.UnresolvedNames);
    }

    [Fact]
    public void Scan_MatchesQualifiedAndBareNamesAsOneGraphic()
    {
        string zpl = $"{Download("R:LOGO.GRF")}\n^XA\n^FO0,0^XGLOGO,1,1^FS\n^XZ";

        ZplGraphicScan scan = ZplGraphicScanner.Scan(zpl);

        Assert.Equal("LOGO", Assert.Single(scan.Definitions).Name);
        Assert.Empty(scan.UnresolvedNames);
    }

    [Fact]
    public void Scan_ReportsAGraphicThatOnlyExistsInThePrinter()
    {
        string zpl = "^XA\n^FO10,10^XGCARIMBO2,1,1^FS\n^XZ";

        ZplGraphicScan scan = ZplGraphicScanner.Scan(zpl);

        Assert.Empty(scan.Definitions);
        Assert.Equal("CARIMBO2", Assert.Single(scan.UnresolvedNames));
    }

    [Fact]
    public void Scan_CountsLabelBlocksAndTracksLabelHome()
    {
        string zpl = $"^XA\n^XZ\n{Download("LOGO")}\n^XA\n^LH20,30\n^FO10,10^XGLOGO,1,1^FS\n^XZ";

        ZplGraphicPlacement placement = Assert.Single(ZplGraphicScanner.Scan(zpl).Placements);

        Assert.Equal(1, placement.LabelIndex);
        Assert.Equal(30, placement.X);
        Assert.Equal(40, placement.Y);
    }

    [Fact]
    public void Scan_ForgetsTheOriginAfterTheFieldEnds()
    {
        string zpl = $"{Download("LOGO")}\n^XA\n^FO500,500^GB10,10,1^FS\n^XGLOGO,1,1^FS\n^XZ";

        ZplGraphicPlacement placement = Assert.Single(
            ZplGraphicScanner.Scan(zpl).Placements, p => p.Name == "LOGO");

        Assert.Equal(0, placement.X);
        Assert.Equal(0, placement.Y);
    }

    [Fact]
    public void Scan_ReadsAnInlineGraphicField()
    {
        string zpl = $"^XA\n^FO40,50^GFA,16,16,4,{Checker}^FS\n^XZ";

        ZplGraphicPlacement placement = Assert.Single(ZplGraphicScanner.Scan(zpl).Placements);

        Assert.Null(placement.Name);
        Assert.NotNull(placement.Inline);
        Assert.Equal(4, placement.Inline.BytesPerRow);
        Assert.Equal(40, placement.X);
        Assert.Equal(50, placement.Y);
    }

    [Fact]
    public void Import_ProducesAnImageElementAtItsPlace()
    {
        string zpl = $"{Download("LOGO")}\n^XA\n^FO120,60^XGLOGO,2,3^FS\n^XZ";

        ZplGraphicImportResult result = ZplGraphicImport.FromZpl(zpl);

        ImportedGraphic imported = Assert.Single(result.Graphics);
        Assert.Equal("LOGO", imported.Name);
        Assert.Equal(1, imported.Placements);
        Assert.Empty(result.Warnings);

        ImageElement element = imported.Element;
        Assert.Equal("LOGO", element.Name);
        Assert.Equal(120, element.X);
        Assert.Equal(60, element.Y);

        // Magnification is baked in at import, one dot per pixel, so nothing has to
        // resample the bitmap on the way back out.
        Assert.Equal(64, element.SourcePixelWidth);
        Assert.Equal(12, element.SourcePixelHeight);
        Assert.Equal(64, element.WidthDots);
        Assert.Equal(12, element.HeightDots);

        // Already one bit deep: diffusing it would invent texture that is not there.
        Assert.Equal(DitherMode.Threshold, element.Dithering);
        Assert.NotEmpty(element.ImageData);
    }

    [Fact]
    public void Import_RecoversTheOriginalBitsThroughThePng()
    {
        string zpl = $"{Download("LOGO")}\n^XA\n^FO0,0^XGLOGO,1,1^FS\n^XZ";

        ImageElement element = Assert.Single(ZplGraphicImport.FromZpl(zpl).Graphics).Element;

        // Round-trip the element the way the generator will: decode, threshold, pack.
        byte[]? gray = ImageRasterizer.ToGrayscale(element.ImageData, 32, 4);
        Assert.NotNull(gray);
        bool[] recovered = ImageDitherer.Dither(gray, 32, 4, DitherMode.Threshold);

        GraphicBitmap? original = GraphicField.Decode(Checker, 4, 16);
        Assert.NotNull(original);
        Assert.Equal(original.Black, recovered);
    }

    [Fact]
    public void Import_KeepsAGraphicTheLabelNeverDrawsAndSaysSo()
    {
        string zpl = $"{Download("SPARE")}\n^XA\n^FO0,0^GB10,10,1^FS\n^XZ";

        ZplGraphicImportResult result = ZplGraphicImport.FromZpl(zpl);

        ImportedGraphic imported = Assert.Single(result.Graphics);
        Assert.Equal(0, imported.Placements);
        Assert.Equal(0, imported.Element.X);
        Assert.Contains("never placed", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void Import_NamesTheGraphicsItCannotFind()
    {
        string zpl = "^XA\n^FO10,10^XGCARIMBO2,1,1^FS\n^FO10,300^XGSIFNV,1,1^FS\n^XZ";

        ZplGraphicImportResult result = ZplGraphicImport.FromZpl(zpl);

        Assert.Empty(result.Graphics);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("CARIMBO2", warning, StringComparison.Ordinal);
        Assert.Contains("SIFNV", warning, StringComparison.Ordinal);
        Assert.Contains("2 graphics are", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_ClampsAnOriginThatWouldSitOffTheTopLeft()
    {
        string zpl = $"{Download("LOGO")}\n^XA\n^FO-40,-10^XGLOGO,1,1^FS\n^XZ";

        ImageElement element = Assert.Single(ZplGraphicImport.FromZpl(zpl).Graphics).Element;

        Assert.Equal(0, element.X);
        Assert.Equal(0, element.Y);
    }

    [Fact]
    public void Import_OfZplWithoutGraphicsIsEmptyRatherThanAnError()
    {
        ZplGraphicImportResult result = ZplGraphicImport.FromZpl("^XA\n^FO10,10^FDhello^FS\n^XZ");

        Assert.Empty(result.Graphics);
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// The short name form is what real labels are written with, and BinaryKits does not
    /// resolve it, so before this the viewer drew those labels with every logo silently
    /// missing. The fix belongs to the renderer, and it must not alter the caller's ZPL.
    /// </summary>
    [Fact]
    public void Renderer_DrawsAGraphicRecalledByItsShortName()
    {
        string zpl = $"~DGLOGO,64,4,{string.Concat(Enumerable.Repeat("FFFFFFFF", 16))}"
                     + "\n^XA\n^FO24,16^XGLOGO,1,1^FS\n^XZ";

        RenderResult result = new BinaryKitsRenderer().Render(zpl, 60, 40, 8);

        Assert.Empty(result.Errors);

        // 32 x 16 dots of solid black.
        Assert.Equal(512, BlackPixels(result.Png));
    }

    /// <summary>The committed fixture written the way real labels are: bare names,
    /// driver row compression, one stamp used twice, one that only the printer has.</summary>
    [Fact]
    public void Import_HandlesAFixtureShapedLikeARealLabel()
    {
        string zpl = File.ReadAllText(
            Path.Combine(TestCorpus.FixturesDirectory(), "embedded-graphic-short-name.zpl"));

        ZplGraphicImportResult result = ZplGraphicImport.FromZpl(zpl);

        ImportedGraphic imported = Assert.Single(result.Graphics);
        Assert.Equal("STAMP", imported.Name);
        Assert.Equal(2, imported.Placements);
        Assert.Equal(24, imported.Element.SourcePixelWidth);
        Assert.Equal(8, imported.Element.SourcePixelHeight);

        // Placed at the first recall, not at the second.
        Assert.Equal(40, imported.Element.X);
        Assert.Equal(100, imported.Element.Y);

        Assert.Contains("CARIMBO", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void QualifyGraphicNames_TouchesOnlyTheNames()
    {
        string zpl = $"{Download("LOGO")}\n^XA\n^FO120,60^XGLOGO,1,1^FS\n^FD##TEXT##^FS\n^XZ";

        string qualified = ZplGraphicScanner.QualifyGraphicNames(zpl);

        Assert.Contains("~DGR:LOGO.GRF,16,4,", qualified, StringComparison.Ordinal);
        Assert.Contains("^FO120,60^XGR:LOGO.GRF,1,1^FS", qualified, StringComparison.Ordinal);
        Assert.Contains(Checker, qualified, StringComparison.Ordinal);
        Assert.Contains("^FD##TEXT##^FS", qualified, StringComparison.Ordinal);
    }

    [Fact]
    public void QualifyGraphicNames_LeavesZplWithoutGraphicsAlone()
    {
        const string zpl = "^XA\n^FO10,10^A0N,30^FDhello^FS\n^XZ";

        Assert.Same(zpl, ZplGraphicScanner.QualifyGraphicNames(zpl));
    }

    /// <summary>
    /// The whole point of the feature, end to end: take a label somebody else's driver
    /// wrote, pull its graphic into a document, generate our own ZPL from that document,
    /// and get back exactly the dots the source label prints. Magnification is in the
    /// fixture because that is where a resample would show up as softened edges.
    ///
    /// The comparison is against the bits, not against rendering the source: BinaryKits
    /// draws ^XG at its stored size and ignores the magnification, so the source render
    /// is not a usable oracle here (see <see cref="Renderer_IgnoresXgMagnification"/>).
    /// </summary>
    [Fact]
    public void ImportedGraphic_RegeneratesTheSameDots()
    {
        string source = File.ReadAllText(
            Path.Combine(TestCorpus.FixturesDirectory(), "embedded-graphic.zpl"));

        ImportedGraphic imported = Assert.Single(ZplGraphicImport.FromZpl(source).Graphics);

        // An 8x8 hollow square (FF 81 81 81 81 81 81 FF) recalled at magnification 4.
        Assert.Equal(32, imported.Element.WidthDots);
        Assert.Equal(32, imported.Element.HeightDots);

        bool[] expected = Magnified(
            GraphicField.Decode("FF818181818181FF", 1, 8)!, 4);

        // Straight through the generator's own path: decode the element, threshold it.
        byte[]? gray = ImageRasterizer.ToGrayscale(imported.Element.ImageData, 32, 32);
        Assert.NotNull(gray);
        Assert.Equal(expected, ImageDitherer.Dither(gray, 32, 32, DitherMode.Threshold));

        // And the ZPL that comes out carries those dots at the source label's origin.
        var document = new LabelDocument { WidthMm = 50, HeightMm = 37.5, Dpmm = 8 };
        document.Elements.Add(imported.Element);
        string generated = new ZplGenerator().Generate(document);

        Assert.Contains(
            "^FO40,120^GFA,128,128,4," + GraphicField.Encode(expected, 32, 32),
            generated,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A limitation of the offline renderer, pinned so it is a known quantity rather
    /// than a surprise: BinaryKits draws a recalled graphic at its stored size whatever
    /// magnification ^XG asks for. It costs us nothing on our own output, which always
    /// recalls at 1,1, and nothing on the corpus, which never magnifies either. It does
    /// mean the viewer under-draws a foreign label that magnifies a stamp.
    /// </summary>
    [Fact]
    public void Renderer_IgnoresXgMagnification()
    {
        const string graphic = "~DGLOGO,8,1,FFFFFFFFFFFFFFFF\n^XA\n^FO0,0^XGLOGO,";
        var renderer = new BinaryKitsRenderer();

        int atOne = BlackPixels(renderer.Render($"{graphic}1,1^FS\n^XZ", 20, 12, 8).Png);
        int atFour = BlackPixels(renderer.Render($"{graphic}4,4^FS\n^XZ", 20, 12, 8).Png);

        Assert.Equal(64, atOne);
        Assert.Equal(atOne, atFour);
    }

    private static bool[] Magnified(GraphicBitmap bitmap, int factor)
    {
        int width = bitmap.Width * factor;
        var black = new bool[width * bitmap.Height * factor];
        for (int y = 0; y < bitmap.Height * factor; y++)
        {
            for (int x = 0; x < width; x++)
            {
                black[y * width + x] = bitmap.Black[y / factor * bitmap.Width + x / factor];
            }
        }

        return black;
    }

    private static int BlackPixels(byte[] png)
    {
        using SKBitmap? bitmap = SKBitmap.Decode(png);
        Assert.NotNull(bitmap);

        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
