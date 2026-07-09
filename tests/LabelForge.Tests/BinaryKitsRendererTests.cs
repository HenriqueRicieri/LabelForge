using System.Globalization;
using LabelForge.Core.Rendering;

namespace LabelForge.Tests;

public sealed class BinaryKitsRendererTests
{
    [Fact]
    public void Render_IsCultureInvariant_ForDecimalBarcodeRatio()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // A locale whose decimal separator is a comma; without the invariant-culture
            // guard, BinaryKits would misread the "3.0" ratio as 30.
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            var renderer = new BinaryKitsRenderer();

            var withDecimal = renderer.Render(
                "^XA^BY2,3.0^FO40,40^B3N,N,120,Y,N^FDCODE39^FS^XZ", 100, 60, 8);
            var withInteger = renderer.Render(
                "^XA^BY2,3^FO40,40^B3N,N,120,Y,N^FDCODE39^FS^XZ", 100, 60, 8);

            Assert.Empty(withDecimal.Errors);
            Assert.True(withDecimal.Png.Length > 0);
            // Ratio 3.0 and 3 are the same barcode; the render must be byte-identical.
            Assert.Equal(withInteger.Png.Length, withDecimal.Png.Length);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Render_RestoresAmbientCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var ptBr = new CultureInfo("pt-BR");
            CultureInfo.CurrentCulture = ptBr;

            new BinaryKitsRenderer().Render("^XA^FO10,10^A0N,30^FDx^FS^XZ", 50, 30, 8);

            Assert.Equal(ptBr, CultureInfo.CurrentCulture);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
