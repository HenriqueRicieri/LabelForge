namespace LabelForge.Core.Rendering;

/// <summary>
/// The output of rendering ZPL to a raster image. Deliberately free of any UI or
/// SkiaSharp type so the Core project stays framework-agnostic: the picture leaves here
/// as encoded bytes or as a plain buffer, and the App turns whichever it asked for into
/// its own bitmap type.
/// </summary>
/// <param name="Png">Encoded PNG bytes of the rendered label. Empty when the caller asked
/// for pixels, and empty when the render failed (the reason is in <paramref name="Errors"/>).</param>
/// <param name="WidthDots">Label width in printer dots (widthMm * dpmm, rounded).</param>
/// <param name="HeightDots">Label height in printer dots (heightMm * dpmm, rounded).</param>
/// <param name="UnknownCommands">ZPL commands the engine did not recognize (diagnostics).</param>
/// <param name="Errors">Errors the engine reported while analyzing the ZPL (diagnostics).</param>
/// <param name="LabelCount">Number of label blocks (^XA..^XZ) found in the source.</param>
/// <param name="Pixels">Raw BGRA pixels, premultiplied, top row first, when the caller
/// asked for <see cref="RenderOutput.Pixels"/> or <see cref="RenderOutput.TransparentPixels"/>.
/// Null when it asked for a PNG, which is how a caller tells the two apart; empty when the
/// pixel render failed.</param>
/// <param name="Stride">Bytes per row of <paramref name="Pixels"/>, which is not always
/// four times the width. Zero when there are no pixels.</param>
/// <param name="PixelWidth">Width of <paramref name="Pixels"/> in pixels. Zero when there
/// are none. Equal to <paramref name="WidthDots"/>, stated separately so a caller reading
/// the buffer never has to assume that.</param>
/// <param name="PixelHeight">Height of <paramref name="Pixels"/> in pixels. Zero when
/// there are none.</param>
public sealed record RenderResult(
    byte[] Png,
    int WidthDots,
    int HeightDots,
    IReadOnlyList<string> UnknownCommands,
    IReadOnlyList<string> Errors,
    int LabelCount,
    byte[]? Pixels = null,
    int Stride = 0,
    int PixelWidth = 0,
    int PixelHeight = 0)
{
    /// <summary>Whether there is a picture here at all, whichever output was asked for.
    /// A render that failed still returns a result, carrying the reason and no image, so
    /// callers ask this rather than measuring one of the two buffers and guessing which
    /// one was filled.</summary>
    public bool HasImage => Pixels is not null ? Pixels.Length > 0 : Png.Length > 0;
}
