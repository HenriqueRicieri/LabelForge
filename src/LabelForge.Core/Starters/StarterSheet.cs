using LabelForge.Core.Model;

namespace LabelForge.Core.Starters;

/// <summary>
/// Places elements on a starter label in millimeters.
///
/// A starter is a physical label rather than a bitmap: the same design has to come out the
/// same size on a 203, 300 or 600 dpi printer, and a dot is three different lengths on
/// those three machines. So the layouts state millimeters and every one of them is
/// converted here, through <see cref="Units.MmToDots"/>, the one helper.
///
/// What cannot scale continuously is a module. A bar, a QR cell and a border are whole
/// dots wide, so <see cref="Module"/> rounds to a dot and never goes below one, which
/// makes a barcode's total width move by up to a rounding step from one density to the
/// next. That is physically true, since the printer has no half dots either, and it is why
/// the layouts leave room at the edges instead of filling the stock.
/// </summary>
internal sealed class StarterSheet
{
    private readonly LabelDocument _document;
    private int _z;

    internal StarterSheet(LabelDocument document) => _document = document;

    /// <summary>Millimeters to dots at this label's density.</summary>
    internal int Dots(double mm) => Units.MmToDots(mm, _document.Dpmm);

    /// <summary>A module width in dots: whole dots, and at least one whatever the
    /// density, because a zero-width bar is not a barcode.</summary>
    internal int Module(double mm) => Math.Max(1, Dots(mm));

    /// <summary>What the preview shows in place of a marker. Seeded so a starter draws a
    /// real-looking label on the canvas rather than a row of delimiters.</summary>
    internal void Sample(string variable, string value) => _document.SampleValues[variable] = value;

    /// <summary>A marker written in this document's own delimiters.</summary>
    internal string Marker(string variable) => _document.Markers.Marker(variable);

    /// <summary>Plain text: a caption, a heading, anything the data system does not
    /// fill in.</summary>
    internal TextElement Text(string name, double xMm, double yMm, double heightMm, string text) =>
        Add(new TextElement
        {
            Name = name,
            X = Dots(xMm),
            Y = Dots(yMm),
            FontHeightDots = Dots(heightMm),
            Text = text,
        });

    /// <summary>
    /// A field carrying a template marker, and the sample the preview draws in its place.
    ///
    /// One call for both halves so a starter cannot end up with a marker nobody gave a
    /// sample for, which on the canvas is a field reading "##TO_NAME##" and in a barcode
    /// is a symbol encoding the delimiters.
    /// </summary>
    internal TextElement Field(
        string name, double xMm, double yMm, double heightMm, string variable, string sample)
    {
        Sample(variable, sample);
        return Text(name, xMm, yMm, heightMm, Marker(variable));
    }

    /// <summary>A horizontal rule: a solid bar, which is what a line is in ZPL.</summary>
    internal LineElement Rule(string name, double xMm, double yMm, double lengthMm, double thicknessMm) =>
        Add(new LineElement
        {
            Name = name,
            X = Dots(xMm),
            Y = Dots(yMm),
            LengthDots = Dots(lengthMm),
            ThicknessDots = Math.Max(1, Dots(thicknessMm)),
        });

    internal BoxElement Box(
        string name, double xMm, double yMm, double widthMm, double heightMm, double thicknessMm) =>
        Add(new BoxElement
        {
            Name = name,
            X = Dots(xMm),
            Y = Dots(yMm),
            WidthDots = Dots(widthMm),
            HeightDots = Dots(heightMm),
            ThicknessDots = Math.Max(1, Dots(thicknessMm)),
        });

    /// <summary>
    /// A linear barcode, sized by its bar height and its module in millimeters.
    ///
    /// Left aligned rather than centred, deliberately. A field that carries a marker at
    /// design time carries a value of some other length at print time, so there is no
    /// centre to hold: centring the design would leave every printed label off centre by
    /// half the difference. Real templates line their barcodes up on the left for exactly
    /// this reason.
    /// </summary>
    internal BarcodeElement Barcode(
        string name,
        BarcodeSymbology symbology,
        double xMm,
        double yMm,
        string data,
        double barHeightMm,
        double moduleMm,
        bool interpretationLine = true) =>
        Add(new BarcodeElement
        {
            Name = name,
            Symbology = symbology,
            X = Dots(xMm),
            Y = Dots(yMm),
            Data = data,
            HeightDots = Dots(barHeightMm),
            ModuleWidthDots = Module(moduleMm),
            PrintInterpretationLine = interpretationLine,
        });

    /// <summary>A QR code sized by its cell rather than by ZPL's magnification factor,
    /// which is a count of dots and so a different size on every printer.</summary>
    internal QrCodeElement Qr(string name, double xMm, double yMm, string data, double cellMm) =>
        Add(new QrCodeElement
        {
            Name = name,
            X = Dots(xMm),
            Y = Dots(yMm),
            Data = data,
            Magnification = Module(cellMm),
        });

    /// <summary>Adds an element in the order it was placed, which is also the order it
    /// draws in.</summary>
    private T Add<T>(T element)
        where T : Element
    {
        element.ZOrder = _z++;
        _document.Elements.Add(element);
        return element;
    }
}
