using LabelForge.Core.Editing;
using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

public sealed class GestureLayerRenderTests
{
    [Fact]
    public void LayerOriginsUseBothAxesAndDoNotChangeThePrintedDocument()
    {
        var text = new TextElement { X = 240, Y = 320, Text = "PRINT", FontHeightDots = 30 };
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Elements = [text] };
        var generator = new ZplGenerator();
        string before = LabelDocumentJson.Serialize(document);
        string expected = "^XA\n^CI28\n^PW800\n^LL480\n^LH0,0\n^FO240,320^A0N,30^FDPRINT^FS\n^XZ";
        Assert.Equal(expected, generator.Generate(document));

        string layer = generator.GeneratePreviewLayer(document, [text], new DotRect(200, 300, 150, 90));

        Assert.Contains("^PW150\n^LL90", layer, StringComparison.Ordinal);
        Assert.Contains("^FO40,20^A0N,30", layer, StringComparison.Ordinal);
        Assert.Equal(expected, generator.Generate(document));
        Assert.Equal(before, LabelDocumentJson.Serialize(document));
    }

    [Fact]
    public void TheFullPasteboardLayerKeepsTheExistingPreviewBytes()
    {
        var offLabel = new BoxElement { X = -40, Y = -30, WidthDots = 100, HeightDots = 50, DoNotPrint = true };
        var hidden = new TextElement { Text = "HIDDEN", IsVisible = false };
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Elements = [offLabel, hidden] };
        string ordinary = new ZplGenerator().GeneratePreview(document, 160);
        string layer = new ZplGenerator().GeneratePreviewLayer(document, [offLabel, hidden],
            new DotRect(-160, -160, 1120, 800));

        Assert.Equal(ordinary, layer);
        Assert.Contains("^FO120,130", layer, StringComparison.Ordinal);
        Assert.DoesNotContain("HIDDEN", layer, StringComparison.Ordinal);
        Assert.DoesNotContain("^GB", new ZplGenerator().Generate(document), StringComparison.Ordinal);
    }

    [Fact]
    public void LayerGraphicsAreSelfContainedEvenWhenTheirTwinIsInAnotherLayer()
    {
        byte[] imageData = TestImages.HalfBlackPng();
        var first = new ImageElement { X = 30, Y = 20, WidthDots = 80, HeightDots = 10, ImageData = imageData };
        var second = new ImageElement { X = 140, Y = 20, WidthDots = 80, HeightDots = 10, ImageData = imageData };
        var document = new LabelDocument { Elements = [first, second] };
        Assert.Contains("~DG", new ZplGenerator().Generate(document), StringComparison.Ordinal);

        string layer = new ZplGenerator().GeneratePreviewLayer(document, [first], GestureLayers.GetMovingViewport([first]));

        Assert.Contains("^GF", layer, StringComparison.Ordinal);
        Assert.DoesNotContain("^XG", layer, StringComparison.Ordinal);
        Assert.DoesNotContain("~DG", layer, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> CroppedFields()
    {
        foreach (string kind in new[] { "text", "ean", "qr" })
        foreach (Orientation orientation in Enum.GetValues<Orientation>())
        foreach (FieldAnchor anchor in Enum.GetValues<FieldAnchor>())
            yield return [kind, orientation, anchor, 24];
        foreach (string kind in new[] { "image", "diagonal", "box", "line", "ellipse", "data-matrix", "pdf417", "code128" })
        foreach (int density in new[] { 8, 24 })
            yield return [kind, Orientation.Normal, FieldAnchor.TopLeft, density];
        foreach (string kind in new[] { "thick-box", "thick-line", "qr-large", "qr-high", "matrix-large", "pdf417-tall" })
            yield return [kind, Orientation.Normal, FieldAnchor.TopLeft, 24];
    }

    [Theory]
    [MemberData(nameof(CroppedFields))]
    public void CroppedPixelsRetainTheWholeField(string kind, Orientation orientation, FieldAnchor anchor, int dpmm)
    {
        Element element = Field(kind);
        element.X = -350;
        element.Y = -220;
        element.Orientation = orientation;
        element.Anchor = anchor;
        AssertCropMatchesFullRender([element], dpmm);
    }

    [Theory]
    [InlineData(Orientation.Normal)]
    [InlineData(Orientation.Rotated90)]
    [InlineData(Orientation.Rotated180)]
    [InlineData(Orientation.Rotated270)]
    public void ResolvedSamplesDetermineTheCropBeforeRendering(Orientation orientation)
    {
        var original = new TextElement
        {
            X = 70, Y = 90, Text = "##PRODUCT##", FontHeightDots = 40,
            Anchor = FieldAnchor.Baseline, Orientation = orientation,
        };
        var clone = (TextElement)LabelDocumentJson.DeserializeElements(ElementSnapshot.Capture([original]))[0];
        clone.Text = new TemplateSubstitutor().Substitute(clone.Text,
            _ => string.Join(" ", Enumerable.Repeat("Agua mineral 1.5L", 12)));

        AssertCropMatchesFullRender([clone], 24);
        Assert.Equal("##PRODUCT##", original.Text);
    }

    [Fact]
    public void ALongGapDoesNotHideTheLastGlyphWhenTheFieldMovesOntoTheLabel()
    {
        var text = new TextElement { X = -900, Y = -500, Text = "I" + new string(' ', 120) + "W", FontHeightDots = 60 };
        AssertCropMatchesFullRender([text], 24);
    }

    [Fact]
    public void MultipleFieldsRetainTheirOwnOriginsInsideOneMovingLayer()
    {
        var text = (TextElement)Field("text");
        text.X = -230;
        text.Y = 50;
        var image = Field("image");
        image.X = 110;
        image.Y = -20;
        image.ZOrder = 1;
        AssertCropMatchesFullRender([text, image], 24);
    }

    private static Element Field(string kind) => kind switch
    {
        "thick-box" => new BoxElement { WidthDots = 30, HeightDots = 40, ThicknessDots = 100 },
        "thick-line" => new LineElement { LengthDots = 30, ThicknessDots = 100 },
        "qr-large" => new QrCodeElement { Data = new string('a', 1200), Magnification = 4 },
        "qr-high" => new QrCodeElement { Data = new string('a', 100), Magnification = 6, ErrorCorrection = QrErrorCorrection.High },
        "matrix-large" => new DataMatrixElement { Data = string.Concat(Enumerable.Repeat("Ab9!z@~x", 100)), ModuleSizeDots = 4 },
        "pdf417-tall" => new Pdf417Element { Data = string.Concat(Enumerable.Repeat("Aa9!~x", 30)), ModuleWidthDots = 2, RowHeightDots = 40, DataColumns = 5 },
        "text" => new TextElement { Text = "Agua 1.25 kg jg / 123", FontHeightDots = 52, FontWidthDots = 38 },
        "ean" => new BarcodeElement { Data = "789123456789", Symbology = BarcodeSymbology.Ean13, HeightDots = 95, ModuleWidthDots = 3 },
        "code128" => new BarcodeElement { Data = "LF001234", HeightDots = 95, ModuleWidthDots = 2 },
        "qr" => new QrCodeElement { Data = "LabelForge sample", Magnification = 4 },
        "data-matrix" => new DataMatrixElement { Data = "LabelForge sample", ModuleSizeDots = 4 },
        "pdf417" => new Pdf417Element { Data = "LabelForge sample", ModuleWidthDots = 2, RowHeightDots = 8, DataColumns = 5 },
        "image" => new ImageElement { WidthDots = 79, HeightDots = 53, SourcePixelWidth = 8, SourcePixelHeight = 1, ImageData = TestImages.HalfBlackPng() },
        "diagonal" => new DiagonalLineElement { WidthDots = 190, HeightDots = 35, ThicknessDots = 29, LeansRight = false },
        "box" => new BoxElement { WidthDots = 85, HeightDots = 60, ThicknessDots = 3 },
        "line" => new LineElement { LengthDots = 95, ThicknessDots = 7, IsVertical = true },
        "ellipse" => new EllipseElement { WidthDots = 85, HeightDots = 60, ThicknessDots = 3 },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static DotRect InkBounds(RenderResult image)
    {
        int left = image.PixelWidth, top = image.PixelHeight, right = 0, bottom = 0;
        for (int y = 0; y < image.PixelHeight; y++)
        for (int x = 0; x < image.PixelWidth; x++)
        {
            int pixel = y * image.Stride + x * 4;
            if (image.Pixels![pixel] + 255 - image.Pixels[pixel + 3] < 128)
            {
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
            }
        }
        return new DotRect(left, top, right - left, bottom - top);
    }
    private static void AssertCropMatchesFullRender(IReadOnlyList<Element> elements, int dpmm)
    {
        DotRect crop = GestureLayers.GetMovingViewport(elements);
        const int guard = 200;
        var full = new DotRect(crop.X - guard, crop.Y - guard, crop.Width + 2 * guard, crop.Height + 2 * guard);
        var document = new LabelDocument { Dpmm = dpmm, Elements = elements.ToList() };
        string snapshot = LabelDocumentJson.Serialize(document);
        RenderResult layer = new BinaryKitsRenderer().Render(
            new ZplGenerator().GeneratePreviewLayer(document, elements, crop),
            (double)crop.Width / dpmm, (double)crop.Height / dpmm, dpmm, output: RenderOutput.TransparentPixels);

        List<Element> shifted = LabelDocumentJson.DeserializeElements(ElementSnapshot.Capture(elements));
        foreach (Element element in shifted)
        {
            element.X -= full.X;
            element.Y -= full.Y;
        }
        var referenceDocument = new LabelDocument
        {
            WidthMm = (double)full.Width / dpmm, HeightMm = (double)full.Height / dpmm,
            Dpmm = dpmm, Elements = shifted,
        };
        RenderResult reference = new BinaryKitsRenderer().Render(
            new ZplGenerator().GeneratePreview(referenceDocument, 0),
            referenceDocument.WidthMm, referenceDocument.HeightMm, dpmm, output: RenderOutput.Pixels);
        Assert.Empty(layer.Errors);
        Assert.Empty(reference.Errors);
        Assert.NotNull(layer.Pixels);
        Assert.NotNull(reference.Pixels);
        Assert.True(layer.HasImage);
        Assert.Equal(crop.Width, layer.PixelWidth);
        Assert.Equal(crop.Height, layer.PixelHeight);

        int differences = 0, solidDifferences = 0, outsideDifferences = 0;
        int ink = 0;
        for (int y = 0; y < full.Height; y++)
        for (int x = 0; x < full.Width; x++)
        {
            int actual = y * reference.Stride + x * 4;
            int localX = x - guard, localY = y - guard;
            bool inside = localX >= 0 && localX < crop.Width && localY >= 0 && localY < crop.Height;
            int pixel = inside ? localY * layer.Stride + localX * 4 : 0;
            for (int channel = 0; channel < 3; channel++)
            {
                int expected = inside ? layer.Pixels[pixel + channel] + 255 - layer.Pixels[pixel + 3] : 255;
                if (reference.Pixels[actual + channel] != expected) differences++;
                if ((reference.Pixels[actual + channel] == 0 && expected == 255) || (reference.Pixels[actual + channel] == 255 && expected == 0)) solidDifferences++;
                if (!inside && reference.Pixels[actual + channel] != 255) outsideDifferences++;
            }
            if (reference.Pixels[actual] < 128) ink++;
        }
        Assert.True(ink > 0, "The reference must draw visible ink.");
        Assert.Equal(0, outsideDifferences);
        Assert.Equal(0, solidDifferences);
        DotRect inkBounds = InkBounds(layer);
        Assert.Equal(inkBounds with { X = inkBounds.X + guard, Y = inkBounds.Y + guard }, InkBounds(reference));

        // Skia changes edge coverage slightly when these shapes are translated to a
        // smaller surface. Permit only antialiased edges, with unchanged ink bounds
        // and fewer than 2% of ink pixels affected. Every other fixture stays exact.
        bool translatedEdges = elements.Count == 1 && elements[0] is EllipseElement
            or BarcodeElement { Symbology: BarcodeSymbology.Ean13, Orientation: Orientation.Rotated90 };
        int allowedChannels = translatedEdges ? (int)(ink * 0.02) * 3 : 0;
        Assert.True(differences <= allowedChannels,
            $"Crop changed {differences} channels, allowed {allowedChannels}; viewport {crop}.");
        Assert.Equal(snapshot, LabelDocumentJson.Serialize(document));
    }
}
