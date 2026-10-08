using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;

namespace LabelForge.Core.Export;

/// <summary>
/// The label as a picture, for the PNG and PDF exports. Those are for looking at
/// (approval, documentation, a support ticket), so they show what the canvas shows:
/// sample values for markers, the counter's first value and a real date, never the
/// printer-side ^SN or ^FC the engine cannot draw. And only what prints: nothing parked
/// off the label, nothing marked not to print. The ZPL export keeps the markers; this
/// is the other half.
/// </summary>
public static class LabelImage
{
    /// <summary>The ZPL that gets rendered: the preview's fields, substituted the way the
    /// canvas substitutes them, for the printable elements only.</summary>
    public static string Zpl(LabelDocument document, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(document);
        string zpl = new ZplGenerator().GenerateExportImage(document);
        return new TemplateSubstitutor(document.Markers).Substitute(
            zpl, inner => VariableValues.ForPreview(document, inner, now));
    }

    /// <summary>The label as PNG bytes at its own size and density.</summary>
    /// <exception cref="InvalidOperationException">The renderer drew nothing, with its
    /// reason, so an export never writes an empty file and calls it done.</exception>
    public static byte[] Png(IZplRenderer renderer, LabelDocument document, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        RenderResult result = renderer.Render(
            Zpl(document, now), document.WidthMm, document.HeightMm, document.Dpmm);
        if (!result.HasImage)
        {
            throw new InvalidOperationException(
                "The label could not be drawn: " + string.Join("; ", result.Errors));
        }

        return result.Png;
    }
}
