using System.Globalization;
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

    public RenderResult Render(string zpl, double widthMm, double heightMm, int dpmm, int labelIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(zpl);

        // BinaryKits parses ZPL decimals (for example the ^BY wide-bar ratio "3.0")
        // with the ambient culture. On a locale that uses a comma decimal separator
        // (e.g. pt-BR) it reads "3.0" as 30, producing wildly oversized barcodes.
        // ZPL is always culture-invariant, so pin the culture for the whole call.
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            return RenderCore(zpl, widthMm, heightMm, dpmm, labelIndex);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private RenderResult RenderCore(string zpl, double widthMm, double heightMm, int dpmm, int labelIndex)
    {
        var storage = new PrinterStorage();
        var analyzer = new ZplAnalyzer(storage);
        AnalyzeInfo info = analyzer.Analyze(zpl);

        var unknownCommands = info.UnknownCommands ?? Array.Empty<string>();
        var errors = new List<string>(info.Errors ?? Array.Empty<string>());

        // Render the requested label block. A file can hold several (^XA..^XZ); the
        // viewer enumerates them and lets the user pick one. Clamp to a valid index.
        int labelCount = info.LabelInfos.Length;
        int index = labelCount > 0 ? Math.Clamp(labelIndex, 0, labelCount - 1) : 0;
        ZplElementBase[] elements = labelCount > 0
            ? info.LabelInfos[index].ZplElements
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

        return new RenderResult(png, widthDots, heightDots, unknownCommands, errors, labelCount);
    }
}
