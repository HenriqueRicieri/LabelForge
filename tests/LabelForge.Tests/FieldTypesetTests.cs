using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// Reading `^FT`, which is how real labels are written: across the sample corpus it
/// outnumbers `^FO` by 1304 to 477 and appears in 28 of the 30 files.
///
/// The test that carries this is not an assertion about coordinates, it is the picture.
/// Render the original, import it, regenerate, render that, and the ink has to land in
/// the same place. That fails the moment the geometry is wrong for any field type or any
/// orientation, including combinations nobody thought to write down.
///
/// A `^FT` field keeps its anchor rather than being converted to a `^FO`, so the ink test
/// now also holds the stronger property that the command comes back out as it went in.
/// What the conversion is still needed for is the canvas: the drawn top-left of a field
/// typeset by its baseline has to be worked out from its own size.
/// </summary>
public sealed class FieldTypesetTests
{
    [Theory]
    // Text, every orientation.
    [InlineData("^FT120,240^A0N,40^FDWXYZ^FS")]
    [InlineData("^FT120,240^A0R,40^FDWXYZ^FS")]
    [InlineData("^FT120,240^A0I,40^FDWXYZ^FS")]
    [InlineData("^FT120,240^A0B,40^FDWXYZ^FS")]

    // Text of other sizes, since the baseline is a fraction of the font size.
    [InlineData("^FT120,240^A0N,30^FDMantenha^FS")]
    [InlineData("^FT120,240^A0N,80^FDMantenha^FS")]
    [InlineData("^FT120,240^A0R,79,57^FDRAST^FS")]

    // Barcodes, whose anchor is the bottom of the bars.
    [InlineData("^BY2^FT120,240^BCN,80,N,N,N^FD12345678^FS")]
    [InlineData("^BY2^FT120,240^BCR,80,N,N,N^FD12345678^FS")]
    [InlineData("^BY2^FT120,240^BCI,80,N,N,N^FD12345678^FS")]
    [InlineData("^BY2^FT120,240^BCB,80,N,N,N^FD12345678^FS")]

    // A barcode with its interpretation line, which hangs below the anchor.
    [InlineData("^BY2^FT120,240^BCN,80,Y,N,N^FD12345678^FS")]

    // The two dimensional codes and the graphic primitives.
    [InlineData("^FT120,240^BQN,2,4^FDMA,HELLO^FS")]
    [InlineData("^FT120,240^BXN,6,200^FDHELLO^FS")]
    [InlineData("^BY2^FT120,240^B7N,8,2,5,,N^FDPDF417 DATA^FS")]
    [InlineData("^FT120,240^GB160,90,4,B^FS")]
    [InlineData("^FT120,240^GB200,4,4,B^FS")]
    public void AnImportedTypesetField_LandsWhereTheOriginalDrewIt(string field)
    {
        const string head = "^XA^PW1200^LL900";
        string original = head + field + "^XZ";

        DotRect before = Ink(original);
        Assert.True(before.Width > 0, "the original drew nothing, so there is nothing to compare");

        LabelDocument imported = ZplDocumentImport.FromZpl(original, dpmm: 8).Document;
        DotRect after = Ink(new ZplGenerator().Generate(imported));

        // Within a couple of dots: the conversion leans on the same footprint estimates
        // the canvas does, and a barcode's own width is what the renderer chooses.
        Assert.InRange(after.X, before.X - 2, before.X + 2);
        Assert.InRange(after.Y, before.Y - 2, before.Y + 2);
    }

    /// <summary>The gap between the two commands is not subtle: at the same coordinates a
    /// typeset field draws higher by its own ascent, which is what reading one as the
    /// other used to lose.</summary>
    [Fact]
    public void ATypesetField_DrawsHigherThanAPlainOneAtTheSameCoordinates()
    {
        var bounds = new ElementBoundsCalculator();

        DotRect typeset = bounds.GetBounds(Imported("^FT100,300^A0N,40^FDWXYZ^FS"));
        DotRect plain = bounds.GetBounds(Imported("^FO100,300^A0N,40^FDWXYZ^FS"));

        // The ascent of a 40 dot font, which is what the two commands differ by.
        Assert.Equal(29, plain.Y - typeset.Y);
        Assert.Equal(plain.X, typeset.X);
    }

    /// <summary>A plain `^FO` field is untouched, so nothing that was already read
    /// correctly moves.</summary>
    [Fact]
    public void APlainOrigin_IsLeftAlone()
    {
        Element element = Imported("^FO100,300^A0N,40^FDWXYZ^FS");

        Assert.Equal(FieldAnchor.TopLeft, element.Anchor);
        Assert.Equal(100, element.X);
        Assert.Equal(300, element.Y);
    }

    /// <summary>The origin is kept as the file wrote it, which is what lets a template
    /// field print where it printed: a `^FT` position depends on the width of whatever
    /// the marker is replaced with, and only the printer knows that.</summary>
    [Fact]
    public void ATypesetOrigin_IsKeptAsOne()
    {
        Element element = Imported("^FT100,300^A0N,40^FDWXYZ^FS");

        Assert.Equal(FieldAnchor.Baseline, element.Anchor);
        Assert.Equal(100, element.X);
        Assert.Equal(300, element.Y);
    }

    /// <summary>The two commands are modal, so a file that mixes them has to keep them
    /// straight field by field rather than latching onto whichever came first.</summary>
    [Fact]
    public void AFileMixingBoth_KeepsThemApart()
    {
        LabelDocument document = ZplDocumentImport.FromZpl(
            "^XA^PW800^LL600"
            + "^FT100,300^A0N,40^FDfirst^FS"
            + "^FO100,300^A0N,40^FDsecond^FS"
            + "^FT100,300^A0N,40^FDthird^FS"
            + "^XZ",
            8).Document;

        Assert.Equal(3, document.Elements.Count);
        Assert.Equal(
            [FieldAnchor.Baseline, FieldAnchor.TopLeft, FieldAnchor.Baseline],
            document.Elements.Select(e => e.Anchor));

        // Same coordinates, and the middle one still draws lower than its neighbours.
        var bounds = new ElementBoundsCalculator();
        Assert.All(document.Elements, e => Assert.Equal(300, e.Y));
        Assert.Equal(271, bounds.GetBounds(document.Elements[0]).Y);
        Assert.Equal(300, bounds.GetBounds(document.Elements[1]).Y);
        Assert.Equal(271, bounds.GetBounds(document.Elements[2]).Y);
    }

    /// <summary>
    /// What E1g cost before the anchor was modelled. A `^FT` field at 180 degrees is
    /// placed from its own right edge, so the printed position depends on how wide the
    /// content turns out to be. The label carries a marker; the system that prints it
    /// substitutes a value of some other length. Keeping the anchor means the printer
    /// decides, which is what the source label did.
    /// </summary>
    [Fact]
    public void ARotatedTypesetField_KeepsItsAnchorWhateverTheContentsWidth()
    {
        const string head = "^XA^PW800^LL600";
        const string tail = "^A0I,43,43^FD";

        string marker = new ZplGenerator().Generate(
            ZplDocumentImport.FromZpl($"{head}^FT340,73{tail}##PESO_LIQUIDO@0,000##^FS^XZ", 8).Document);
        string value = new ZplGenerator().Generate(
            ZplDocumentImport.FromZpl($"{head}^FT340,73{tail}12,345^FS^XZ", 8).Document);

        // The same anchor either way: the field's own width never enters into it.
        Assert.Contains("^FT340,73", marker, StringComparison.Ordinal);
        Assert.Contains("^FT340,73", value, StringComparison.Ordinal);
    }

    /// <summary>
    /// The coordinates a real file was written with come back out unchanged, which is
    /// what a round trip through this app has to mean for the ordinary case: `^FT` is
    /// 73 per cent of the corpus's field origins.
    /// </summary>
    [Theory]
    [InlineData("^FT120,240^A0N,40^FDWXYZ^FS")]
    [InlineData("^FT120,240^A0I,43,43^FD##PESO_LIQUIDO@0,000##^FS")]
    [InlineData("^BY2^FT120,240^BCB,80,Y,N,N^FD12345678^FS")]
    [InlineData("^FT120,240^BQN,2,4^FDMA,HELLO^FS")]
    [InlineData("^FT120,240^GB160,90,4,B^FS")]
    public void ATypesetField_RegeneratesTheCommandItCameFrom(string field)
    {
        string original = $"^XA^PW1200^LL900{field}^XZ";

        string generated = new ZplGenerator().Generate(
            ZplDocumentImport.FromZpl(original, dpmm: 8).Document);

        Assert.Contains("^FT120,240", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("^FO", generated, StringComparison.Ordinal);

        // And it is stable, which is the property that fails first if the anchor and the
        // geometry ever disagree.
        Assert.Equal(
            generated,
            new ZplGenerator().Generate(ZplDocumentImport.FromZpl(generated, dpmm: 8).Document));
    }

    /// <summary>A field placed by its baseline can legitimately draw above and left of
    /// its own origin, which no `^FO` can express and which the old conversion clamped to
    /// the label edge. The origin stays where the file put it; the canvas reports the
    /// overhang as clipping, which is what a printer does with it.</summary>
    [Fact]
    public void ATypesetFieldCanDrawOutsideItsOwnOrigin()
    {
        Element element = Imported("^FT40,30^A0I,40^FDWXYZ^FS");
        var document = new LabelDocument { WidthMm = 100, HeightMm = 75, Dpmm = 8 };
        DotRect bounds = new ElementBoundsCalculator().GetBounds(element);

        Assert.Equal(40, element.X);
        Assert.True(bounds.X < 0, $"a 180 degree field draws left of its anchor ({bounds.X})");

        // The origin is on the label, so it prints; the part that hangs off is clipping.
        Assert.True(ElementPlacement.IsPrintable(element, document));
        Assert.Equal(PlacementStatus.Clipped, ElementPlacement.Classify(element, bounds, document));
    }

    private static Element Imported(string field) =>
        Assert.Single(ZplDocumentImport.FromZpl($"^XA^PW800^LL600{field}^XZ", 8).Document.Elements);

    private static DotRect Ink(string zpl)
    {
        RenderResult result = new BinaryKitsRenderer().Render(zpl, 150, 112.5, 8);
        Assert.Empty(result.Errors);

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

        return maxX < 0 ? default : new DotRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
