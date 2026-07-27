using System.Text.Json.Serialization;

namespace LabelForge.Core.Model;

/// <summary>Which point of a field its origin names, and so which ZPL command places
/// it. ZPL has exactly these two, and they are not interchangeable; see
/// <see cref="FieldTypeset"/> for the geometry and <see cref="Element.Anchor"/> for
/// why the difference is kept rather than converted away.</summary>
public enum FieldAnchor
{
    /// <summary>`^FO`: the top-left corner of the drawn field.</summary>
    TopLeft,

    /// <summary>`^FT`: the field's bottom-left, which for text is the baseline rather
    /// than the bottom of the line.</summary>
    Baseline,
}

/// <summary>
/// Base type for everything placed on a label. Geometry is stored in printer dots
/// with a top-left origin, matching ZPL's ^FO field origin, so what you place is what
/// prints with no generation-time rounding.
/// The polymorphic JSON attributes drive the .lfl file format and undo snapshots;
/// new element types must be registered here or they will not round-trip.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(BarcodeElement), "barcode")]
[JsonDerivedType(typeof(QrCodeElement), "qr")]
[JsonDerivedType(typeof(DataMatrixElement), "datamatrix")]
[JsonDerivedType(typeof(Pdf417Element), "pdf417")]
[JsonDerivedType(typeof(ImageElement), "image")]
[JsonDerivedType(typeof(LineElement), "line")]
[JsonDerivedType(typeof(BoxElement), "box")]
[JsonDerivedType(typeof(EllipseElement), "ellipse")]
[JsonDerivedType(typeof(DiagonalLineElement), "diagonal")]
public abstract class Element
{
    /// <summary>Stable identity for selection, undo, and serialization. Settable so
    /// clipboard paste can assign a fresh identity to a deserialized copy.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-facing name shown in the layers/objects panel.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>X of the field origin in dots. Which corner of the drawn field that
    /// origin names is <see cref="Anchor"/>'s business.</summary>
    public int X { get; set; }

    /// <summary>Y of the field origin in dots; see <see cref="X"/>.</summary>
    public int Y { get; set; }

    /// <summary>
    /// Which point of the field <see cref="X"/> and <see cref="Y"/> name, which is also
    /// which ZPL command places it: `^FO` for the top-left corner, `^FT` for the
    /// bottom-left (the baseline, for text).
    ///
    /// It matters because the two anchors behave differently when the content changes
    /// size. A `^FO` field grows right and down from a fixed corner; a `^FT` field at 180
    /// or 270 degrees grows away from a fixed corner in the other direction, so where it
    /// prints depends on how wide its own content turns out to be. That is exactly what
    /// real labels rely on, and it is why the anchor is modelled instead of being
    /// converted away: a template field carries a marker at design time and a value of
    /// some other length at print time, and only the printer knows the second one.
    /// </summary>
    public FieldAnchor Anchor { get; set; } = FieldAnchor.TopLeft;

    public Orientation Orientation { get; set; } = Orientation.Normal;

    /// <summary>Draw order; lower values are emitted first (drawn underneath).</summary>
    public int ZOrder { get; set; }

    public bool IsVisible { get; set; } = true;

    /// <summary>Blocks canvas drag, resize and alignment. Panel edits still apply, so a
    /// lock protects finished layout from the mouse without making it read-only.</summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// Keep this element on the canvas but out of the print. For annotations, keep-out
    /// outlines, and work in progress.
    ///
    /// Distinct from <see cref="IsVisible"/>, which hides an element everywhere, and
    /// from the positional rule that skips anything parked off the label: this one is a
    /// deliberate choice the user made, so the canvas marks it differently and the
    /// warning line says so in different words.
    /// </summary>
    public bool DoNotPrint { get; set; }

    /// <summary>
    /// Print this field in reverse (^FR): wherever it lands on ink it knocks the ink out
    /// instead of adding to it, which is how a label puts white text in a black bar.
    ///
    /// This is the only colour the model has beyond black. It is a property of the field
    /// rather than of the element type, because ZPL applies ^FR to any field, and it
    /// depends on what is underneath: reversing over blank stock prints nothing at all.
    /// </summary>
    public bool IsReversed { get; set; }

    /// <summary>Double-dispatch entry point used by the ZPL generator and future
    /// visitors (export, validation). Keeps the model free of ZPL string-building.</summary>
    public abstract void Accept(IElementVisitor visitor);
}

/// <summary>Visitor over the concrete element types. The ZPL generator implements this.</summary>
public interface IElementVisitor
{
    void Visit(TextElement element);
    void Visit(BarcodeElement element);
    void Visit(QrCodeElement element);
    void Visit(DataMatrixElement element);
    void Visit(Pdf417Element element);
    void Visit(ImageElement element);
    void Visit(LineElement element);
    void Visit(BoxElement element);
    void Visit(EllipseElement element);
    void Visit(DiagonalLineElement element);
}
