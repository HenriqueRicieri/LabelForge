using BinaryKits.Zpl.Label.Elements;
using BinaryKits.Zpl.Viewer;
using BinaryKits.Zpl.Viewer.ElementDrawers;
using BinaryKits.Zpl.Viewer.Models;

namespace LabelForge.Core.Rendering;

/// <summary>
/// Offline ZPL renderer backed by BinaryKits.Zpl.Viewer. This is the default
/// engine and requires no network access.
///
/// Notes for maintainers:
/// - BinaryKits works in millimeters + dots-per-millimeter, matching <see cref="IZplRenderer"/>.
/// - It ignores ^PW/^LL, so the label size is passed explicitly by the caller.
/// - A fresh <see cref="PrinterStorage"/> and <see cref="ZplAnalyzer"/> are created per call:
///   they hold mutable ~DG downloaded-graphic state, and construction is cheap.
/// - Draw is CPU-bound and synchronous. Callers driving a live preview must run it
///   off the UI thread (see the App's render pipeline).
/// </summary>
public sealed class BinaryKitsRenderer : IZplRenderer
{
    private readonly DrawerOptions _options;

    public BinaryKitsRenderer(DrawerOptions? options = null)
    {
        _options = options ?? CreateDefaultOptions();
    }

    /// <summary>
    /// Default drawer options tuned for label fidelity. Crucially, the "smart"
    /// character substitutions are disabled so literal text such as Atak template
    /// markers (for example "##FILIAL_DOCUMENTO##") renders exactly as written.
    /// </summary>
    public static DrawerOptions CreateDefaultOptions() => new()
    {
        OpaqueBackground = true,
        Antialias = true,
        ReplaceDashWithEnDash = false,
        ReplaceUnderscoreWithEnSpace = false,
    };

    public RenderResult Render(string zpl, double widthMm, double heightMm, int dpmm)
    {
        ArgumentNullException.ThrowIfNull(zpl);

        var storage = new PrinterStorage();
        var analyzer = new ZplAnalyzer(storage);
        AnalyzeInfo info = analyzer.Analyze(zpl);

        var unknownCommands = info.UnknownCommands ?? Array.Empty<string>();
        var errors = new List<string>(info.Errors ?? Array.Empty<string>());

        // v1: render the first label block. Multi-block files (setup block + label)
        // are enumerated by the viewer UI; the renderer stays single-label.
        ZplElementBase[] elements = info.LabelInfos.Length > 0
            ? info.LabelInfos[0].ZplElements
            : Array.Empty<ZplElementBase>();

        var drawer = new ZplElementDrawer(storage, _options);

        // Draw can throw when an element cannot be rendered, most notably when an
        // Atak template marker (e.g. "##CODIGO_BARRAS##") lands inside a barcode
        // field, because a linear barcode cannot encode non-conforming characters.
        // The viewer must never crash on real input, so we degrade: record the
        // engine error and return an empty image rather than propagating.
        byte[] png;
        try
        {
            png = drawer.Draw(elements, widthMm, heightMm, dpmm);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            png = Array.Empty<byte>();
        }

        int widthDots = (int)Math.Round(widthMm * dpmm, MidpointRounding.AwayFromZero);
        int heightDots = (int)Math.Round(heightMm * dpmm, MidpointRounding.AwayFromZero);

        return new RenderResult(png, widthDots, heightDots, unknownCommands, errors);
    }
}
