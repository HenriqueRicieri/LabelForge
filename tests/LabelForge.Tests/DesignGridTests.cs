using LabelForge.Core.Editing;
using LabelForge.Core.Model;

namespace LabelForge.Tests;

/// <summary>
/// The design grid. Drawn and snapped to from one source, because a line the eye can see
/// but the pointer slips past is worse than no grid at all.
/// </summary>
public sealed class DesignGridTests
{
    private static LabelDocument Label(double pitchMm, int dpmm = 8) => new()
    {
        WidthMm = 100, HeightMm = 60, Dpmm = dpmm, GridPitchMm = pitchMm,
    };

    [Fact]
    public void APitchOfZero_IsOff()
    {
        LabelDocument document = Label(0);

        Assert.False(DesignGrid.IsEnabled(document));
        Assert.Empty(DesignGrid.Lines(document, document.WidthDots));
    }

    /// <summary>Below the finest pitch the lines sit closer than the snap threshold, so
    /// every position would be on the grid and the grid would mean nothing.</summary>
    [Fact]
    public void APitchFinerThanTheMinimum_IsOff() =>
        Assert.False(DesignGrid.IsEnabled(Label(DesignGrid.MinimumPitchMm / 2)));

    [Fact]
    public void LinesRunFromTheOriginToTheEdge()
    {
        LabelDocument document = Label(10);

        int[] lines = DesignGrid.Lines(document, document.WidthDots).ToArray();

        // 100 mm at 10 mm and 8 dpmm: 0, 80, 160 ... 800, which is eleven lines.
        Assert.Equal(11, lines.Length);
        Assert.Equal(0, lines[0]);
        Assert.Equal(800, lines[^1]);
        Assert.Equal(80, lines[1]);
    }

    /// <summary>Nothing past the edge, because a line drawn off the stock is a line about
    /// nothing.</summary>
    [Fact]
    public void NoLineFallsPastTheExtent() =>
        Assert.All(
            DesignGrid.Lines(Label(7), 800),
            dots => Assert.InRange(dots, 0, 800));

    /// <summary>
    /// A pitch that does not divide evenly into dots still lands on whole ones, through
    /// the same rounding every other millimetre goes through. The lines are therefore not
    /// perfectly even, which is the honest outcome: the printer has no half dots either.
    /// </summary>
    [Fact]
    public void AnAwkwardPitch_StillLandsOnWholeDots()
    {
        int[] lines = DesignGrid.Lines(Label(2.5, dpmm: 12), 360).ToArray();

        Assert.Equal([0, 30, 60, 90, 120, 150, 180, 210, 240, 270, 300, 330, 360], lines);
    }

    /// <summary>A tiny pitch on a long roll must not be able to stall a render.</summary>
    [Fact]
    public void TheLineCount_IsBounded() =>
        Assert.True(
            DesignGrid.Lines(Label(DesignGrid.MinimumPitchMm), 1_000_000).Count()
                <= DesignGrid.MaxLines + 1);

    /// <summary>The grid joins the same targets as the guides and edges, and the snapper
    /// takes the closest, so a guide a dot away still beats a grid line three away.</summary>
    [Fact]
    public void TheClosestTargetStillWins()
    {
        LabelDocument document = Label(10);
        int[] targets = [.. DesignGrid.Lines(document, document.WidthDots), 83];

        (int shift, int? target) = GuideSnapper.Snap(84, 184, targets, threshold: 8);

        Assert.Equal(83, target);
        Assert.Equal(-1, shift);
    }

    /// <summary>A design aid, so it reaches the saved file and never the printer.</summary>
    [Fact]
    public void TheGridIsSavedAndNeverPrinted()
    {
        LabelDocument document = Label(5);
        document.Elements.Add(new TextElement { X = 10, Y = 10, Text = "x" });
        string withGrid = new Core.Zpl.ZplGenerator().Generate(document);

        document.GridPitchMm = 0;

        Assert.Equal(withGrid, new Core.Zpl.ZplGenerator().Generate(document));
        Assert.Equal(
            5,
            Core.Io.LabelDocumentJson.Deserialize(
                Core.Io.LabelDocumentJson.Serialize(Label(5))).GridPitchMm,
            3);
    }
}
