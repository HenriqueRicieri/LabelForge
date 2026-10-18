using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// The preview renders on a pool thread while the editor keeps changing the label. The
/// render reads a shallow copy: the same elements in collections of its own, so adding,
/// deleting or reordering an element on the UI thread cannot break an enumeration the
/// render is in the middle of ("Collection was modified").
/// </summary>
public sealed class LabelDocumentCopyTests
{
    private static LabelDocument Sample()
    {
        var document = new LabelDocument
        {
            WidthMm = 80, HeightMm = 50, Dpmm = 12, IsContinuous = true, ContinuousMarginMm = 4,
            Markers = new MarkerSyntax("{{", "}}", '@'), GridPitchMm = 2.5,
        };
        document.Elements.Add(new TextElement { X = 10, Y = 10, Text = "a", FontHeightDots = 30 });
        document.Elements.Add(new BoxElement { X = 50, Y = 60, WidthDots = 40, HeightDots = 40, ZOrder = 1 });
        document.SampleValues["A"] = "1";
        document.Variables["N"] = new VariableDefinition { Kind = VariableKind.Counter };
        document.VerticalGuides.Add(100);
        document.HorizontalGuides.Add(200);
        document.Print.Copies = 3;
        return document;
    }

    [Fact]
    public void TheCopySaysEverythingTheDocumentSays()
    {
        LabelDocument document = Sample();

        Assert.Equal(LabelDocumentJson.Serialize(document), LabelDocumentJson.Serialize(document.ShallowCopy()));
    }

    [Fact]
    public void ChangingTheDocumentsCollectionsLeavesTheCopysAlone()
    {
        LabelDocument document = Sample();
        LabelDocument copy = document.ShallowCopy();

        document.Elements.RemoveAt(0);
        document.SampleValues["B"] = "2";
        document.Variables.Clear();
        document.VerticalGuides.Clear();
        document.HorizontalGuides.Add(1);

        Assert.Equal(2, copy.Elements.Count);
        Assert.Single(copy.SampleValues);
        Assert.Single(copy.Variables);
        Assert.Single(copy.VerticalGuides);
        Assert.Single(copy.HorizontalGuides);
    }

    [Fact]
    public void TheCopySharesTheElementsThemselves()
    {
        LabelDocument document = Sample();

        Assert.Same(document.Elements[0], document.ShallowCopy().Elements[0]);
    }
}
