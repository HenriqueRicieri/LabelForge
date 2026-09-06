using System.Globalization;
using System.Runtime.InteropServices;
using LabelForge.Core.Model;
using LabelForge.Core.Zpl;
using BinaryKits.Zpl.Label.Elements;
using BinaryKits.Zpl.Viewer;
using BinaryKits.Zpl.Viewer.ElementDrawers;
using BinaryKits.Zpl.Viewer.Models;
using SkiaSharp;

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
/// - Two ways out, same picture: <see cref="RenderOutput.Png"/> goes through the engine's
///   own Draw, and the pixel outputs go through its DrawSurface into a surface we own.
///   DrawSurface clears that surface itself, to transparency, and ignores
///   <see cref="DrawerOptions.OpaqueBackground"/> (both measured), so a white background
///   is composited UNDER the finished ink rather than cleared to beforehand.
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
    ///
    /// The font loader is the other load-bearing setting. Left alone, the engine picks a
    /// substitute for ZPL's font 0 from whatever the machine has installed, and two
    /// machines disagreed about the width of the same label by 22 per cent. Pinning it
    /// makes every machine draw the same picture, which is what lets
    /// <see cref="Model.TextMetrics"/> be a fact rather than a local observation. See
    /// <see cref="PreviewFont"/> for the measurement that chose the typeface.
    /// </summary>
    public static DrawerOptions CreateDefaultOptions()
    {
        var options = new DrawerOptions
        {
            OpaqueBackground = true,
            Antialias = true,
            ReplaceDashWithEnDash = false,
            ReplaceUnderscoreWithEnSpace = false,
        };

        // Asked by ZPL designator ("0", "A", ...), not by family name, so this depends on
        // nothing about what fonts are called or which are installed.
        options.FontManager.FontLoader = PreviewFont.Resolve;
        return options;
    }

    public RenderResult Render(
        string zpl,
        double widthMm,
        double heightMm,
        int dpmm,
        int labelIndex = 0,
        RenderOutput output = RenderOutput.Png)
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
            return RenderCore(zpl, widthMm, heightMm, dpmm, labelIndex, output);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private RenderResult RenderCore(
        string zpl, double widthMm, double heightMm, int dpmm, int labelIndex, RenderOutput output)
    {
        var storage = new PrinterStorage();
        var analyzer = new ZplAnalyzer(storage);

        // Two render-time repairs to the graphic commands, both for the same reason: the
        // engine loses a logo silently where a printer draws it. It stores and recalls
        // downloaded graphics by the literal name, so the short form a printer accepts
        // ("~DGLOGO" then "^XGLOGO") renders with no logo at all; and it throws on the
        // framing a file writes around a payload, a trailing tab or a "//" comment being
        // enough to discard the whole download.
        // Both rewrite only the copy handed to the engine; the caller's ZPL is untouched.
        AnalyzeInfo info = analyzer.Analyze(
            ZplGraphicScanner.QualifyGraphicNames(
                ZplGraphicScanner.CompactGraphicData(zpl)));

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

        int widthDots = Units.MmToDots(widthMm, dpmm);
        int heightDots = Units.MmToDots(heightMm, dpmm);

        // Drawing can throw when an element cannot be rendered, most notably when an
        // Atak template marker (e.g. "##CODIGO_BARRAS##") lands inside a barcode
        // field, because a linear barcode cannot encode non-conforming characters.
        // The viewer must never crash on real input, so we degrade: record the
        // engine error and return an empty image rather than propagating. Both ways
        // out do it the same way, and the empty image keeps the shape of the output
        // that was asked for so a caller can still tell which one it got.
        if (output == RenderOutput.Png)
        {
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

            return new RenderResult(png, widthDots, heightDots, unknownCommands, errors, labelCount);
        }

        byte[] pixels;
        int stride;
        try
        {
            (pixels, stride) = DrawPixels(
                drawer, elements, widthMm, heightMm, dpmm, widthDots, heightDots,
                transparent: output == RenderOutput.TransparentPixels);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            pixels = Array.Empty<byte>();
            stride = 0;
        }

        return new RenderResult(
            Array.Empty<byte>(), widthDots, heightDots, unknownCommands, errors, labelCount,
            pixels,
            stride,
            pixels.Length > 0 ? widthDots : 0,
            pixels.Length > 0 ? heightDots : 0);
    }

    /// <summary>
    /// The same drawing with no PNG on either end: the engine draws into a surface we own
    /// and the pixels come straight back out. BGRA premultiplied because that is what a
    /// desktop compositor wants and what Avalonia's raw-pixel bitmap takes.
    ///
    /// The background is put on afterwards, and that is not a preference. DrawSurface
    /// clears the surface it is handed before drawing and ignores
    /// <see cref="DrawerOptions.OpaqueBackground"/>, so anything cleared in front of it is
    /// discarded: measured, and the reason this does not simply clear to white. Drawing
    /// white through DstOver instead puts it under the finished ink, which reproduces the
    /// encoded render byte for byte (RenderOutputTests). Leaving it off is what a gesture
    /// layer wants: ink on nothing, compositing over the rest of the label.
    ///
    /// A reversed field (^FR) knocks out to what it is drawn over, so over transparency it
    /// knocks a hole rather than painting white. That is correct for the opaque output,
    /// where the white is already there, and it is the one thing a transparent layer gets
    /// wrong while it stands in for the whole label.
    /// </summary>
    private static (byte[] Pixels, int Stride) DrawPixels(
        ZplElementDrawer drawer,
        ZplElementBase[] elements,
        double widthMm,
        double heightMm,
        int dpmm,
        int widthDots,
        int heightDots,
        bool transparent)
    {
        if (widthDots <= 0 || heightDots <= 0)
        {
            return (Array.Empty<byte>(), 0);
        }

        var info = new SKImageInfo(widthDots, heightDots, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKSurface? surface = SKSurface.Create(info);
        if (surface is null)
        {
            // Skia refuses a surface it cannot allocate, most plausibly a label so large
            // the buffer does not fit. Reported like any other render failure.
            throw new InvalidOperationException(
                $"Could not allocate a {widthDots} x {heightDots} rendering surface.");
        }

        drawer.DrawSurface(surface, elements, widthMm, heightMm, dpmm);
        if (!transparent)
        {
            surface.Canvas.DrawColor(SKColors.White, SKBlendMode.DstOver);
        }

        var pixels = new byte[(long)info.RowBytes * info.Height];
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            if (!surface.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0))
            {
                throw new InvalidOperationException("Could not read the rendered pixels back.");
            }
        }
        finally
        {
            handle.Free();
        }

        return (pixels, info.RowBytes);
    }
}
