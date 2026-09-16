using LabelForge.Core.Editing;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;

namespace LabelForge.Bench;

internal static class GestureBench
{
    public static void Run()
    {
        Scenario scenario = Scenarios.Build(0).Single(s => s.Document.Dpmm == 24);
        LabelDocument document = scenario.Document;
        Element moving = document.Elements.OfType<BarcodeElement>().First();
        GestureLayerPlan plan = GestureLayers.Split(document, [moving]);
        int margin = Units.MmToDots(ElementPlacement.PasteboardMarginMm, document.Dpmm);
        var viewport = new DotRect(-margin, -margin,
            document.WidthDots + 2 * margin, document.HeightDots + 2 * margin);
        DotRect crop = GestureLayers.GetMovingViewport(plan.Moving);
        DateTime now = DateTime.Now;

        RenderResult Render(IReadOnlyList<Element> elements, DotRect bounds, RenderOutput output)
        {
            string zpl = new ZplGenerator().GeneratePreviewLayer(document, elements, bounds);
            zpl = new TemplateSubstitutor().Substitute(zpl,
                inner => VariableValues.ForPreview(document, inner, now));
            RenderResult result = new BinaryKitsRenderer().Render(zpl,
                Units.DotsToMm(bounds.Width, document.Dpmm), Units.DotsToMm(bounds.Height, document.Dpmm),
                document.Dpmm, output: output);
            if (!result.HasImage || result.Errors.Count != 0)
                throw new InvalidOperationException(string.Join("; ", result.Errors));
            return result;
        }

        double full = Measure.Time(() => Render(document.Elements.ToArray(), viewport, RenderOutput.Pixels));
        double below = Measure.Time(() => Render(plan.Below, viewport, RenderOutput.Pixels));
        double above = Measure.Time(() => Render(plan.Above, viewport, RenderOutput.TransparentPixels));
        double cropped = Measure.Time(() => Render(plan.Moving, crop, RenderOutput.TransparentPixels));
        double preparation = Measure.Time(() => Parallel.Invoke(
            () => Render(plan.Below, viewport, RenderOutput.Pixels),
            () => Render(plan.Moving, crop, RenderOutput.TransparentPixels),
            () => Render(plan.Above, viewport, RenderOutput.TransparentPixels)));

        Console.WriteLine($"Gesture layers: {scenario.Name}; median of {Measure.Runs} runs");
        Console.WriteLine($"Full pasteboard: {viewport.Width}x{viewport.Height}, {4L * viewport.Width * viewport.Height:N0} pixel bytes");
        Console.WriteLine($"Moving crop: {crop.Width}x{crop.Height}, {4L * crop.Width * crop.Height:N0} pixel bytes");
        Console.WriteLine($"Full render per ordinary gesture update: {Measure.Ms(full)}");
        Console.WriteLine($"Below layer: {Measure.Ms(below)}; above layer: {Measure.Ms(above)}");
        Console.WriteLine($"Initial parallel preparation: {Measure.Ms(preparation)}");
        Console.WriteLine($"Moving crop render: {Measure.Ms(cropped)} ({full / cropped:0.0}x less render time than full pasteboard)");
        Console.WriteLine("Move after preparation: bitmap translation, no renderer call.");
        Console.WriteLine("Times include ZPL generation and sample substitution, exclude Avalonia upload and presentation.");
    }
}
