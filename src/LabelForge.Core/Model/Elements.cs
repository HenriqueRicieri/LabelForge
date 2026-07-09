namespace LabelForge.Core.Model;

/// <summary>A text field. v1 uses the scalable font 0 (^A0), sized in dots.</summary>
public sealed class TextElement : Element
{
    public string Text { get; set; } = string.Empty;

    /// <summary>Character height in dots.</summary>
    public int FontHeightDots { get; set; } = 30;

    /// <summary>Character width in dots. If 0, the printer derives it from the height.</summary>
    public int FontWidthDots { get; set; }

    public override void Accept(IElementVisitor visitor) => visitor.Visit(this);
}

/// <summary>Linear (1D) barcode symbologies supported in v1.</summary>
public enum BarcodeSymbology
{
    Code128,
    Code39,
    Ean13,
    UpcA,
}

/// <summary>A linear barcode. Width is derived by the symbology from the data and the
/// module width, so it is never freely resized on the canvas.</summary>
public sealed class BarcodeElement : Element
{
    public BarcodeSymbology Symbology { get; set; } = BarcodeSymbology.Code128;

    public string Data { get; set; } = string.Empty;

    /// <summary>Bar height in dots.</summary>
    public int HeightDots { get; set; } = 100;

    /// <summary>Narrow-bar (module) width in dots, 1-10 (^BY).</summary>
    public int ModuleWidthDots { get; set; } = 2;

    /// <summary>Wide-to-narrow bar ratio for symbologies that use it (e.g. Code 39).</summary>
    public double WideBarRatio { get; set; } = 3.0;

    /// <summary>Whether to print the human-readable interpretation line.</summary>
    public bool PrintInterpretationLine { get; set; } = true;

    public override void Accept(IElementVisitor visitor) => visitor.Visit(this);
}

/// <summary>QR code error-correction level.</summary>
public enum QrErrorCorrection
{
    /// <summary>~7% recovery.</summary>
    Low,

    /// <summary>~15% recovery.</summary>
    Medium,

    /// <summary>~25% recovery.</summary>
    Quartile,

    /// <summary>~30% recovery.</summary>
    High,
}

/// <summary>A QR code (^BQ, model 2).</summary>
public sealed class QrCodeElement : Element
{
    public string Data { get; set; } = string.Empty;

    /// <summary>Module magnification factor, 1-10.</summary>
    public int Magnification { get; set; } = 5;

    public QrErrorCorrection ErrorCorrection { get; set; } = QrErrorCorrection.Medium;

    public override void Accept(IElementVisitor visitor) => visitor.Visit(this);
}

/// <summary>A straight line, drawn as a solid ^GB bar so orientation is unambiguous.</summary>
public sealed class LineElement : Element
{
    public bool IsVertical { get; set; }

    /// <summary>Length of the line in dots.</summary>
    public int LengthDots { get; set; } = 100;

    /// <summary>Line thickness in dots.</summary>
    public int ThicknessDots { get; set; } = 2;

    public override void Accept(IElementVisitor visitor) => visitor.Visit(this);
}

/// <summary>A rectangle outline (^GB). Thickness is the border width in dots.</summary>
public sealed class BoxElement : Element
{
    public int WidthDots { get; set; } = 100;

    public int HeightDots { get; set; } = 100;

    /// <summary>Border thickness in dots.</summary>
    public int ThicknessDots { get; set; } = 2;

    public override void Accept(IElementVisitor visitor) => visitor.Visit(this);
}
