using System.Text.Json.Serialization;

namespace LabelForge.Core.Model;

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
[JsonDerivedType(typeof(ImageElement), "image")]
[JsonDerivedType(typeof(LineElement), "line")]
[JsonDerivedType(typeof(BoxElement), "box")]
public abstract class Element
{
    /// <summary>Stable identity for selection, undo, and serialization. Settable so
    /// clipboard paste can assign a fresh identity to a deserialized copy.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-facing name shown in the layers/objects panel.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>X of the field origin in dots (top-left).</summary>
    public int X { get; set; }

    /// <summary>Y of the field origin in dots (top-left).</summary>
    public int Y { get; set; }

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
    void Visit(ImageElement element);
    void Visit(LineElement element);
    void Visit(BoxElement element);
}
