using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// The ZPL graphic primitives that were not modelled until now: the ellipse (^GE), the
/// circle (^GC) which is the same shape with equal sides, the diagonal line (^GD), and
/// the corner rounding ^GB has always taken and nothing here ever asked for.
///
/// The footprints are measured against rendered ink like every other element's, and the
/// renderer's own limits are pinned here rather than described in a comment, because two
/// of them decide what the properties panel is allowed to claim.
/// </summary>
public sealed class GraphicPrimitiveTests
{
    private const int Dpmm = 8;
    private const double WidthMm = 100;
    private const double HeightMm = 62.5;

    private static LabelDocument Label(params Element[] elements)
    {
        var document = new LabelDocument { WidthMm = WidthMm, HeightMm = HeightMm, Dpmm = Dpmm };
        foreach (Element element in elements)
        {
            document.Elements.Add(element);
        }

        return document;
    }

    /// <summary>Every ink pixel of a rendered document, as a bitmap of its own so two
    /// renders can be compared dot for dot rather than by how much ink they hold.</summary>
    private static (bool[] Ink, int Width, int Height) Render(LabelDocument document)
    {
        RenderResult result = new BinaryKitsRenderer()
            .Render(new ZplGenerator().Generate(document), WidthMm, HeightMm, Dpmm);
        Assert.Empty(result.Errors);
        return Decode(result);
    }

    private static (bool[] Ink, int Width, int Height) Render(string zpl)
    {
        RenderResult result = new BinaryKitsRenderer()
            .Render("^XA\n^CI28\n^PW800\n^LL500\n^LH0,0\n" + zpl + "\n^XZ",
                WidthMm, HeightMm, Dpmm);
        Assert.Empty(result.Errors);
        return Decode(result);
    }

    private static (bool[] Ink, int Width, int Height) Decode(RenderResult result)
    {
        using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
        Assert.NotNull(bitmap);

        var ink = new bool[bitmap.Width * bitmap.Height];
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                ink[(y * bitmap.Width) + x] = bitmap.GetPixel(x, y).Red < 128;
            }
        }

        return (ink, bitmap.Width, bitmap.Height);
    }

    private static DotRect InkBounds((bool[] Ink, int Width, int Height) render)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < render.Height; y++)
        {
            for (int x = 0; x < render.Width; x++)
            {
                if (!render.Ink[(y * render.Width) + x])
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0 ? default : new DotRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static int InkCount((bool[] Ink, int Width, int Height) render) =>
        render.Ink.Count(on => on);

    /// <summary>
    /// An ellipse is inscribed in the box it declares, exactly, at every thickness and at
    /// both the ordinary and the square shape. This is the footprint the selection
    /// outline, the snap targets and the label's measured extent all read.
    /// </summary>
    [Theory]
    [InlineData(200, 100, 4)]
    [InlineData(200, 100, 1)]
    [InlineData(200, 100, 40)]
    [InlineData(100, 100, 3)]
    [InlineData(60, 180, 6)]
    [InlineData(3, 3, 2)]
    public void EllipseFootprint_IsTheBoxItDeclares(int width, int height, int thickness)
    {
        var ellipse = new EllipseElement
        {
            X = 100, Y = 100, WidthDots = width, HeightDots = height, ThicknessDots = thickness,
        };

        DotRect ink = InkBounds(Render(Label(ellipse)));
        DotRect bounds = new ElementBoundsCalculator().GetBounds(ellipse);

        Assert.Equal(new DotRect(100, 100, width, height), ink);
        Assert.Equal(width, bounds.Width);
        Assert.Equal(height, bounds.Height);
    }

    /// <summary>
    /// The reason there is one element rather than two. ^GE with equal sides and ^GC with
    /// that diameter are not merely similar, they are the same dots, so a circle needs no
    /// type of its own and a resize handle never has to switch commands as it passes
    /// through square.
    /// </summary>
    [Theory]
    [InlineData(100, 4)]
    [InlineData(100, 1)]
    [InlineData(60, 3)]
    public void ACircleIsAnEllipseWithEqualSides(int diameter, int thickness)
    {
        (bool[] ellipse, int width, int height) =
            Render($"^FO100,100^GE{diameter},{diameter},{thickness},B^FS");
        (bool[] circle, _, _) = Render($"^FO100,100^GC{diameter},{thickness},B^FS");

        Assert.Equal(width * height, ellipse.Length);
        Assert.Equal(ellipse, circle);
    }

    /// <summary>
    /// A diagonal's footprint is the box it crosses, which is what ^GD's width and height
    /// mean and what a printer draws inside.
    ///
    /// The offline renderer overshoots that box horizontally, and this test states the
    /// shape of the overshoot rather than adopting it: the height is exact at every
    /// thickness, and the width runs over by at most the thickness. It is a stroke cap,
    /// not the command, so following it would make the label's own measured extent wrong
    /// while gaining nothing. Anyone who later finds ink outside the outline should read
    /// this before "fixing" the bounds.
    /// </summary>
    [Theory]
    [InlineData(200, 100, 2)]
    [InlineData(200, 100, 4)]
    [InlineData(200, 100, 10)]
    [InlineData(100, 200, 2)]
    [InlineData(60, 200, 6)]
    public void DiagonalFootprint_IsTheBoxItCrosses(int width, int height, int thickness)
    {
        var diagonal = new DiagonalLineElement
        {
            X = 100, Y = 100, WidthDots = width, HeightDots = height, ThicknessDots = thickness,
        };

        DotRect ink = InkBounds(Render(Label(diagonal)));
        DotRect bounds = new ElementBoundsCalculator().GetBounds(diagonal);

        Assert.Equal(width, bounds.Width);
        Assert.Equal(height, bounds.Height);

        // Exact on the axis the renderer does not stroke along.
        Assert.Equal(100, ink.Y);
        Assert.Equal(height, ink.Height);

        // And within the cap on the other one.
        Assert.InRange(ink.X, 100, 101);
        Assert.InRange(ink.Width, width, width + thickness);
    }

    /// <summary>Which corner the line starts from. "R" is ZPL's default and runs from the
    /// bottom-left up to the top-right, so the two leans are mirror images and neither is
    /// the other rotated.</summary>
    [Fact]
    public void TheLeanDecidesWhichCornersAreJoined()
    {
        (bool[] right, int width, _) = Render("^FO100,100^GD200,100,4,B,R^FS");
        (bool[] left, _, _) = Render("^FO100,100^GD200,100,4,B,L^FS");

        Assert.NotEqual(right, left);

        // The top row of a right-leaning line sits at the far end, the bottom row at the
        // near end; a left-leaning one is the other way about.
        Assert.True(FirstInk(right, width, 100) > FirstInk(right, width, 199));
        Assert.True(FirstInk(left, width, 100) < FirstInk(left, width, 199));

        // ZPL's own default is R, so a command that names no lean draws the same line.
        (bool[] unstated, _, _) = Render("^FO100,100^GD200,100,4^FS");
        Assert.Equal(right, unstated);
    }

    private static int FirstInk(bool[] ink, int width, int row)
    {
        for (int x = 0; x < width; x++)
        {
            if (ink[(row * width) + x])
            {
                return x;
            }
        }

        return -1;
    }

    /// <summary>
    /// A one-dot diagonal prints and the offline renderer draws nothing for it, which is
    /// why a diagonal created here starts at 2 and the panel says so for an imported one.
    /// The two-dot case is the control: the same command one dot thicker draws fine, so
    /// this is a threshold in the renderer rather than the command failing.
    /// </summary>
    [Fact]
    public void TheRendererCannotDrawAOneDotDiagonal()
    {
        Assert.Equal(0, InkCount(Render("^FO100,100^GD200,100,1,B,R^FS")));
        Assert.True(InkCount(Render("^FO100,100^GD200,100,2,B,R^FS")) > 0);
    }

    /// <summary>
    /// White is the one thing on an ellipse the canvas cannot show, and the measurement
    /// says it is the command rather than the shape: over the same black slab, a white
    /// ^GB, ^GD and ^GC all take ink back out and a white ^GE takes none at any thickness,
    /// though ^GC is drawing the very same circle.
    ///
    /// The flag is kept anyway, because a printer erases as asked. The properties panel
    /// carries the difference, which is the call ^FB's line cap already made: say what the
    /// preview cannot show rather than let the canvas pretend either way.
    /// </summary>
    [Fact]
    public void TheRendererDrawsNoWhiteEllipse()
    {
        const string slab = "^FO50,50^GB400,300,400,B^FS";
        int solid = InkCount(Render(slab));

        Assert.Equal(solid, InkCount(Render(slab + "^FO150,120^GE200,100,10,W^FS")));
        Assert.Equal(solid, InkCount(Render(slab + "^FO150,120^GE100,100,10,W^FS")));
        Assert.Equal(solid, InkCount(Render(slab + "^FO150,120^GE200,100,1,W^FS")));

        // The controls: three other white commands over the same slab, all of which erase.
        Assert.True(InkCount(Render(slab + "^FO150,120^GC100,10,W^FS")) < solid);
        Assert.True(InkCount(Render(slab + "^FO150,120^GB200,100,10,W^FS")) < solid);
        Assert.True(InkCount(Render(slab + "^FO150,120^GD200,100,10,W,R^FS")) < solid);
    }

    /// <summary>
    /// ^GB's rounding index rounds the corners without moving the edges, so a rounded box
    /// keeps the footprint of a square one and only the ink inside the corners changes.
    /// Monotonic in the index, which is what tells the parameter apart from being ignored.
    /// </summary>
    [Fact]
    public void CornerRounding_TakesInkOutOfTheCornersAndNothingElse()
    {
        int[] ink = new[] { 0, 1, 3, 5, 8 }
            .Select(r => new BoxElement
            {
                X = 100, Y = 100, WidthDots = 200, HeightDots = 100,
                ThicknessDots = 4, CornerRoundness = r,
            })
            .Select(box =>
            {
                Assert.Equal(new DotRect(100, 100, 200, 100), InkBounds(Render(Label(box))));
                return InkCount(Render(Label(box)));
            })
            .ToArray();

        for (int i = 1; i < ink.Length; i++)
        {
            Assert.True(
                ink[i] < ink[i - 1],
                $"rounding index {i} should take more ink out than the one before it");
        }
    }

    /// <summary>
    /// A box that never asked for rounding writes the ZPL it always did. The fifth ^GB
    /// argument appears only when it is set, or every label saved before this would
    /// generate different bytes for the same picture.
    /// </summary>
    [Fact]
    public void ABoxWithNoRounding_GeneratesTheBytesItAlwaysDid()
    {
        var box = new BoxElement { X = 40, Y = 50, WidthDots = 200, HeightDots = 100, ThicknessDots = 4 };
        string plain = new ZplGenerator().Generate(Label(box));

        Assert.Contains("^FO40,50^GB200,100,4,B^FS", plain, StringComparison.Ordinal);

        box.CornerRoundness = 5;
        Assert.Contains(
            "^FO40,50^GB200,100,4,B,5^FS",
            new ZplGenerator().Generate(Label(box)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandsAreWrittenTheWayZplStatesThem()
    {
        string zpl = new ZplGenerator().Generate(Label(
            new EllipseElement
            {
                X = 10, Y = 20, WidthDots = 180, HeightDots = 110, ThicknessDots = 4,
            },
            new EllipseElement
            {
                X = 200, Y = 20, WidthDots = 90, HeightDots = 90, ThicknessDots = 2, IsWhite = true,
            },
            new DiagonalLineElement
            {
                X = 10, Y = 200, WidthDots = 150, HeightDots = 100, ThicknessDots = 3,
            },
            new DiagonalLineElement
            {
                X = 200, Y = 200, WidthDots = 150, HeightDots = 100, ThicknessDots = 3,
                LeansRight = false, IsWhite = true,
            }));

        Assert.Contains("^FO10,20^GE180,110,4,B^FS", zpl, StringComparison.Ordinal);
        Assert.Contains("^FO200,20^GE90,90,2,W^FS", zpl, StringComparison.Ordinal);
        Assert.Contains("^FO10,200^GD150,100,3,B,R^FS", zpl, StringComparison.Ordinal);
        Assert.Contains("^FO200,200^GD150,100,3,W,L^FS", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportReadsEveryArgument()
    {
        LabelDocument document = ZplDocumentImport.FromZpl(
            "^XA^PW800^LL500"
            + "^FO10,20^GE180,110,4,B^FS"
            + "^FO200,20^GE90,90,2,W^FS"
            + "^FO10,200^GD150,100,3,B,L^FS"
            + "^FO200,200^GD150,100,3,W^FS"
            + "^FO400,200^GB160,90,3,B,5^FS"
            + "^XZ",
            Dpmm).Document;

        var ellipse = Assert.IsType<EllipseElement>(document.Elements[0]);
        Assert.Equal((10, 20, 180, 110, 4), (ellipse.X, ellipse.Y, ellipse.WidthDots, ellipse.HeightDots, ellipse.ThicknessDots));
        Assert.False(ellipse.IsWhite);

        var circle = Assert.IsType<EllipseElement>(document.Elements[1]);
        Assert.Equal(90, circle.WidthDots);
        Assert.Equal(90, circle.HeightDots);
        Assert.True(circle.IsWhite);

        var leaning = Assert.IsType<DiagonalLineElement>(document.Elements[2]);
        Assert.Equal((150, 100, 3), (leaning.WidthDots, leaning.HeightDots, leaning.ThicknessDots));
        Assert.False(leaning.LeansRight);
        Assert.False(leaning.IsWhite);

        var white = Assert.IsType<DiagonalLineElement>(document.Elements[3]);
        Assert.True(white.IsWhite);

        // An unstated lean is ZPL's default, which is R.
        Assert.True(white.LeansRight);

        var box = Assert.IsType<BoxElement>(document.Elements[4]);
        Assert.Equal(5, box.CornerRoundness);
    }

    /// <summary>
    /// ^GC is read as an ellipse with equal sides, which is lossless because the two draw
    /// the same dots. Its arguments sit one place to the left of ^GE's, since it states a
    /// diameter where the ellipse states two sides.
    /// </summary>
    [Fact]
    public void ACircleCommandIsReadAsAnEllipse()
    {
        LabelDocument document = ZplDocumentImport.FromZpl(
            "^XA^PW800^LL500^FO10,20^GC120,6,W^FS^XZ", Dpmm).Document;

        var circle = Assert.IsType<EllipseElement>(Assert.Single(document.Elements));
        Assert.Equal(120, circle.WidthDots);
        Assert.Equal(120, circle.HeightDots);
        Assert.Equal(6, circle.ThicknessDots);
        Assert.True(circle.IsWhite);
    }

    /// <summary>A one-dot diagonal keeps the thickness the file stated rather than being
    /// raised to what the canvas can draw: a printer prints it, so thickening it would
    /// change the label. The warning is what carries the difference.</summary>
    [Fact]
    public void AnUndrawableDiagonalIsReportedRatherThanThickened()
    {
        ZplDocumentImportResult result = ZplDocumentImport.FromZpl(
            "^XA^PW800^LL500^FO10,20^GD150,100,1,B,R^FS^XZ", Dpmm);

        var diagonal = Assert.IsType<DiagonalLineElement>(Assert.Single(result.Document.Elements));
        Assert.Equal(1, diagonal.ThicknessDots);
        Assert.Contains(result.Warnings, w => w.Contains("^GD", StringComparison.Ordinal));
    }

    [Fact]
    public void TheNewShapesRoundTripThroughTheProjectFile()
    {
        var document = new LabelDocument();
        document.Elements.Add(new EllipseElement
        {
            X = 10, Y = 20, WidthDots = 180, HeightDots = 110, ThicknessDots = 4, IsWhite = true,
        });
        document.Elements.Add(new DiagonalLineElement
        {
            X = 30, Y = 40, WidthDots = 150, HeightDots = 100, ThicknessDots = 3, LeansRight = false,
        });
        document.Elements.Add(new BoxElement
        {
            X = 50, Y = 60, WidthDots = 90, HeightDots = 70, CornerRoundness = 7,
        });

        LabelDocument restored =
            LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(document));

        var ellipse = Assert.IsType<EllipseElement>(restored.Elements[0]);
        Assert.Equal(180, ellipse.WidthDots);
        Assert.Equal(110, ellipse.HeightDots);
        Assert.True(ellipse.IsWhite);

        var diagonal = Assert.IsType<DiagonalLineElement>(restored.Elements[1]);
        Assert.Equal(150, diagonal.WidthDots);
        Assert.False(diagonal.LeansRight);

        Assert.Equal(7, Assert.IsType<BoxElement>(restored.Elements[2]).CornerRoundness);
    }

    /// <summary>The resize gesture keeps both shapes inside the range ZPL accepts. Above
    /// 4095 a printer stops widening the ellipse, so a handle dragged past it would keep
    /// moving while the ink stood still.</summary>
    [Fact]
    public void ResizingStaysInsideTheRangeZplAccepts()
    {
        var ellipse = new EllipseElement();
        ElementResizer.Resize(ellipse, 9000, 1);
        Assert.Equal(4095, ellipse.WidthDots);
        Assert.Equal(3, ellipse.HeightDots);

        var diagonal = new DiagonalLineElement();
        ElementResizer.Resize(diagonal, 240, 1);
        Assert.Equal(240, diagonal.WidthDots);
        Assert.Equal(3, diagonal.HeightDots);
    }

    /// <summary>Blank stock is not a symbol, so none of these ask for a quiet zone.</summary>
    [Fact]
    public void TheseAreNotSymbolsAndAskForNoQuietZone()
    {
        Assert.False(QuietZone.Applies(new EllipseElement()));
        Assert.False(QuietZone.Applies(new DiagonalLineElement()));
    }

    /// <summary>
    /// ZPL turns a field only where the command takes an orientation, and the graphic
    /// primitives do not: `^GB`, `^GE`, `^GD` and `^GF` state a width and a height and
    /// draw them. Rendering each at 0 and at 90 degrees gives byte-identical output, and
    /// only text differs.
    ///
    /// Found while adding the two new shapes, and it was already wrong for the box, the
    /// line and the image: the footprint swapped their sides for a rotation the printer
    /// ignores, so a 200 by 100 box set to 90 degrees drew 200 by 100 and measured 100 by
    /// 200. That is the selection outline, the snap targets, the alignment and the label's
    /// own measured length all wrong at once, and silently, since nothing about the ZPL
    /// changed to give it away.
    /// </summary>
    [Fact]
    public void RotationReachesOnlyTheFieldsZplTurns()
    {
        Element[] unturnable =
        [
            new BoxElement { WidthDots = 200, HeightDots = 100, ThicknessDots = 4 },
            new EllipseElement { WidthDots = 200, HeightDots = 100, ThicknessDots = 4 },
            new DiagonalLineElement { WidthDots = 200, HeightDots = 100, ThicknessDots = 4 },
            new LineElement { LengthDots = 200, ThicknessDots = 4 },
            new ImageElement
            {
                ImageData = TestImages.HalfBlackPng(),
                SourcePixelWidth = 8, SourcePixelHeight = 1,
                WidthDots = 200, HeightDots = 100, Dithering = DitherMode.Threshold,
            },
        ];

        foreach (Element element in unturnable)
        {
            Assert.False(FieldRotation.Applies(element), element.GetType().Name);

            element.X = 100;
            element.Y = 100;
            element.Orientation = Orientation.Normal;
            byte[] upright = new BinaryKitsRenderer()
                .Render(new ZplGenerator().Generate(Label(element)), WidthMm, HeightMm, Dpmm).Png;
            DotRect flat = new ElementBoundsCalculator().GetBounds(element);

            element.Orientation = Orientation.Rotated90;
            byte[] turned = new BinaryKitsRenderer()
                .Render(new ZplGenerator().Generate(Label(element)), WidthMm, HeightMm, Dpmm).Png;

            // The printer draws the same thing, so the footprint has to say the same thing.
            Assert.Equal(upright, turned);
            Assert.Equal(flat, new ElementBoundsCalculator().GetBounds(element));
        }

        // And the control: text really does turn, so its sides really do swap.
        var text = new TextElement { X = 100, Y = 100, Text = "Wide text", FontHeightDots = 40 };
        DotRect uprightText = new ElementBoundsCalculator().GetBounds(text);
        text.Orientation = Orientation.Rotated90;
        DotRect turnedText = new ElementBoundsCalculator().GetBounds(text);

        Assert.True(FieldRotation.Applies(text));
        Assert.Equal(uprightText.Width, turnedText.Height);
        Assert.Equal(uprightText.Height, turnedText.Width);
    }
}
