using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// What a copy has to be: everything the original was, in the same place, with an identity
/// of its own and a place in the stack above what is already there.
/// </summary>
public sealed class ElementDuplicatorTests
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
    public void ACopyStartsWhereTheOriginalIs()
    {
        var box = new BoxElement { X = 120, Y = 80, WidthDots = 200, HeightDots = 100, ZOrder = 0 };
        LabelDocument document = Label(box);

        Element clone = Assert.Single(ElementDuplicator.Clone(document, [box]));
        var cloned = Assert.IsType<BoxElement>(clone);

        Assert.Equal(box.X, cloned.X);
        Assert.Equal(box.Y, cloned.Y);
        Assert.Equal(box.WidthDots, cloned.WidthDots);
        Assert.Equal(box.HeightDots, cloned.HeightDots);
    }

    /// <summary>Two elements sharing an id break selection, undo restore and anything else
    /// that finds an element by it.</summary>
    [Fact]
    public void ACopyHasAnIdentityOfItsOwn()
    {
        var text = new TextElement { X = 20, Y = 20, Text = "keep me", FontHeightDots = 40 };
        LabelDocument document = Label(text);

        Element clone = Assert.Single(ElementDuplicator.Clone(document, [text]));

        Assert.NotEqual(text.Id, clone.Id);
        Assert.NotSame(text, clone);
        Assert.Equal("keep me", Assert.IsType<TextElement>(clone).Text);
    }

    [Fact]
    public void CopiesLandAboveEverythingAlreadyInTheDocument()
    {
        var low = new BoxElement { X = 0, Y = 0, WidthDots = 40, HeightDots = 40, ZOrder = 2 };
        var high = new BoxElement { X = 0, Y = 0, WidthDots = 40, HeightDots = 40, ZOrder = 9 };
        LabelDocument document = Label(low, high);

        List<Element> clones = ElementDuplicator.Clone(document, [low]);

        Assert.Equal(10, Assert.Single(clones).ZOrder);
    }

    /// <summary>Copying several keeps which was in front of which, or a duplicated group
    /// comes back reshuffled.</summary>
    [Fact]
    public void CopiesKeepTheirOrderAmongThemselves()
    {
        var back = new TextElement { Text = "back", FontHeightDots = 20, ZOrder = 1 };
        var middle = new TextElement { Text = "middle", FontHeightDots = 20, ZOrder = 4 };
        var front = new TextElement { Text = "front", FontHeightDots = 20, ZOrder = 7 };
        LabelDocument document = Label(back, middle, front);

        List<Element> clones = ElementDuplicator.Clone(document, [front, back, middle]);

        Assert.Equal(
            ["back", "middle", "front"],
            clones.OrderBy(c => c.ZOrder).Select(c => ((TextElement)c).Text));
        Assert.Equal([8, 9, 10], clones.OrderBy(c => c.ZOrder).Select(c => c.ZOrder));
    }

    /// <summary>Every property survives, which is the whole reason this goes through the
    /// serializer rather than a hand-written copy per type.</summary>
    [Fact]
    public void ACopyKeepsWhatMakesTheElementWhatItIs()
    {
        var barcode = new BarcodeElement
        {
            X = 30,
            Y = 40,
            Data = "7891234567895",
            Symbology = BarcodeSymbology.Ean13,
            HeightDots = 90,
            ModuleWidthDots = 3,
            PrintInterpretationLine = false,
            Orientation = Orientation.Rotated90,
            IsLocked = true,
            DoNotPrint = true,
            Name = "carton code",
        };
        LabelDocument document = Label(barcode);

        var cloned = Assert.IsType<BarcodeElement>(
            Assert.Single(ElementDuplicator.Clone(document, [barcode])));

        Assert.Equal(barcode.Data, cloned.Data);
        Assert.Equal(barcode.Symbology, cloned.Symbology);
        Assert.Equal(barcode.HeightDots, cloned.HeightDots);
        Assert.Equal(barcode.ModuleWidthDots, cloned.ModuleWidthDots);
        Assert.Equal(barcode.PrintInterpretationLine, cloned.PrintInterpretationLine);
        Assert.Equal(barcode.Orientation, cloned.Orientation);
        Assert.Equal(barcode.IsLocked, cloned.IsLocked);
        Assert.Equal(barcode.DoNotPrint, cloned.DoNotPrint);
        Assert.Equal(barcode.Name, cloned.Name);
    }

    [Fact]
    public void CopyingNothingIsNotAnError()
    {
        LabelDocument document = Label(new BoxElement { WidthDots = 40, HeightDots = 40 });
        Assert.Empty(ElementDuplicator.Clone(document, []));
    }
}
