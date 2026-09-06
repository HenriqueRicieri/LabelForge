using System.Globalization;
using System.Runtime.InteropServices;
using BinaryKits.Zpl.Label.Elements;
using BinaryKits.Zpl.Viewer;
using BinaryKits.Zpl.Viewer.Models;
using LabelForge.Bench;
using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;
using SkiaSharp;

// ZPL is culture-invariant and so is every number this prints.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

int corpusCount = 4;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--runs" && int.TryParse(args[i + 1], out int runs) && runs > 0)
    {
        Measure.Runs = runs;
    }
    else if (args[i] == "--corpus" && int.TryParse(args[i + 1], out int count) && count >= 0)
    {
        corpusCount = count;
    }
}

List<Scenario> scenarios = Scenarios.Build(corpusCount);
var renderer = new BinaryKitsRenderer();
var substitutor = new TemplateSubstitutor();

var frames = new List<string[]>();
var sideWork = new Dictionary<string, List<(string Scenario, double Ms)>>();

void Side(string work, string scenario, double milliseconds)
{
    if (!sideWork.TryGetValue(work, out var rows))
    {
        rows = [];
        sideWork[work] = rows;
    }

    rows.Add((scenario, milliseconds));
}

foreach ((string name, LabelDocument document) in scenarios)
{
    Console.Error.WriteLine($"measuring {name} ...");

    double widthMm = document.WidthMm;
    double heightMm = document.HeightMm;
    int dpmm = document.Dpmm;

    // One timestamp for the whole scenario, as the designer takes one per render pass,
    // so a clock variable resolves to the same text in every row.
    DateTime now = DateTime.Now;

    int margin = Units.MmToDots(ElementPlacement.PasteboardMarginMm, dpmm);
    double marginMm = Units.DotsToMm(margin, dpmm);

    string Preview(LabelDocument source, int offsetDots) =>
        substitutor.Substitute(
            new ZplGenerator().GeneratePreview(source, offsetDots),
            inner => VariableValues.ForPreview(source, inner, now));

    string preview = Preview(document, 0);
    string previewPasteboard = Preview(document, margin);

    double drawPng = Measure.Time(() => renderer.Render(preview, widthMm, heightMm, dpmm));
    double drawPasteboard = Measure.Time(() => renderer.Render(
        previewPasteboard, widthMm + 2 * marginMm, heightMm + 2 * marginMm, dpmm));

    RenderResult rendered = renderer.Render(preview, widthMm, heightMm, dpmm);
    if (rendered.Errors.Count > 0)
    {
        Console.Error.WriteLine("  render errors: " + string.Join("; ", rendered.Errors.Take(2)));
    }

    SKBitmap? decoded = SKBitmap.Decode(rendered.Png);
    double pngDecode = Measure.Time(() =>
    {
        using SKBitmap? bitmap = SKBitmap.Decode(rendered.Png);
    });
    double pngEncode = Measure.Time(() =>
    {
        using SKImage image = SKImage.FromBitmap(decoded);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
    });

    double rawPixels = Measure.Time(() => RawDraw(preview, document));

    // What a gesture layer costs: the moving element rendered by itself at label size.
    Element? sample = document.Elements.FirstOrDefault(e => e is BarcodeElement)
        ?? document.Elements.FirstOrDefault();
    double oneElement = 0;
    if (sample is not null)
    {
        var alone = new LabelDocument { WidthMm = widthMm, HeightMm = heightMm, Dpmm = dpmm };
        alone.Elements.Add(sample);
        string alonePreview = Preview(alone, 0);
        oneElement = Measure.Time(() => renderer.Render(alonePreview, widthMm, heightMm, dpmm));
    }

    frames.Add([
        name,
        $"{document.WidthDots}x{document.HeightDots}",
        Measure.Ms(drawPng),
        Measure.Ms(drawPasteboard),
        Measure.Ms(pngEncode),
        Measure.Ms(rawPixels),
        sample is null ? "n/a" : Measure.Ms(oneElement),
        Measure.Ms(pngDecode),
    ]);

    decoded?.Dispose();

    // Everything else the render pass does per frame.
    Side("ZplGenerator.Generate plus GeneratePreview and substitution", name, Measure.Time(() =>
    {
        new ZplGenerator().Generate(document, new GenerationContext { Now = now });
        Preview(document, margin);
    }));

    string json = LabelDocumentJson.Serialize(document);
    Side("LabelDocumentJson.Serialize (undo step, recovery snapshot)", name,
        Measure.Time(() => LabelDocumentJson.Serialize(document)));

    string snapshotPath = Path.Combine(Path.GetTempPath(), "labelforge-bench-snapshot.json");
    Side("Recovery snapshot file write", name,
        Measure.Time(() => File.WriteAllText(snapshotPath, json)));

    Side("Bounds for every element, placement classification", name, Measure.Time(() =>
    {
        var bounds = new ElementBoundsCalculator();
        foreach (Element element in document.Elements)
        {
            ElementPlacement.Classify(element, bounds.GetBounds(element), document);
        }
    }));

    Side("QuietZoneChecker.Check", name, Measure.Time(() => QuietZoneChecker.Check(document)));

    Side("TemplateVariables.Discover, barcode validation", name, Measure.Time(() =>
    {
        TemplateVariables.Discover(document);
        foreach (BarcodeElement barcode in document.Elements.OfType<BarcodeElement>())
        {
            BarcodeValidator.Validate(barcode, document.Markers);
        }
    }));

    Side("ZPL parse alone (ZplAnalyzer.Analyze)", name,
        Measure.Time(() => new ZplAnalyzer(new PrinterStorage()).Analyze(preview)));
}

Console.WriteLine();
Console.WriteLine($"Median of {Measure.Runs} runs after warm-up. Build: {Configuration()}.");
Console.WriteLine();

Table(
    ["Label", "Size (dots)", "Draw to PNG", "Draw to PNG, pasteboard", "PNG encode alone",
        "DrawSurface, raw pixels", "One element alone, PNG", "PNG decode"],
    frames);

Console.WriteLine();
Table(
    ["Work per frame", "Cost"],
    [.. sideWork.Select(entry =>
    {
        double low = entry.Value.Min(row => row.Ms);
        (string Scenario, double Ms) high = entry.Value.MaxBy(row => row.Ms);
        return new[]
        {
            entry.Key,
            Math.Abs(high.Ms - low) < 0.005
                ? Measure.Ms(low)
                : $"{Measure.Ms(low)} to {Measure.Ms(high.Ms)} (the high is {high.Scenario})",
        };
    })]);

static void Table(string[] header, List<string[]> rows)
{
    Console.WriteLine("| " + string.Join(" | ", header) + " |");
    Console.WriteLine("|" + string.Concat(header.Select(_ => "---|")));
    foreach (string[] row in rows)
    {
        Console.WriteLine("| " + string.Join(" | ", row) + " |");
    }
}

static string Configuration() =>
#if DEBUG
    "Debug, which is not comparable with anything; rerun with -c Release";
#else
    "Release";
#endif

/// <summary>The picture with no PNG on either end: BinaryKits draws into a surface we own
/// and the pixels are read back out. This is the column H2 turns into an IZplRenderer
/// output, and the direct call to the drawing library goes away with it.</summary>
static void RawDraw(string zpl, LabelDocument document)
{
    var storage = new PrinterStorage();
    AnalyzeInfo info = new ZplAnalyzer(storage).Analyze(zpl);
    ZplElementBase[] elements = info.LabelInfos.Length > 0
        ? info.LabelInfos[0].ZplElements
        : [];

    var drawer = new ZplElementDrawer(storage, BinaryKitsRenderer.CreateDefaultOptions());
    var imageInfo = new SKImageInfo(
        document.WidthDots, document.HeightDots, SKColorType.Bgra8888, SKAlphaType.Premul);

    using SKSurface surface = SKSurface.Create(imageInfo);
    surface.Canvas.Clear(SKColors.White);
    drawer.DrawSurface(surface, elements, document.WidthMm, document.HeightMm, document.Dpmm);

    var pixels = new byte[imageInfo.RowBytes * imageInfo.Height];
    GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
    try
    {
        surface.ReadPixels(imageInfo, handle.AddrOfPinnedObject(), imageInfo.RowBytes, 0, 0);
    }
    finally
    {
        handle.Free();
    }
}
