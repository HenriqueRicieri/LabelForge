using LabelForge.Core.Model;
using LabelForge.Core.Templating;

namespace LabelForge.Tests;

public sealed class TemplateVariablesTests
{
    [Fact]
    public void Discover_FindsVariablesAcrossElementTypes_InEncounterOrder()
    {
        var doc = new LabelDocument();
        doc.Elements.Add(new TextElement { Text = "Lot ##LOTE## of ##PRODUTO##" });
        doc.Elements.Add(new BarcodeElement { Data = "##CODIGO_BARRAS##" });
        doc.Elements.Add(new QrCodeElement { Data = "##LOTE##" });
        doc.Elements.Add(new DataMatrixElement { Data = "##SERIE##" });

        Assert.Equal(["LOTE", "PRODUTO", "CODIGO_BARRAS", "SERIE"], TemplateVariables.Discover(doc));
    }

    [Fact]
    public void Discover_IgnoresDirectivesAndUnterminatedMarkers()
    {
        var doc = new LabelDocument();
        doc.Elements.Add(new TextElement { Text = "##@PRINT_REGION(1)## and a lone ## pair" });

        Assert.Empty(TemplateVariables.Discover(doc));
    }

    [Fact]
    public void Discover_StripsFunctionSuffixes()
    {
        var doc = new LabelDocument();
        doc.Elements.Add(new BarcodeElement { Data = "##CODIGO@EAN13(0)##" });

        Assert.Equal(["CODIGO"], TemplateVariables.Discover(doc));
    }

    [Fact]
    public void NameOf_ReturnsThePartBeforeTheFunction()
    {
        Assert.Equal("CODIGO", TemplateVariables.NameOf("CODIGO@EAN13(0)"));
        Assert.Equal("NOME", TemplateVariables.NameOf("NOME"));
    }
}
