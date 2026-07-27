namespace LabelForge.Core.Model;

/// <summary>How a text block's lines sit inside its width (the ^FB justification
/// argument). Only meaningful when <see cref="TextElement.BlockWidthDots"/> is set.</summary>
public enum TextJustification
{
    Left,
    Center,
    Right,

    /// <summary>Spread to both edges. The last line of a block is left aligned, which
    /// is what the printer does and what every typesetter expects.</summary>
    Justified,
}

/// <summary>A text field. v1 uses the scalable font 0 (^A0), sized in dots.</summary>
public sealed class TextElement : Element
{
    public string Text { get; set; } = string.Empty;

    /// <summary>Character height in dots.</summary>
    public int FontHeightDots { get; set; } = 30;

    /// <summary>Character width in dots. If 0, the printer derives it from the height.</summary>
    public int FontWidthDots { get; set; }

    /// <summary>
    /// Width of the text block in dots, or 0 for a plain single line that runs as far as
    /// it likes (no ^FB at all, which is how every label saved before this behaved).
    ///
    /// Setting it turns the field into a ZPL field block: the text wraps inside this
    /// width and aligns by <see cref="Justification"/>. Real labels use it mostly to
    /// centre one line inside a known width rather than to wrap paragraphs.
    /// </summary>
    public int BlockWidthDots { get; set; }

    /// <summary>Maximum lines the block prints. ZPL's own default is 1, and real labels
    /// rely on it, so it is kept faithfully rather than quietly raised: changing it would
    /// change what an imported label prints.</summary>
    public int BlockMaxLines { get; set; } = 1;

    /// <summary>Dots added between lines; negative closes them up.</summary>
    public int BlockLineSpacingDots { get; set; }

    /// <summary>Indent applied to every line after the first, in dots.</summary>
    public int BlockHangingIndentDots { get; set; }

    public TextJustification Justification { get; set; } = TextJustification.Left;

    /// <summary>True when this field is a block, i.e. when ^FB has to be emitted.</summary>
    public bool IsBlock => BlockWidthDots > 0;

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

/// <summary>A Data Matrix 2D code (^BX, ECC 200). Square, sized by the module size;
/// the symbol dimension grows with the data like a QR code.</summary>
public sealed class DataMatrixElement : Element
{
    public string Data { get; set; } = string.Empty;

    /// <summary>Module (cell) size in dots, 1-20.</summary>
    public int ModuleSizeDots { get; set; } = 4;

    public override void Accept(IElementVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// A PDF417 stacked barcode (^B7). Unlike QR and Data Matrix, whose shape follows from
/// the data alone, a PDF417 has a free parameter: how many data columns the codewords
/// are laid out in. That single number decides whether the symbol is a wide strip or a
/// tall block, so it is modelled explicitly rather than left to chance.
/// </summary>
public sealed class Pdf417Element : Element
{
    public string Data { get; set; } = string.Empty;

    /// <summary>Narrow-module width in dots, 1-10. Emitted as ^BY, the same
    /// format-level default the linear barcodes use.</summary>
    public int ModuleWidthDots { get; set; } = 2;

    /// <summary>Height of one row in dots. The symbol's total height is this times the
    /// row count, so it is the knob that makes a PDF417 taller.</summary>
    public int RowHeightDots { get; set; } = 8;

    /// <summary>
    /// Error-correction level, 0-8: the symbol carries 2^(level+1) correction codewords.
    ///
    /// The default is 2, not ZPL's own 0. Level 0 is error detection with no correction
    /// at all, which is not a state anyone wants a printed label in, and 2 is what the
    /// PDF417 standard recommends for payloads of the size labels actually carry.
    /// </summary>
    public int SecurityLevel { get; set; } = 2;

    /// <summary>
    /// Data columns, 1-30, or 0 to let the printer choose.
    ///
    /// Automatic is what ZPL does by default and is kept so a foreign label survives
    /// import unchanged, but it is not the default here: the printer's aspect-ratio
    /// heuristic is its own, so neither the offline preview nor the footprint math can
    /// promise the shape the printer will pick. A fixed column count is what makes the
    /// designer, the preview and the print agree.
    /// </summary>
    public int DataColumns { get; set; } = 5;

    /// <summary>Drop the right row indicator and shorten the stop pattern. Saves width
    /// on a controlled scanning setup; leave it off for general use.</summary>
    public bool Truncate { get; set; }

    public override void Accept(IElementVisitor visitor) => visitor.Visit(this);
}

/// <summary>How an image's grays become the printer's 1-bit black.</summary>
public enum DitherMode
{
    /// <summary>Plain 50% threshold; crisp for line art and logos.</summary>
    Threshold,

    /// <summary>Ordered (Bayer 8x8) pattern; stable texture for flat tints.</summary>
    Ordered,

    /// <summary>Floyd-Steinberg error diffusion; best for photos and gradients.</summary>
    FloydSteinberg,
}

/// <summary>An image placed on the label, emitted as an inline ^GF graphic field.
/// The original file bytes are kept in the document (base64 in the .lfl) so the
/// label stays self-contained; conversion to 1-bit happens at generation time.</summary>
public sealed class ImageElement : Element
{
    /// <summary>The original encoded image (PNG, JPEG, BMP...).</summary>
    public byte[] ImageData { get; set; } = [];

    /// <summary>Pixel size of the source image, captured at import so bounds and
    /// aspect-ratio math never need to decode the image.</summary>
    public int SourcePixelWidth { get; set; }

    public int SourcePixelHeight { get; set; }

    /// <summary>Rendered size on the label in dots.</summary>
    public int WidthDots { get; set; } = 200;

    public int HeightDots { get; set; } = 200;

    public DitherMode Dithering { get; set; } = DitherMode.FloydSteinberg;

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

    /// <summary>
    /// Draw in white rather than black (^GB's colour parameter). On monochrome stock
    /// that means erase: the shape paints white over whatever was drawn before it, which
    /// is how real labels clear an area before dropping a graphic into it.
    ///
    /// Not the same thing as <see cref="Element.IsReversed"/>, and the difference shows
    /// on blank stock: ^FR inverts the ink it lands on, so over white it prints black,
    /// while white paints white and so prints nothing at all. Only the graphic primitives
    /// take a colour, which is why this sits here and on <see cref="BoxElement"/> rather
    /// than on <see cref="Element"/>.
    /// </summary>
    public bool IsWhite { get; set; }

    public override void Accept(IElementVisitor visitor) => visitor.Visit(this);
}

/// <summary>A rectangle outline (^GB). Thickness is the border width in dots.</summary>
public sealed class BoxElement : Element
{
    public int WidthDots { get; set; } = 100;

    public int HeightDots { get; set; } = 100;

    /// <summary>Border thickness in dots.</summary>
    public int ThicknessDots { get; set; } = 2;

    /// <inheritdoc cref="LineElement.IsWhite"/>
    public bool IsWhite { get; set; }

    public override void Accept(IElementVisitor visitor) => visitor.Visit(this);
}
