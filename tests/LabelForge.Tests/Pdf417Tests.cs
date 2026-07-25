using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// PDF417 (^B7). The symbol's shape is not decided by the data alone the way a QR
/// code's is: the column count picks it, so most of what is worth testing here is that
/// the model, the footprint math and the renderer agree about what that count produces.
/// </summary>
public sealed class Pdf417Tests
{
    private static LabelDocument Label(params Element[] elements)
    {
        var document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        foreach (Element element in elements)
        {
            document.Elements.Add(element);
        }

        return document;
    }

    [Fact]
    public void Generate_EmitsModuleWidthThenTheSymbol()
    {
        var document = Label(new Pdf417Element
        {
            X = 40, Y = 40, Data = "LF-000123", ModuleWidthDots = 3, RowHeightDots = 10,
            SecurityLevel = 4, DataColumns = 6,
        });

        string zpl = new ZplGenerator().Generate(document);

        // The empty fifth argument is the row count, left to the printer on purpose.
        Assert.Contains("^BY3^FO40,40^B7N,10,4,6,,N^FDLF-000123^FS", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_LeavesTheColumnArgumentEmptyWhenAutomatic()
    {
        var document = Label(new Pdf417Element
        {
            X = 10, Y = 10, Data = "AUTO", DataColumns = 0, Truncate = true,
            Orientation = Orientation.Rotated90,
        });

        Assert.Contains("^B7R,8,2,,,Y^FDAUTO^FS", new ZplGenerator().Generate(document),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The WYSIWYG guard, and the reason this element could be built at all: the offline
    /// renderer has to draw ^B7, because the canvas is the render of the ZPL we generate.
    /// </summary>
    [Fact]
    public void Renderer_DrawsTheSymbol()
    {
        var document = Label(new Pdf417Element { X = 40, Y = 40, Data = "LF-000123" });

        RenderResult result = new BinaryKitsRenderer()
            .Render(new ZplGenerator().Generate(document), 100, 60, 8);

        Assert.Empty(result.Errors);
        Assert.True(InkBox(result) is { Width: > 0 }, "the renderer drew nothing for ^B7");
    }

    /// <summary>
    /// The footprint against the ink, which is what makes the selection outline honest.
    ///
    /// The width is exact arithmetic and is asserted as such: a row is 17 modules per
    /// data column plus the start pattern, both row indicators and the stop pattern,
    /// which truncating shortens. The row count rests on an estimate of how many
    /// codewords the data occupies, so it is allowed one row of slack; on everything
    /// here but the punctuation-heavy string it lands exactly.
    /// </summary>
    [Theory]
    [InlineData("LABEL PAYLOAD 12345", 5, 2, false)]
    [InlineData("LABEL PAYLOAD 12345", 5, 0, false)]
    [InlineData("LABEL PAYLOAD 12345", 5, 4, false)]
    [InlineData("LABEL PAYLOAD 12345", 5, 2, true)]
    [InlineData("LABEL PAYLOAD 12345", 10, 2, false)]
    [InlineData("0123456789012345678901234567890123456789", 5, 2, false)]
    [InlineData("MIXED Case Data 4321 with punctuation, and more!", 5, 2, false)]
    [InlineData("AB", 5, 2, false)]

    // Automatic columns, where the footprint has to follow what the renderer draws
    // rather than what the printer might: these are the cases that set that rule.
    [InlineData("LF-000123", 0, 2, false)]
    [InlineData("LABEL PAYLOAD 12345", 0, 0, false)]
    [InlineData("LABEL PAYLOAD 12345", 0, 5, false)]
    [InlineData("SHORT", 0, 5, false)]
    public void Bounds_MatchTheRenderedInk(string data, int columns, int security, bool truncate)
    {
        var element = new Pdf417Element
        {
            X = 40, Y = 40, Data = data, DataColumns = columns,
            SecurityLevel = security, Truncate = truncate,
        };

        RenderResult result = new BinaryKitsRenderer()
            .Render(new ZplGenerator().Generate(Label(element)), 200, 120, 8);
        Assert.Empty(result.Errors);

        DotRect ink = InkBox(result);
        DotRect bounds = new ElementBoundsCalculator().GetBounds(element);

        Assert.Equal(40, ink.X);
        Assert.Equal(40, ink.Y);
        Assert.Equal(bounds.Width, ink.Width);

        int rowHeight = element.RowHeightDots;
        Assert.InRange(bounds.Height, ink.Height - rowHeight, ink.Height + rowHeight);
    }

    /// <summary>Rotating swaps the footprint, the same approximation the other element
    /// types use, and here it is exactly right because the symbol is a rectangle.</summary>
    [Fact]
    public void Bounds_SwapWhenRotated()
    {
        var upright = new Pdf417Element { Data = "LF-000123", DataColumns = 5 };
        var turned = new Pdf417Element
        {
            Data = "LF-000123", DataColumns = 5, Orientation = Orientation.Rotated270,
        };

        var calculator = new ElementBoundsCalculator();
        DotRect flat = calculator.GetBounds(upright);
        DotRect side = calculator.GetBounds(turned);

        Assert.Equal(flat.Width, side.Height);
        Assert.Equal(flat.Height, side.Width);
    }

    /// <summary>A stacked symbol quantizes on both axes independently: whole modules
    /// across, whole rows down. Dragging a handle has to land on one of those.</summary>
    [Fact]
    public void Resize_QuantizesToModulesAndRows()
    {
        var element = new Pdf417Element { Data = "LF-000123", DataColumns = 5, SecurityLevel = 4 };
        Pdf417Shape shape = Pdf417Metrics.Measure(element);
        int modules = Pdf417Metrics.WidthModules(shape.Columns, truncate: false);

        // Ask for three modules across and twelve dots per row, overshooting both by
        // less than half a step so the gesture has to snap back down to them.
        ElementResizer.Resize(element, modules * 3 + 20, shape.Rows * 12 + 3);

        Assert.Equal(3, element.ModuleWidthDots);
        Assert.Equal(12, element.RowHeightDots);
        Assert.Equal(modules * 3, new ElementBoundsCalculator().GetBounds(element).Width);
    }

    [Fact]
    public void Measure_ReportsWhenTheDataCannotFit()
    {
        var element = new Pdf417Element
        {
            Data = new string('X', 4000), DataColumns = 5, SecurityLevel = 8,
        };

        Assert.True(Pdf417Metrics.Measure(element).OverCapacity);
        Assert.False(Pdf417Metrics.Measure(
            new Pdf417Element { Data = "LF-000123", DataColumns = 5 }).OverCapacity);
    }

    /// <summary>Automatic columns are flagged, because the printer picks the shape and
    /// nothing on this side can promise which one.</summary>
    [Fact]
    public void Measure_FlagsAnAutomaticColumnCount()
    {
        Assert.True(Pdf417Metrics.Measure(
            new Pdf417Element { Data = "LF-000123", DataColumns = 0 }).ColumnsAreAutomatic);
        Assert.False(Pdf417Metrics.Measure(
            new Pdf417Element { Data = "LF-000123", DataColumns = 5 }).ColumnsAreAutomatic);
    }

    [Fact]
    public void Import_ReadsEveryParameterBack()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^PW800^LL480^BY4^FO60,70^B7I,12,6,7,,Y^FDCARGO-42^FS^XZ", dpmm: 8);

        var element = Assert.IsType<Pdf417Element>(Assert.Single(result.Document.Elements));
        Assert.Equal("CARGO-42", element.Data);
        Assert.Equal(60, element.X);
        Assert.Equal(70, element.Y);
        Assert.Equal(Orientation.Rotated180, element.Orientation);
        Assert.Equal(4, element.ModuleWidthDots);
        Assert.Equal(12, element.RowHeightDots);
        Assert.Equal(6, element.SecurityLevel);
        Assert.Equal(7, element.DataColumns);
        Assert.True(element.Truncate);
        Assert.Empty(result.Warnings);
    }

    /// <summary>An omitted argument falls back to what ZPL means by it, not to what this
    /// model would have chosen: an imported label has to keep printing what it printed,
    /// and ZPL's security level really is 0.</summary>
    [Fact]
    public void Import_UsesZplsOwnDefaults_NotTheDesignersDefaults()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^FO10,10^B7^FDPLAIN^FS^XZ", dpmm: 8);

        var element = Assert.IsType<Pdf417Element>(Assert.Single(result.Document.Elements));
        Assert.Equal(0, element.SecurityLevel);
        Assert.Equal(0, element.DataColumns);
        Assert.False(element.Truncate);
    }

    /// <summary>A row count is the other way a file can pin the shape, and this model has
    /// no room for it, so it is named rather than dropped in silence.</summary>
    [Fact]
    public void Import_ReportsARowCountItCannotKeep()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^FO10,10^B7N,8,2,,20,N^FDROWS^FS^XZ", dpmm: 8);

        Assert.Single(result.Document.Elements);
        Assert.Contains(result.Warnings, w => w.Contains("20 rows", StringComparison.Ordinal));
    }

    [Fact]
    public void RoundTrip_ThroughTheProjectFile()
    {
        var document = new LabelDocument();
        document.Elements.Add(new Pdf417Element
        {
            X = 12, Y = 34, Data = "##NOTA##", ModuleWidthDots = 3, RowHeightDots = 9,
            SecurityLevel = 6, DataColumns = 8, Truncate = true,
        });

        LabelDocument restored = LabelDocumentJson.Deserialize(
            LabelDocumentJson.Serialize(document));

        var element = Assert.IsType<Pdf417Element>(Assert.Single(restored.Elements));
        Assert.Equal("##NOTA##", element.Data);
        Assert.Equal(3, element.ModuleWidthDots);
        Assert.Equal(9, element.RowHeightDots);
        Assert.Equal(6, element.SecurityLevel);
        Assert.Equal(8, element.DataColumns);
        Assert.True(element.Truncate);
    }

    [Fact]
    public void Markers_InThePayloadAreFoundByTheVariablesPanel()
    {
        var document = Label(new Pdf417Element { Data = "NF ##NOTA## / ##SERIE##" });

        Assert.Equal(
            ["NOTA", "SERIE"],
            Core.Templating.TemplateVariables.Discover(document).Order().ToArray());
    }

    /// <summary>The ink's bounding box in dot space, at the 1:1 density the renderer is
    /// given here.</summary>
    private static DotRect InkBox(RenderResult result)
    {
        using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
        Assert.NotNull(bitmap);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red >= 128)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0
            ? default
            : new DotRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
