namespace LabelForge.Core.Model;

/// <summary>
/// Base type for everything placed on a label. Geometry is stored in printer dots
/// with a top-left origin, matching ZPL's ^FO field origin, so what you place is what
/// prints with no generation-time rounding.
/// </summary>
public abstract class Element
{
    /// <summary>Stable identity for selection, undo, and serialization.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

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

    public bool IsLocked { get; set; }

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
    void Visit(LineElement element);
    void Visit(BoxElement element);
}
