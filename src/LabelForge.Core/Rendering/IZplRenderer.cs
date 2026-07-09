namespace LabelForge.Core.Rendering;

/// <summary>
/// Renders ZPL source into a raster image. Implementations may be offline
/// (the default, <see cref="BinaryKitsRenderer"/>) or online (a Labelary
/// compare mode). The interface is intentionally minimal and returns a
/// <see cref="RenderResult"/> of PNG bytes plus diagnostics so callers never
/// depend on the underlying rendering library.
/// </summary>
public interface IZplRenderer
{
    /// <param name="zpl">Raw ZPL source (may contain multiple ^XA..^XZ blocks).</param>
    /// <param name="widthMm">Label width in millimeters.</param>
    /// <param name="heightMm">Label height in millimeters.</param>
    /// <param name="dpmm">Print density in dots per millimeter (203 dpi = 8, 300 = 12, 600 = 24).</param>
    /// <param name="labelIndex">Which label block to render when the source has several (0-based).</param>
    RenderResult Render(string zpl, double widthMm, double heightMm, int dpmm, int labelIndex = 0);
}
