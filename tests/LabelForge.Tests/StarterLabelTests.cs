using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Starters;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// The labels the gallery offers to start from.
///
/// A starter is the first thing somebody sees the app produce, so "it opens" is not the
/// bar: it has to fit the stock, encode, and leave its symbols readable, at every print
/// density and with the preview samples in place of the markers. Each of those is a way a
/// hand-placed layout goes wrong quietly, and none of them is visible in the source.
/// </summary>
public sealed class StarterLabelTests
{
    /// <summary>203, 300 and 600 dpi. A starter is stated in millimeters and built in
    /// dots, so these are the three roundings it has to survive.</summary>
    private static readonly int[] EveryDensity = [8, 12, 24];

    public static TheoryData<string> EveryStarter()
    {
        var data = new TheoryData<string>();
        foreach (StarterLabel starter in StarterCatalog.All)
        {
            data.Add(starter.Name);
        }

        return data;
    }

    public static TheoryData<string, int> EveryStarterAtEveryDensity()
    {
        var data = new TheoryData<string, int>();
        foreach (StarterLabel starter in StarterCatalog.All)
        {
            foreach (int dpmm in EveryDensity)
            {
                data.Add(starter.Name, dpmm);
            }
        }

        return data;
    }

    private static StarterLabel Find(string name) =>
        StarterCatalog.All.Single(s => s.Name == name);

    [Fact]
    public void All_StartersAreNamedSizedAndDistinct()
    {
        Assert.NotEmpty(StarterCatalog.All);
        Assert.Equal(
            StarterCatalog.All.Count,
            StarterCatalog.All.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count());

        Assert.All(StarterCatalog.All, starter =>
        {
            Assert.NotEmpty(starter.Name);
            Assert.NotEmpty(starter.Summary);
            Assert.InRange(starter.WidthMm, 10, 300);
            Assert.InRange(starter.HeightMm, 10, 300);
        });
    }

    /// <summary>
    /// The size on the card is the size of the document the card creates. Two numbers for
    /// one fact is how a gallery ends up offering a "4 x 6" that opens at 100 by 150.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryStarter))]
    public void Create_ProducesTheSizeTheGalleryAdvertises(string name)
    {
        StarterLabel starter = Find(name);
        LabelDocument document = starter.Create(8);

        Assert.Equal(starter.WidthMm, document.WidthMm);
        Assert.Equal(starter.HeightMm, document.HeightMm);
        Assert.NotEmpty(document.Elements);
    }

    /// <summary>
    /// A fresh document every call, with fresh element identities. The designer edits
    /// whatever it is handed, so a shared instance would carry one session's edits into the
    /// next person's new label, and repeated Guids would break selection and undo.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryStarter))]
    public void Create_ProducesAnIndependentDocumentEveryTime(string name)
    {
        StarterLabel starter = Find(name);

        LabelDocument first = starter.Create(8);
        first.Elements[0].X += 137;
        first.Elements.RemoveAt(first.Elements.Count - 1);
        first.SampleValues["ADDED_BY_THE_TEST"] = "x";

        LabelDocument second = starter.Create(8);

        Assert.NotEqual(first.Elements.Count, second.Elements.Count);
        Assert.DoesNotContain("ADDED_BY_THE_TEST", second.SampleValues.Keys);
        Assert.Empty(
            first.Elements.Select(e => e.Id).Intersect(second.Elements.Select(e => e.Id)));
        Assert.Equal(
            second.Elements.Count,
            second.Elements.Select(e => e.Id).Distinct().Count());
    }

    /// <summary>
    /// Every element on the label at every density: not off the edge (which drops it from
    /// the ZPL silently) and not across it (which prints it cut off). This is the check
    /// that a millimeter layout survives being rounded into dots three different ways.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryStarterAtEveryDensity))]
    public void Create_PlacesEveryElementOnTheLabel(string name, int dpmm)
    {
        LabelDocument document = Find(name).Create(dpmm);
        var bounds = new ElementBoundsCalculator();

        Assert.All(document.Elements, element =>
        {
            PlacementStatus status = ElementPlacement.Classify(
                element, bounds.GetBounds(element), document);
            Assert.Equal(PlacementStatus.Inside, status);
        });
    }

    /// <summary>
    /// The same, for what the canvas actually draws.
    ///
    /// A marker and the value that replaces it are different lengths, so a field that fits
    /// as `##TO_STREET##` can run off the stock as "Rua das Palmeiras 482", and the sample
    /// is what the person picking a starter sees. Design-time bounds alone would call that
    /// label fine.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryStarterAtEveryDensity))]
    public void Create_PlacesThePreviewOnTheLabelToo(string name, int dpmm)
    {
        LabelDocument document = WithSamplesInPlace(Find(name).Create(dpmm));
        var bounds = new ElementBoundsCalculator();

        Assert.All(document.Elements, element =>
        {
            PlacementStatus status = ElementPlacement.Classify(
                element, bounds.GetBounds(element), document);
            Assert.Equal(PlacementStatus.Inside, status);
        });
    }

    /// <summary>
    /// Nothing crowds a symbol, in the design or in the preview. A starter is what somebody
    /// copies the spacing of, so a barcode that needs a second pass to scan is worse here
    /// than anywhere else in the app.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryStarterAtEveryDensity))]
    public void Create_LeavesEveryQuietZoneClear(string name, int dpmm)
    {
        LabelDocument document = Find(name).Create(dpmm);
        Assert.Empty(QuietZoneChecker.Check(document).Select(Describe));
        Assert.Empty(QuietZoneChecker.Check(WithSamplesInPlace(document)).Select(Describe));
    }

    /// <summary>Every barcode encodes, with the marker in it and with the sample in its
    /// place. The second one is the real check: a symbology with a length rule accepts the
    /// marker unjudged and only meets the value at preview time.</summary>
    [Theory]
    [MemberData(nameof(EveryStarter))]
    public void Create_EncodesEveryBarcode(string name)
    {
        LabelDocument document = Find(name).Create(8);

        foreach (LabelDocument version in new[] { document, WithSamplesInPlace(document) })
        {
            foreach (BarcodeElement barcode in version.Elements.OfType<BarcodeElement>())
            {
                Assert.Null(BarcodeValidator.Validate(barcode, version.Markers));
            }
        }
    }

    /// <summary>
    /// Every marker has a sample. Without one the canvas draws the delimiters, a barcode
    /// encodes them, and the first thing the app shows somebody is a label reading
    /// "##TO_NAME##".
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryStarter))]
    public void Create_SamplesEveryMarkerItUses(string name)
    {
        LabelDocument document = Find(name).Create(8);

        Assert.All(TemplateVariables.Discover(document), variable =>
        {
            Assert.True(
                document.SampleValues.TryGetValue(variable, out string? sample),
                $"{name} uses ##{variable}## and seeds no sample for it");
            Assert.NotEmpty(sample!);
        });

        // And nothing seeded that no field asks for, which is how a renamed marker leaves
        // a sample behind that quietly stops meaning anything.
        Assert.All(document.SampleValues.Keys, seeded =>
            Assert.Contains(seeded, TemplateVariables.Discover(document)));
    }

    /// <summary>A starter is an ordinary document, so it has to save and reopen like
    /// one. The round trip is the .lfl format and the undo snapshot both.</summary>
    [Theory]
    [MemberData(nameof(EveryStarter))]
    public void Create_RoundTripsThroughTheLabelFormat(string name)
    {
        LabelDocument document = Find(name).Create(12);
        string json = LabelDocumentJson.Serialize(document);

        Assert.Equal(json, LabelDocumentJson.Serialize(LabelDocumentJson.Deserialize(json)));
    }

    /// <summary>
    /// The same physical label at 203, 300 and 600 dpi.
    ///
    /// This is the reason a starter is a layout rather than a saved document. Element
    /// coordinates are dots, so a stored 4 by 6 shipping label built at 8 dpmm would print
    /// its address block in the top left third of a 300 dpi label, and its barcode at half
    /// the width a scanner was set up for.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryStarter))]
    public void Create_KeepsThePhysicalLayoutAtEveryDensity(string name)
    {
        StarterLabel starter = Find(name);
        LabelDocument reference = starter.Create(8);

        foreach (int dpmm in EveryDensity)
        {
            LabelDocument document = starter.Create(dpmm);
            Assert.Equal(reference.Elements.Count, document.Elements.Count);

            for (int i = 0; i < document.Elements.Count; i++)
            {
                Element expected = reference.Elements[i];
                Element actual = document.Elements[i];

                // Within a dot of the coarser of the two densities: a millimeter that
                // lands between dots rounds to one of them, and there is no finer answer
                // a printer could give.
                Assert.Equal(Units.DotsToMm(expected.X, 8), Units.DotsToMm(actual.X, dpmm), 1);
                Assert.Equal(Units.DotsToMm(expected.Y, 8), Units.DotsToMm(actual.Y, dpmm), 1);
            }
        }
    }

    /// <summary>
    /// It renders, with ink on it, through the same path the canvas uses: preview ZPL with
    /// the samples substituted. An empty render is what a starter that cannot encode
    /// something looks like, and it would look like a broken app rather than a broken
    /// starter.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryStarter))]
    public void Create_RendersInkThroughThePreviewPath(string name)
    {
        LabelDocument document = Find(name).Create(8);
        string zpl = new TemplateSubstitutor(document.Markers).Substitute(
            new ZplGenerator().GeneratePreview(document, 0),
            inner => VariableValues.ForPreview(document, inner, DateTime.Now));

        RenderResult result = new BinaryKitsRenderer().Render(
            zpl, document.WidthMm, document.HeightMm, document.Dpmm);

        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Png);

        // A range rather than "more than nothing": too little is a label that failed to
        // draw its content, and a solid slab is a white ^GB read as black or a graphic
        // gone wrong.
        double ink = InkFraction(result.Png);
        Assert.InRange(ink, 0.005, 0.35);
    }

    /// <summary>The document with every marker replaced by the sample the preview would
    /// draw, so bounds and validation can be measured against what is on the canvas rather
    /// than against the delimiters.</summary>
    private static LabelDocument WithSamplesInPlace(LabelDocument document)
    {
        LabelDocument copy = LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(document));
        var substitutor = new TemplateSubstitutor(copy.Markers);
        string Resolve(string text) => substitutor.Substitute(
            text, inner => VariableValues.ForPreview(copy, inner, DateTime.Now));

        foreach (Element element in copy.Elements)
        {
            switch (element)
            {
                case TextElement text:
                    text.Text = Resolve(text.Text);
                    break;
                case BarcodeElement barcode:
                    barcode.Data = Resolve(barcode.Data);
                    break;
                case QrCodeElement qr:
                    qr.Data = Resolve(qr.Data);
                    break;
                case DataMatrixElement dm:
                    dm.Data = Resolve(dm.Data);
                    break;
                case Pdf417Element pdf:
                    pdf.Data = Resolve(pdf.Data);
                    break;
            }
        }

        return copy;
    }

    private static string Describe(QuietZoneFinding finding) =>
        finding.Intruder is null
            ? $"{finding.Code.Name} needs blank stock past the label edge"
            : $"{finding.Intruder.Name} sits in {finding.Code.Name}'s quiet zone";

    /// <summary>Share of the label covered in ink.</summary>
    private static double InkFraction(byte[] png)
    {
        using SKBitmap? bitmap = SKBitmap.Decode(png);
        Assert.NotNull(bitmap);

        int ink = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    ink++;
                }
            }
        }

        return (double)ink / (bitmap.Width * bitmap.Height);
    }
}
