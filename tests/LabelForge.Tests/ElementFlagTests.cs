using LabelForge.Core.Editing;
using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

/// <summary>
/// The two per-element switches that decide whether something is editable and whether it
/// prints. The rule that matters is that "do not print" folds into ElementPlacement, so
/// the generator, the warning line and the canvas outlines read one source and cannot
/// come to different conclusions.
/// </summary>
public sealed class ElementFlagTests
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

    private static TextElement Text(string text, bool doNotPrint = false) => new()
    {
        X = 20, Y = 20, Text = text, FontHeightDots = 30, DoNotPrint = doNotPrint,
    };

    [Fact]
    public void ADoNotPrintElement_IsLeftOutOfTheGeneratedZpl()
    {
        string zpl = new ZplGenerator().Generate(Label(
            Text("keep me"), Text("annotation", doNotPrint: true)));

        Assert.Contains("keep me", zpl, StringComparison.Ordinal);
        Assert.DoesNotContain("annotation", zpl, StringComparison.Ordinal);
    }

    /// <summary>The canvas underlay is the preview, and an element the user can still see
    /// and drag has to be in it. That is the whole difference from hiding it.</summary>
    [Fact]
    public void ADoNotPrintElement_StaysInThePreview()
    {
        string preview = new ZplGenerator().GeneratePreview(
            Label(Text("annotation", doNotPrint: true)), offsetDots: 160);

        Assert.Contains("annotation", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void AHiddenElement_IsInNeither()
    {
        LabelDocument document = Label(Text("gone"));
        document.Elements[0].IsVisible = false;

        Assert.DoesNotContain("gone", new ZplGenerator().Generate(document), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "gone", new ZplGenerator().GeneratePreview(document, 160), StringComparison.Ordinal);
    }

    [Fact]
    public void APrintRun_LeavesItOutToo()
    {
        LabelDocument document = Label(Text("keep me"), Text("annotation", doNotPrint: true));

        PrintJobResult job = PrintJob.Build(document, new DateTime(2026, 7, 25, 9, 0, 0));

        Assert.DoesNotContain("annotation", job.Zpl, StringComparison.Ordinal);
    }

    /// <summary>Reported as a decision, not as a mistake, and not confused with the
    /// positional rule even when the element also happens to sit off the label.</summary>
    [Fact]
    public void Classification_SeparatesADecisionFromAnAccident()
    {
        var bounds = new DotRect(20, 20, 100, 30);

        Assert.Equal(
            PlacementStatus.Suppressed,
            ElementPlacement.Classify(Text("x", doNotPrint: true), bounds, 800, 480));

        Assert.Equal(
            PlacementStatus.NotPrintable,
            ElementPlacement.Classify(new TextElement { X = 900, Y = 20 }, bounds, 800, 480));

        // Both at once still reads as the deliberate one.
        Assert.Equal(
            PlacementStatus.Suppressed,
            ElementPlacement.Classify(
                new TextElement { X = 900, Y = 20, DoNotPrint = true }, bounds, 800, 480));

        Assert.Equal(
            PlacementStatus.Inside,
            ElementPlacement.Classify(Text("x"), bounds, 800, 480));
    }

    [Fact]
    public void IsPrintable_AndTheGeneratorAgree()
    {
        TextElement suppressed = Text("x", doNotPrint: true);

        Assert.False(ElementPlacement.IsPrintable(suppressed, 800, 480));
        Assert.True(ElementPlacement.IsPrintable(Text("x"), 800, 480));
    }

    /// <summary>Alignment is a canvas gesture, so a locked element must sit it out while
    /// the others still move.</summary>
    [Fact]
    public void ALockedElement_IsNotMovedByAlignment()
    {
        var locked = new TextElement { X = 500, Y = 10, Text = "pinned", IsLocked = true };
        var free = new TextElement { X = 300, Y = 60, Text = "movable" };
        var anchor = new TextElement { X = 100, Y = 110, Text = "anchor" };

        Aligner.Align([locked, free, anchor], AlignEdge.Left, 800, 480);

        Assert.Equal(500, locked.X);
        Assert.Equal(100, free.X);
    }

    [Fact]
    public void BothFlags_RoundTripThroughTheProjectFile()
    {
        LabelDocument document = Label(
            new TextElement
            {
                X = 20, Y = 20, Text = "note", FontHeightDots = 30,
                DoNotPrint = true, IsLocked = true,
            },
            Text("ordinary"));

        LabelDocument reloaded = LabelDocumentJson.Deserialize(
            LabelDocumentJson.Serialize(document));

        Assert.True(reloaded.Elements[0].DoNotPrint);
        Assert.True(reloaded.Elements[0].IsLocked);
        Assert.False(reloaded.Elements[1].DoNotPrint);
        Assert.False(reloaded.Elements[1].IsLocked);
    }

    /// <summary>The flags are ours, not ZPL's: a label that never sets them generates the
    /// bytes it always did, and nothing about them reaches the printer.</summary>
    [Fact]
    public void TheFlagsNeverReachTheZpl()
    {
        LabelDocument plain = Label(Text("same"));
        LabelDocument locked = Label(Text("same"));
        locked.Elements[0].IsLocked = true;

        Assert.Equal(
            new ZplGenerator().Generate(plain),
            new ZplGenerator().Generate(locked));
    }
}
