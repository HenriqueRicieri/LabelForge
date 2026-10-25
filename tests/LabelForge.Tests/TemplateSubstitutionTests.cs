using LabelForge.Core.Rendering;
using LabelForge.Core.Templating;

namespace LabelForge.Tests;

/// <summary>
/// Unit behavior of the substitutor, plus proof that substituting sample data makes
/// the previously unrenderable Atak barcodes (template markers inside a barcode) draw.
/// </summary>
public sealed class TemplateSubstitutionTests
{
    [Fact]
    public void Substitute_ReplacesVariables_WithDefaultSample()
    {
        var sut = new TemplateSubstitutor();
        string result = sut.Substitute("^FD##CODIGO_BARRAS##^FS");
        Assert.Equal($"^FD{TemplateSubstitutor.DefaultSampleValue}^FS", result);
    }

    /// <summary>A lone delimiter in one field is text, not the start of a marker that ends
    /// in the next field: pairing across a ZPL command swallowed the commands between them.</summary>
    [Fact]
    public void Substitute_DoesNotPairDelimitersAcrossCommands()
    {
        var sut = new TemplateSubstitutor();
        string result = sut.Substitute("^FO1,1^FDTotal ##^FS\n^FO1,40^FD##NAME##^FS");
        Assert.Equal(
            $"^FO1,1^FDTotal ##^FS\n^FO1,40^FD{TemplateSubstitutor.DefaultSampleValue}^FS", result);
    }

    [Fact]
    public void Scan_LeavesAnUnterminatedMarkerLiteralAtTheEndOfItsLine()
    {
        TemplateSegment[] segments =
            [.. TemplateScanner.Scan("^FD##@F(X)#^FS\n^FD##A##^FS", "##", "##")];

        Assert.Equal(
            [TemplateSegmentKind.Literal, TemplateSegmentKind.Variable, TemplateSegmentKind.Literal],
            segments.Select(s => s.Kind));
        Assert.Equal("^FD##@F(X)#^FS\n^FD", segments[0].Text);
        Assert.Equal("A", segments[1].Inner);
    }

    [Fact]
    public void Substitute_RemovesInternalDirectives()
    {
        var sut = new TemplateSubstitutor();
        string result = sut.Substitute("##@SET_PRINTER(1)##^XA^FDx^FS^XZ");
        Assert.Equal("^XA^FDx^FS^XZ", result);
    }

    [Fact]
    public void Substitute_HonorsUserOverride()
    {
        var sut = new TemplateSubstitutor();
        string result = sut.Substitute(
            "^FD##EAN##^FS",
            inner => inner == "EAN" ? "7891234567895" : null);
        Assert.Equal("^FD7891234567895^FS", result);
    }

    [Fact]
    public void Substitute_LeavesPlainZplUntouched()
    {
        var sut = new TemplateSubstitutor();
        const string zpl = "^XA^FO50,50^A0N,30,30^FDHello^FS^XZ";
        Assert.Equal(zpl, sut.Substitute(zpl));
    }

    [Fact]
    public void SampleSubstitution_MakesTheWholeCorpusRenderToImages()
    {
        var substitutor = new TemplateSubstitutor();
        var renderer = new BinaryKitsRenderer();

        var stillFailing = new List<string>();
        foreach (var path in CorpusFiles())
        {
            string zpl = substitutor.Substitute(File.ReadAllText(path));
            RenderResult result = renderer.Render(zpl, widthMm: 100, heightMm: 150, dpmm: 8);
            if (result.Png.Length == 0)
            {
                stillFailing.Add($"{Path.GetFileName(path)}: {string.Join("; ", result.Errors)}");
            }
        }

        Assert.True(
            stillFailing.Count == 0,
            "Files still not rendering after sample substitution:\n" + string.Join("\n", stillFailing));
    }

    private static IEnumerable<string> CorpusFiles() => TestCorpus.Files();
}
