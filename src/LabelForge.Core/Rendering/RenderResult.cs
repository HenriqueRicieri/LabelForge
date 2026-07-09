namespace LabelForge.Core.Rendering;

/// <summary>
/// The output of rendering ZPL to a raster image. Deliberately free of any UI or
/// SkiaSharp type so the Core project stays framework-agnostic; the App decodes
/// <see cref="Png"/> into whatever bitmap type it needs.
/// </summary>
/// <param name="Png">Encoded PNG bytes of the rendered label.</param>
/// <param name="WidthDots">Label width in printer dots (widthMm * dpmm, rounded).</param>
/// <param name="HeightDots">Label height in printer dots (heightMm * dpmm, rounded).</param>
/// <param name="UnknownCommands">ZPL commands the engine did not recognize (diagnostics).</param>
/// <param name="Errors">Errors the engine reported while analyzing the ZPL (diagnostics).</param>
public sealed record RenderResult(
    byte[] Png,
    int WidthDots,
    int HeightDots,
    IReadOnlyList<string> UnknownCommands,
    IReadOnlyList<string> Errors);
