using LabelForge.Core.Imaging;
using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Core.Io;

/// <summary>One graphic recovered from a ZPL file, ready to drop on the canvas.</summary>
/// <param name="Name">The ~DG storage name, or a generated one for an inline ^GF field.</param>
/// <param name="Element">The element to add. Its origin is where the label drew the
/// graphic; a graphic the file never draws lands at 0,0.</param>
/// <param name="Placements">How many times the source label drew this graphic.</param>
public sealed record ImportedGraphic(string Name, ImageElement Element, int Placements);

/// <param name="Graphics">What was recovered, in the order the file defines it.</param>
/// <param name="Warnings">What could not be recovered, and why. Never silent: a label
/// that half imported is worse than one that says which half.</param>
public sealed record ZplGraphicImportResult(
    IReadOnlyList<ImportedGraphic> Graphics,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Recovers the graphics of an existing ZPL label as editable image elements.
///
/// A ~DG bitmap re-enters the app as an ordinary <see cref="ImageElement"/> holding a
/// monochrome PNG, not as a new element type. That is the whole point: once imported it
/// moves, resizes, saves into the .lfl and regenerates like any other image, and the
/// designer needs no special case for "graphic that came from someone else's label".
/// Regenerating emits ^GF or ~DG from the bits, so the round trip is by value, not by
/// name, and nothing depends on what the origin printer happens to still have in memory.
/// </summary>
public static class ZplGraphicImport
{
    public static ZplGraphicImportResult FromZpl(string zpl)
    {
        if (string.IsNullOrEmpty(zpl))
        {
            return new ZplGraphicImportResult([], []);
        }

        ZplGraphicScan scan = ZplGraphicScanner.Scan(zpl);
        var graphics = new List<ImportedGraphic>();
        var warnings = new List<string>();
        var undrawn = new List<string>();
        var undecodable = new List<string>();

        foreach (ZplGraphicDefinition definition in scan.Definitions)
        {
            ZplGraphicPlacement[] uses = scan.Placements
                .Where(p => string.Equals(p.Name, definition.Name, StringComparison.Ordinal))
                .ToArray();

            if (Build(definition, uses.FirstOrDefault()) is not { } element)
            {
                undecodable.Add(definition.Name);
                continue;
            }

            if (uses.Length == 0)
            {
                undrawn.Add(definition.Name);
            }

            graphics.Add(new ImportedGraphic(definition.Name, element, uses.Length));
        }

        foreach (ZplGraphicPlacement placement in scan.Placements.Where(p => p.Inline is not null))
        {
            ZplGraphicDefinition inline = placement.Inline!;
            if (Build(inline, placement) is not { } element)
            {
                undecodable.Add(inline.Name);
                continue;
            }

            graphics.Add(new ImportedGraphic(inline.Name, element, 1));
        }

        if (scan.UnresolvedNames.Count > 0)
        {
            // The bitmap lives in the printer's memory from an earlier download, so it
            // is genuinely not in this file. Naming them beats a count.
            warnings.Add(
                $"{Describe(scan.UnresolvedNames.Count, "graphic")} recalled by name but never "
                + $"downloaded in this file, so the image is not here: "
                + $"{string.Join(", ", scan.UnresolvedNames)}.");
        }

        if (undrawn.Count > 0)
        {
            warnings.Add(
                $"{Describe(undrawn.Count, "graphic")} downloaded but never placed on a label; "
                + $"imported at the top-left corner: {string.Join(", ", undrawn)}.");
        }

        if (undecodable.Count > 0)
        {
            warnings.Add(
                $"{Describe(undecodable.Count, "graphic")} could not be read: "
                + $"{string.Join(", ", undecodable)}.");
        }

        return new ZplGraphicImportResult(graphics, warnings);
    }

    private static ImageElement? Build(
        ZplGraphicDefinition definition, ZplGraphicPlacement? placement)
    {
        GraphicBitmap? bitmap = GraphicField.Decode(
            definition.Data, definition.BytesPerRow, definition.TotalBytes);
        if (bitmap is null)
        {
            return null;
        }

        // ^XG magnifies by replicating dots, so a magnified graphic is baked to its
        // printed size here. Storing the small bitmap and letting the generator scale it
        // back up would go through Skia's smooth resampling and come out a slightly
        // different label than the one being imported; replication keeps it identical.
        (bool[] black, int width, int height) = Magnify(
            bitmap, placement?.MagnificationX ?? 1, placement?.MagnificationY ?? 1);

        return new ImageElement
        {
            Name = definition.Name,
            ImageData = MonochromePng.Encode(black, width, height),
            SourcePixelWidth = width,
            SourcePixelHeight = height,

            // One dot per pixel, so regenerating reproduces these bits exactly.
            WidthDots = width,
            HeightDots = height,

            // The bits are already one bit deep. Thresholding reproduces them exactly
            // at natural size; diffusing them would invent texture that is not there.
            Dithering = DitherMode.Threshold,

            // An origin left of or above the label cannot be expressed as ^FO, and the
            // pasteboard starts at zero, so a negative source origin clamps.
            X = Math.Max(placement?.X ?? 0, 0),
            Y = Math.Max(placement?.Y ?? 0, 0),
        };
    }

    /// <summary>Nearest-neighbour expansion, which is what the printer does for ^XG
    /// magnification: every dot becomes a magX by magY block, no new greys invented.</summary>
    private static (bool[] Black, int Width, int Height) Magnify(
        GraphicBitmap bitmap, int magX, int magY)
    {
        if (magX <= 1 && magY <= 1)
        {
            return (bitmap.Black, bitmap.Width, bitmap.Height);
        }

        int width = bitmap.Width * magX;
        int height = bitmap.Height * magY;
        var black = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = y / magY * bitmap.Width;
            int targetRow = y * width;
            for (int x = 0; x < width; x++)
            {
                black[targetRow + x] = bitmap.Black[sourceRow + x / magX];
            }
        }

        return (black, width, height);
    }

    private static string Describe(int count, string noun) =>
        count == 1 ? $"1 {noun} is" : $"{count} {noun}s are";
}
