namespace LabelForge.Core.Model;

/// <summary>Blank stock a symbol needs on each side, in dots.</summary>
public readonly record struct QuietZoneMargin(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Left <= 0 && Top <= 0 && Right <= 0 && Bottom <= 0;

    /// <summary>The symbol's footprint grown by the blank it needs around it.</summary>
    public DotRect Around(DotRect bounds) => new(
        bounds.X - Left,
        bounds.Y - Top,
        bounds.Width + Left + Right,
        bounds.Height + Top + Bottom);

    /// <summary>The same margin on a field turned clockwise by <paramref name="orientation"/>:
    /// at 90 degrees what was on the left is on top, as the field's own drawing turns.</summary>
    public QuietZoneMargin Turned(Orientation orientation) => orientation switch
    {
        Orientation.Rotated90 => new(Bottom, Left, Top, Right),
        Orientation.Rotated180 => new(Right, Bottom, Left, Top),
        Orientation.Rotated270 => new(Top, Right, Bottom, Left),
        _ => this,
    };
}

/// <summary>
/// The minimum blank margin each symbology needs around it for a scanner to find the
/// symbol at all. These are the figures the symbology standards state, in modules, so
/// they scale with the module width the element is drawn at.
///
/// This is blank stock, not ink: it never reaches the ZPL and it is not part of the
/// footprint. It is what has to be kept clear, which is why it is checked rather than
/// drawn, and why the footprints it is measured from state the symbol alone.
///
/// The offline renderer draws no quiet zone of its own: measured across every symbology
/// here, the first bar lands exactly on the field origin. So a barcode flush against the
/// label edge really does print with no quiet zone at all, and saying so is the point.
/// </summary>
public static class QuietZone
{
    /// <summary>The blank a symbol needs, or an empty margin for elements that are not
    /// symbols at all.</summary>
    public static QuietZoneMargin For(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        QuietZoneMargin margin = element switch
        {
            // Linear symbologies state a horizontal quiet zone only; nothing above or
            // below the bars is required, which is why a caption can sit right under one.
            BarcodeElement bar => Horizontal(
                bar.ModuleWidthDots,
                bar.Symbology switch
                {
                    // ISO/IEC 15420: 11 modules leading, 7 trailing.
                    BarcodeSymbology.Ean13 => (11, 7),

                    // 9 modules either side.
                    BarcodeSymbology.UpcA => (9, 9),

                    // ISO/IEC 15417 (Code 128), 16388 (Code 39) and 16390 (Interleaved
                    // 2 of 5) all ask for 10 modules either side.
                    _ => (10, 10),
                }),

            // ISO/IEC 18004: 4 modules on all four sides.
            QrCodeElement qr => All(qr.Magnification, 4),

            // ISO/IEC 16022: one module of blank all round, the narrowest of the lot.
            DataMatrixElement dm => All(dm.ModuleSizeDots, 1),

            // ISO/IEC 15438: 2 modules on all four sides.
            Pdf417Element pdf => All(pdf.ModuleWidthDots, 2),

            _ => default,
        };

        // The margin belongs at the ends of the bars wherever they point. The 2D symbols
        // ask for the same on every side, so turning them changes nothing.
        return element is BarcodeElement turned ? margin.Turned(turned.Orientation) : margin;
    }

    /// <summary>
    /// The area that has to stay clear: the symbol grown by its margin. Measured from the
    /// symbol itself, not from the digit EAN-13 and UPC-A print outside their guard bars,
    /// because the standards place that digit inside the leading quiet zone.
    /// </summary>
    public static DotRect Area(Element element, ElementBoundsCalculator bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        return For(element).Around(bounds.GetSymbolBounds(element));
    }

    /// <summary>True for the element types a quiet zone means anything for.</summary>
    public static bool Applies(Element element) =>
        element is BarcodeElement or QrCodeElement or DataMatrixElement or Pdf417Element;

    private static QuietZoneMargin Horizontal(int moduleDots, (int Left, int Right) modules)
    {
        int module = Math.Max(moduleDots, 1);
        return new QuietZoneMargin(modules.Left * module, 0, modules.Right * module, 0);
    }

    private static QuietZoneMargin All(int moduleDots, int modules)
    {
        int side = Math.Max(moduleDots, 1) * modules;
        return new QuietZoneMargin(side, side, side, side);
    }
}
