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
/// Render the original, import it, regenerate as `^FO`, render that, and the ink has to
/// land in the same place. That fails the moment the conversion is wrong for any field
/// type or any orientation, including combinations nobody thought to write down.
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

    /// <summary>Reading `^FT` as `^FO` is what this replaces, and the gap it left was not
    /// subtle: a field landed low by its own height.</summary>
    [Fact]
    public void ReadingItAsAPlainOrigin_WouldHaveBeenWrongByTheFieldsHeight()
    {
        LabelDocument typeset =
            ZplDocumentImport.FromZpl("^XA^PW800^LL600^FT100,300^A0N,40^FDWXYZ^FS^XZ", 8).Document;
        LabelDocument plain =
            ZplDocumentImport.FromZpl("^XA^PW800^LL600^FO100,300^A0N,40^FDWXYZ^FS^XZ", 8).Document;

        int typesetY = Assert.Single(typeset.Elements).Y;
        int plainY = Assert.Single(plain.Elements).Y;

        // The ascent of a 40 dot font, which is what the two commands differ by.
        Assert.Equal(29, plainY - typesetY);
    }

    /// <summary>A plain `^FO` field is untouched, so nothing that was already read
    /// correctly moves.</summary>
    [Fact]
    public void APlainOrigin_IsLeftAlone()
    {
        LabelDocument document =
            ZplDocumentImport.FromZpl("^XA^PW800^LL600^FO100,300^A0N,40^FDWXYZ^FS^XZ", 8).Document;

        Element element = Assert.Single(document.Elements);
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
        Assert.Equal(271, document.Elements[0].Y);
        Assert.Equal(300, document.Elements[1].Y);
        Assert.Equal(271, document.Elements[2].Y);
    }

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
