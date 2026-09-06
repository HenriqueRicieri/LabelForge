using System.Globalization;
using BinaryKits.Zpl.Viewer;
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

    // The path the designer actually takes since H2. The pasteboard variant is the one
    // every frame becomes the moment a single element sits off the label.
    double drawPixels = Measure.Time(() => renderer.Render(
        preview, widthMm, heightMm, dpmm, 0, RenderOutput.Pixels));
    double drawPixelsPasteboard = Measure.Time(() => renderer.Render(
        previewPasteboard, widthMm + 2 * marginMm, heightMm + 2 * marginMm, dpmm,
        0, RenderOutput.Pixels));

    // What a gesture layer costs: the moving element rendered by itself at label size,
    // on transparency so it composites over the rest.
    Element? sample = document.Elements.FirstOrDefault(e => e is BarcodeElement)
        ?? document.Elements.FirstOrDefault();
    double oneElement = 0;
    if (sample is not null)
    {
        var alone = new LabelDocument { WidthMm = widthMm, HeightMm = heightMm, Dpmm = dpmm };
        alone.Elements.Add(sample);
        string alonePreview = Preview(alone, 0);
        oneElement = Measure.Time(() => renderer.Render(
            alonePreview, widthMm, heightMm, dpmm, 0, RenderOutput.TransparentPixels));
    }

    frames.Add([
        name,
        $"{document.WidthDots}x{document.HeightDots}",
        Measure.Ms(drawPng),
        Measure.Ms(drawPasteboard),
        Measure.Ms(drawPixels),
        Measure.Ms(drawPixelsPasteboard),
        sample is null ? "n/a" : Measure.Ms(oneElement),
        Measure.Ms(pngEncode),
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
    ["Label", "Size (dots)", "Draw to PNG", "Draw to PNG, pasteboard", "Draw to pixels",
        "Draw to pixels, pasteboard", "One element alone, pixels", "PNG encode alone",
        "PNG decode"],
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

foreach (int interval in DragBench.PointerIntervalsMs)
{
    Console.WriteLine();
    Console.WriteLine($"A two second drag, a pointer position every {interval} ms, "
        + "the element moved each time. Same pixel render both sides; only the scheduling differs.");
    Console.WriteLine();

    Table(
        ["Label", "Frames shown, before H3", "Frames shown, with the queue",
            "Renders started, before / after", "Renders at once, before / after"],
        DragBench.Run(scenarios, interval));
}

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
