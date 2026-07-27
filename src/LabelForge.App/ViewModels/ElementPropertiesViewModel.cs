using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelForge.Core.Model;

namespace LabelForge.App.ViewModels;

/// <summary>A friendly label for a ZPL field orientation.</summary>
public sealed record OrientationOption(string Label, Orientation Value)
{
    public static IReadOnlyList<OrientationOption> All { get; } =
    [
        new("0°", Orientation.Normal),
        new("90°", Orientation.Rotated90),
        new("180°", Orientation.Rotated180),
        new("270°", Orientation.Rotated270),
    ];

    public override string ToString() => Label;
}

/// <summary>A friendly label for which point of a field its origin names.</summary>
public sealed record AnchorOption(FieldAnchor Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One of the printer's built-in fonts, named the way someone picking one would
/// recognise it rather than by its ZPL letter alone.</summary>
public sealed record FontOption(char Value, string Label)
{
    public static IReadOnlyList<FontOption> All { get; } =
    [
        new('0', "0 - scalable (default)"),
        new('A', "A - 9 x 5, smallest"),
        new('B', "B - 11 x 7, capitals only"),
        new('C', "C - 18 x 10"),
        new('D', "D - 18 x 10"),
        new('E', "E - OCR-B"),
        new('F', "F - 26 x 13"),
        new('G', "G - 60 x 40, largest"),
        new('H', "H - OCR-A"),
    ];

    public override string ToString() => Label;
}

/// <summary>
/// Base of the per-type property editors shown in the designer's panel. Each editor
/// wraps the selected element directly (getters read the model, setters write it) and
/// signals every committed change through the edited callback, which records an undo
/// snapshot and re-renders. The concrete subclass is picked by DataTemplate.
/// </summary>
public abstract class ElementPropertiesViewModel : ObservableObject
{
    private readonly Action<string> _edited;
    private readonly LabelDocument _document;

    /// <summary>The unit choice is sticky: it survives selection changes within the
    /// session, so it lives here rather than on each per-selection editor instance.</summary>
    private static bool _useMmDefault;

    private bool _useMm;

    protected ElementPropertiesViewModel(Element element, LabelDocument document, Action<string> edited)
    {
        Element = element;
        _document = document;
        _edited = edited;
        _useMm = _useMmDefault;
    }

    protected Element Element { get; }

    /// <summary>The document being edited, for the few editors that need something the
    /// element alone cannot answer (the density for millimetres, the marker syntax).</summary>
    protected LabelDocument Document => _document;

    /// <summary>Supplies the markers the data boxes complete from. Assigned by the
    /// designer when it builds the editor, and read through rather than copied, so
    /// importing a catalog while an element is selected takes effect at once.</summary>
    internal Func<IReadOnlyList<string>>? SuggestionSource { get; set; }

    /// <summary>Ready-to-paste markers from the label's field catalog; empty when it has
    /// none, which leaves the data boxes behaving as plain text boxes.</summary>
    public IReadOnlyList<string> FieldSuggestions => SuggestionSource?.Invoke() ?? [];

    public abstract string TypeName { get; }

    public IReadOnlyList<OrientationOption> Orientations => OrientationOption.All;

    /// <summary>When set, X and Y display and accept millimeters (converted through
    /// the document density); the model always stays in dots.</summary>
    public bool UseMm
    {
        get => _useMm;
        set
        {
            if (_useMm == value)
            {
                return;
            }

            _useMm = value;
            _useMmDefault = value;
            OnPropertyChanged(string.Empty);
        }
    }

    /// <summary>A name of the user's own, shown in the element list. Optional: without
    /// one the list falls back to the type and a glimpse of the content, which is enough
    /// to tell most elements apart and means nothing has to be named to be findable.</summary>
    public string Name
    {
        get => Element.Name;
        set => Edit(Element.Name, (value ?? string.Empty).Trim(), v => Element.Name = v);
    }

    public string UnitSuffix => UseMm ? "mm" : "dots";

    public string PositionFormat => UseMm ? "0.##" : "0";

    public decimal X
    {
        get => UseMm ? (decimal)Math.Round(Units.DotsToMm(Element.X, _document.Dpmm), 2) : Element.X;
        set => Edit(Element.X, ToDots(value), v => Element.X = v);
    }

    public decimal Y
    {
        get => UseMm ? (decimal)Math.Round(Units.DotsToMm(Element.Y, _document.Dpmm), 2) : Element.Y;
        set => Edit(Element.Y, ToDots(value), v => Element.Y = v);
    }

    private int ToDots(decimal value) =>
        UseMm ? Units.MmToDots((double)value, _document.Dpmm) : (int)value;

    /// <summary>Whether to offer a rotation at all. ZPL's graphic primitives take no
    /// orientation argument, so the control would be one that cannot do anything; see
    /// <see cref="FieldRotation"/>.</summary>
    public bool CanRotate => FieldRotation.Applies(Element);

    public OrientationOption SelectedOrientation
    {
        get => Orientations.First(o => o.Value == Element.Orientation);
        set => Edit(Element.Orientation, value?.Value ?? Orientation.Normal, v => Element.Orientation = v);
    }

    public IReadOnlyList<AnchorOption> Anchors { get; } =
    [
        new(FieldAnchor.TopLeft, "Top-left corner (^FO)"),
        new(FieldAnchor.Baseline, "Baseline / bottom-left (^FT)"),
    ];

    /// <summary>Which point of the field X and Y name, and so which command places it.
    /// Worth having on the panel rather than only on import, because the two behave
    /// differently once the content changes width: a baseline-placed field at 180 or 270
    /// degrees grows away from its anchor, which is how a label keeps a value's right
    /// edge fixed when the value itself is filled in elsewhere.</summary>
    public AnchorOption SelectedAnchor
    {
        get => Anchors.First(a => a.Value == Element.Anchor);
        set => Edit(Element.Anchor, value?.Value ?? FieldAnchor.TopLeft, v =>
        {
            Element.Anchor = v;
            OnPropertyChanged(nameof(AnchorHint));
        });
    }

    public string AnchorHint => Element.Anchor == FieldAnchor.Baseline
        ? "X and Y are the field's bottom-left; text sits on that line."
        : "X and Y are the top-left corner of the field.";

    /// <summary>Knocks this field out of whatever ink is under it (^FR) instead of
    /// adding to it. Applies to any element, which is why it lives on the base editor;
    /// over blank stock it prints nothing, which is the command working as intended.</summary>
    public bool IsReversed
    {
        get => Element.IsReversed;
        set => Edit(Element.IsReversed, value, v => Element.IsReversed = v);
    }

    /// <summary>Blocks canvas drag, resize and alignment for this element. These editors
    /// keep working, so a lock guards finished layout from the mouse without making the
    /// element read-only.</summary>
    public bool IsLocked
    {
        get => Element.IsLocked;
        set => Edit(Element.IsLocked, value, v => Element.IsLocked = v);
    }

    /// <summary>Keeps the element on the canvas and out of the print.</summary>
    public bool DoNotPrint
    {
        get => Element.DoNotPrint;
        set => Edit(Element.DoNotPrint, value, v => Element.DoNotPrint = v);
    }

    /// <summary>Re-reads every property from the model (e.g. after a canvas drag).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    /// <summary>Applies a change only when the value really differs, then notifies and
    /// reports the edit so the owner records undo and re-renders.</summary>
    protected void Edit<T>(T current, T next, Action<T> apply,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, next))
        {
            return;
        }

        apply(next);
        OnPropertyChanged(propertyName);
        _edited(propertyName ?? string.Empty);
    }
}

public sealed class TextPropertiesViewModel : ElementPropertiesViewModel
{
    private readonly TextElement _text;

    public TextPropertiesViewModel(TextElement element, LabelDocument document, Action<string> edited)
        : base(element, document, edited) => _text = element;

    public override string TypeName => "Text";

    public string Text
    {
        get => _text.Text;
        set => Edit(_text.Text, value ?? string.Empty, v => _text.Text = v);
    }

    public IReadOnlyList<FontOption> Fonts { get; } = FontOption.All;

    /// <summary>Which of the printer's built-in fonts draws this field.</summary>
    public FontOption SelectedFont
    {
        get => Fonts.FirstOrDefault(f => f.Value == char.ToUpperInvariant(_text.Font)) ?? Fonts[0];
        set => Edit(_text.Font, value?.Value ?? ZplFont.Scalable, v =>
        {
            _text.Font = v;

            // A bitmapped font only prints whole multiples of its cell, so landing on one
            // is not a preference: an in-between size is a size the printer will not use.
            if (ZplFont.Cell(v, Document.Dpmm) is { } cell)
            {
                int magnification = ZplFont.Magnification(
                    v, _text.FontHeightDots, vertical: true, Document.Dpmm);
                _text.FontHeightDots = cell.HeightDots * magnification;
                _text.FontWidthDots = cell.WidthDots * magnification;
            }

            OnPropertyChanged(nameof(FontHeight));
            OnPropertyChanged(nameof(FontWidth));
            OnPropertyChanged(nameof(IsScalableFont));
            OnPropertyChanged(nameof(Magnification));
            OnPropertyChanged(nameof(FontNote));
        });
    }

    /// <summary>Drives which size editor the panel shows: free dots for the scalable
    /// font, whole multiples of the cell for a bitmapped one.</summary>
    public bool IsScalableFont => ZplFont.IsScalable(char.ToUpperInvariant(_text.Font));

    /// <summary>The bitmapped size, as the multiple of the cell the printer will use.</summary>
    public decimal Magnification
    {
        get => ZplFont.Magnification(_text.Font, _text.FontHeightDots, vertical: true, Document.Dpmm);
        set
        {
            if (ZplFont.Cell(_text.Font, Document.Dpmm) is not { } cell)
            {
                return;
            }

            int times = Math.Clamp((int)value, 1, ZplFont.MaxMagnification);
            Edit(_text.FontHeightDots, cell.HeightDots * times, v =>
            {
                _text.FontHeightDots = v;
                _text.FontWidthDots = cell.WidthDots * times;
                OnPropertyChanged(nameof(FontHeight));
                OnPropertyChanged(nameof(FontWidth));
                OnPropertyChanged(nameof(FontNote));
            });
        }
    }

    /// <summary>What the panel says under the font picker. The renderer half is not a
    /// disclaimer for its own sake: the canvas is that render, and a designer that let it
    /// pretend would be worse than one that says which fonts it can show truly.</summary>
    public string FontNote
    {
        get
        {
            char font = char.ToUpperInvariant(_text.Font);
            if (ZplFont.Cell(font, Document.Dpmm) is not { } cell)
            {
                return "Scalable: any height and width in dots.";
            }

            string size = $"Fixed pitch, {cell.HeightDots} x {cell.WidthDots} dots per "
                          + $"character at 1x, up to {ZplFont.MaxMagnification}x.";
            return ZplFont.RendersFaithfully(font)
                ? size
                : size + " The preview draws this font at a slightly different width than"
                       + " it prints; the ZPL is correct.";
        }
    }

    public decimal FontHeight
    {
        get => _text.FontHeightDots;
        set => Edit(_text.FontHeightDots, Math.Max((int)value, 6), v => _text.FontHeightDots = v);
    }

    /// <summary>0 lets the printer derive the width from the height.</summary>
    public decimal FontWidth
    {
        get => _text.FontWidthDots;
        set => Edit(_text.FontWidthDots, Math.Max((int)value, 0), v => _text.FontWidthDots = v);
    }

    /// <summary>0 keeps the field a plain single line and emits no ^FB at all.</summary>
    public decimal BlockWidth
    {
        get => _text.BlockWidthDots;
        set => Edit(_text.BlockWidthDots, Math.Max((int)value, 0), v =>
        {
            _text.BlockWidthDots = v;
            OnPropertyChanged(nameof(IsBlock));
        });
    }

    /// <summary>Drives whether the rest of the block editors are enabled: without a
    /// width there is no block for them to describe.</summary>
    public bool IsBlock => _text.IsBlock;

    public decimal BlockMaxLines
    {
        get => _text.BlockMaxLines;
        set => Edit(_text.BlockMaxLines, Math.Clamp((int)value, 1, 9999), v => _text.BlockMaxLines = v);
    }

    public decimal BlockLineSpacing
    {
        get => _text.BlockLineSpacingDots;
        set => Edit(_text.BlockLineSpacingDots, (int)value, v => _text.BlockLineSpacingDots = v);
    }

    public decimal BlockHangingIndent
    {
        get => _text.BlockHangingIndentDots;
        set => Edit(_text.BlockHangingIndentDots, Math.Max((int)value, 0), v => _text.BlockHangingIndentDots = v);
    }

    public IReadOnlyList<TextJustification> Justifications { get; } =
        Enum.GetValues<TextJustification>();

    public TextJustification Justification
    {
        get => _text.Justification;
        set => Edit(_text.Justification, value, v => _text.Justification = v);
    }
}

public sealed class BarcodePropertiesViewModel : ElementPropertiesViewModel
{
    private readonly BarcodeElement _barcode;

    public BarcodePropertiesViewModel(BarcodeElement element, LabelDocument document, Action<string> edited)
        : base(element, document, edited) => _barcode = element;

    public override string TypeName => "Barcode";

    public IReadOnlyList<BarcodeSymbology> Symbologies { get; } = Enum.GetValues<BarcodeSymbology>();

    public BarcodeSymbology Symbology
    {
        get => _barcode.Symbology;
        set
        {
            Edit(_barcode.Symbology, value, v => _barcode.Symbology = v);
            OnPropertyChanged(nameof(IsCode39));
            OnPropertyChanged(nameof(Warning));
            OnPropertyChanged(nameof(HasWarning));
            NotifyGs1Changed();
        }
    }

    public bool IsCode39 => _barcode.Symbology == BarcodeSymbology.Code39;

    public string Data
    {
        get => _barcode.Data;
        set
        {
            Edit(_barcode.Data, value ?? string.Empty, v => _barcode.Data = v);
            OnPropertyChanged(nameof(Warning));
            OnPropertyChanged(nameof(HasWarning));
            NotifyGs1Changed();
        }
    }

    /// <summary>A design-time message when the data cannot be encoded, else empty.</summary>
    public string Warning => Core.Zpl.BarcodeValidator.Validate(
        _barcode.Symbology, _barcode.Data, Document.Markers) ?? string.Empty;

    public bool HasWarning => Warning.Length > 0;

    public decimal Height
    {
        get => _barcode.HeightDots;
        set => Edit(_barcode.HeightDots, Math.Max((int)value, 10), v => _barcode.HeightDots = v);
    }

    public decimal ModuleWidth
    {
        get => _barcode.ModuleWidthDots;
        set => Edit(_barcode.ModuleWidthDots, Math.Clamp((int)value, 1, 10), v => _barcode.ModuleWidthDots = v);
    }

    /// <summary>Wide-to-narrow ratio; only Code 39 uses it.</summary>
    public decimal Ratio
    {
        get => (decimal)_barcode.WideBarRatio;
        set => Edit(_barcode.WideBarRatio, Math.Clamp((double)value, 2.0, 3.0), v => _barcode.WideBarRatio = v);
    }

    public bool Interpretation
    {
        get => _barcode.PrintInterpretationLine;
        set => Edit(_barcode.PrintInterpretationLine, value, v => _barcode.PrintInterpretationLine = v);
    }

    /// <summary>
    /// True when the data is a GS1-128 payload rather than plain content, which is what
    /// the opening FNC1 says. Only Code 128 carries one.
    /// </summary>
    public bool IsGs1 =>
        _barcode.Symbology == BarcodeSymbology.Code128 &&
        Core.Zpl.Gs1Payload.IsGs1(_barcode.Data);

    /// <summary>
    /// The payload broken into its application identifiers.
    ///
    /// Worth showing even when something upstream assembled the data, because that is
    /// exactly when nobody has read it: a run of digits with no separators in it is not
    /// something a person can check by eye, and the bracketed form is.
    /// </summary>
    public string Gs1Breakdown => IsGs1
        ? string.Join(" ", Core.Zpl.Gs1Payload.Read(_barcode.Data).Fields
            .Select(f => $"({f.Code}){f.Value}"))
        : string.Empty;

    /// <summary>
    /// What is structurally wrong with the payload.
    ///
    /// The one that matters is a variable-length value with nothing after it to say where
    /// it stops, because that does not fail: the scanner reads it and whatever follows as
    /// one value, and the label looks perfect while carrying the wrong data.
    /// </summary>
    public string Gs1Warning => IsGs1
        ? string.Join(" ", Core.Zpl.Gs1Payload.Read(_barcode.Data).Problems)
        : string.Empty;

    public bool HasGs1Warning => Gs1Warning.Length > 0;

    private void NotifyGs1Changed()
    {
        OnPropertyChanged(nameof(IsGs1));
        OnPropertyChanged(nameof(Gs1Breakdown));
        OnPropertyChanged(nameof(Gs1Warning));
        OnPropertyChanged(nameof(HasGs1Warning));
    }
}

public sealed class QrPropertiesViewModel : ElementPropertiesViewModel
{
    private readonly QrCodeElement _qr;

    public QrPropertiesViewModel(QrCodeElement element, LabelDocument document, Action<string> edited)
        : base(element, document, edited) => _qr = element;

    public override string TypeName => "QR Code";

    public IReadOnlyList<QrErrorCorrection> ErrorCorrections { get; } = Enum.GetValues<QrErrorCorrection>();

    public string Data
    {
        get => _qr.Data;
        set => Edit(_qr.Data, value ?? string.Empty, v => _qr.Data = v);
    }

    public decimal Magnification
    {
        get => _qr.Magnification;
        set => Edit(_qr.Magnification, Math.Clamp((int)value, 1, 10), v => _qr.Magnification = v);
    }

    public QrErrorCorrection ErrorCorrection
    {
        get => _qr.ErrorCorrection;
        set => Edit(_qr.ErrorCorrection, value, v => _qr.ErrorCorrection = v);
    }
}

public sealed class DataMatrixPropertiesViewModel : ElementPropertiesViewModel
{
    private readonly DataMatrixElement _dm;

    public DataMatrixPropertiesViewModel(DataMatrixElement element, LabelDocument document, Action<string> edited)
        : base(element, document, edited) => _dm = element;

    public override string TypeName => "Data Matrix";

    public string Data
    {
        get => _dm.Data;
        set => Edit(_dm.Data, value ?? string.Empty, v => _dm.Data = v);
    }

    public decimal ModuleSize
    {
        get => _dm.ModuleSizeDots;
        set => Edit(_dm.ModuleSizeDots, Math.Clamp((int)value, 1, 20), v => _dm.ModuleSizeDots = v);
    }
}

public sealed class Pdf417PropertiesViewModel : ElementPropertiesViewModel
{
    private readonly Pdf417Element _pdf;

    public Pdf417PropertiesViewModel(Pdf417Element element, LabelDocument document, Action<string> edited)
        : base(element, document, edited) => _pdf = element;

    public override string TypeName => "PDF417";

    public string Data
    {
        get => _pdf.Data;
        set
        {
            Edit(_pdf.Data, value ?? string.Empty, v => _pdf.Data = v);
            OnShapeChanged();
        }
    }

    public decimal ModuleWidth
    {
        get => _pdf.ModuleWidthDots;
        set
        {
            Edit(_pdf.ModuleWidthDots, Math.Clamp((int)value, 1, 10), v => _pdf.ModuleWidthDots = v);
            OnShapeChanged();
        }
    }

    public decimal RowHeight
    {
        get => _pdf.RowHeightDots;
        set
        {
            Edit(_pdf.RowHeightDots, Math.Max((int)value, 1), v => _pdf.RowHeightDots = v);
            OnShapeChanged();
        }
    }

    public decimal SecurityLevel
    {
        get => _pdf.SecurityLevel;
        set
        {
            Edit(_pdf.SecurityLevel, Math.Clamp((int)value, 0, 8), v => _pdf.SecurityLevel = v);
            OnShapeChanged();
        }
    }

    /// <summary>Data columns, with 0 shown as "Automatic" by the view's minimum-zero
    /// spinner. Kept as a number rather than a checkbox plus a number, because that is
    /// what the ZPL parameter is.</summary>
    public decimal Columns
    {
        get => _pdf.DataColumns;
        set
        {
            Edit(
                _pdf.DataColumns,
                Math.Clamp((int)value, 0, Pdf417Metrics.MaxColumns),
                v => _pdf.DataColumns = v);
            OnShapeChanged();
        }
    }

    public bool Truncate
    {
        get => _pdf.Truncate;
        set
        {
            Edit(_pdf.Truncate, value, v => _pdf.Truncate = v);
            OnShapeChanged();
        }
    }

    /// <summary>The shape the symbol is expected to take, so the numbers above are not
    /// the only feedback for a parameter whose effect is a whole layout.</summary>
    public string ShapeInfo
    {
        get
        {
            Pdf417Shape shape = Pdf417Metrics.Measure(_pdf);
            string columns = Count(shape.Columns, "column");
            return (shape.ColumnsAreAutomatic ? $"about {columns}" : columns)
                   + $" x {Count(shape.Rows, "row")}, "
                   + $"about {shape.WidthDots} x {shape.HeightDots} dots";
        }
    }

    /// <summary>
    /// What the preview cannot promise. Automatic columns are the printer's own
    /// aspect-ratio choice, so the offline render is one plausible shape rather than
    /// the shape; over capacity, there is no symbol at all to draw.
    /// </summary>
    public string Warning
    {
        get
        {
            Pdf417Shape shape = Pdf417Metrics.Measure(_pdf);
            if (shape.OverCapacity)
            {
                return "This is more data than a PDF417 holds at this security level. "
                       + "Shorten the data, add columns, or lower the security level.";
            }

            return shape.ColumnsAreAutomatic
                ? "With columns set to automatic the printer chooses the symbol's shape, "
                  + "so the preview shows one plausible layout rather than the one that "
                  + "will print. Set a column count to pin it."
                : string.Empty;
        }
    }

    public bool HasWarning => Warning.Length > 0;

    private static string Count(int value, string noun) =>
        value == 1 ? $"1 {noun}" : $"{value} {noun}s";

    private void OnShapeChanged()
    {
        OnPropertyChanged(nameof(ShapeInfo));
        OnPropertyChanged(nameof(Warning));
        OnPropertyChanged(nameof(HasWarning));
    }
}

public sealed class ImagePropertiesViewModel : ElementPropertiesViewModel
{
    private readonly ImageElement _image;

    public ImagePropertiesViewModel(ImageElement element, LabelDocument document, Action<string> edited)
        : base(element, document, edited)
    {
        _image = element;
        RestoreAspectCommand = new RelayCommand(RestoreAspect);
    }

    public override string TypeName => "Image";

    public IRelayCommand RestoreAspectCommand { get; }

    public string SourceInfo => _image.SourcePixelWidth > 0
        ? $"Source: {_image.SourcePixelWidth} x {_image.SourcePixelHeight} px"
        : "Source size unknown";

    public IReadOnlyList<DitherMode> DitherModes { get; } = Enum.GetValues<DitherMode>();

    public DitherMode Dithering
    {
        get => _image.Dithering;
        set => Edit(_image.Dithering, value, v => _image.Dithering = v);
    }

    public decimal ImageWidth
    {
        get => _image.WidthDots;
        set => Edit(_image.WidthDots, Math.Max((int)value, 8), v => _image.WidthDots = v);
    }

    public decimal ImageHeight
    {
        get => _image.HeightDots;
        set => Edit(_image.HeightDots, Math.Max((int)value, 8), v => _image.HeightDots = v);
    }

    /// <summary>Re-derives the height from the width using the source pixel aspect.</summary>
    private void RestoreAspect()
    {
        if (_image.SourcePixelWidth <= 0 || _image.SourcePixelHeight <= 0)
        {
            return;
        }

        int height = Math.Max((int)Math.Round(
            (double)_image.WidthDots * _image.SourcePixelHeight / _image.SourcePixelWidth), 8);
        Edit(_image.HeightDots, height, v => _image.HeightDots = v, nameof(ImageHeight));
    }
}

public sealed class LinePropertiesViewModel : ElementPropertiesViewModel
{
    private readonly LineElement _line;

    public LinePropertiesViewModel(LineElement element, LabelDocument document, Action<string> edited)
        : base(element, document, edited) => _line = element;

    public override string TypeName => "Line";

    public decimal Length
    {
        get => _line.LengthDots;
        set => Edit(_line.LengthDots, Math.Max((int)value, 1), v => _line.LengthDots = v);
    }

    public decimal Thickness
    {
        get => _line.ThicknessDots;
        set => Edit(_line.ThicknessDots, Math.Max((int)value, 1), v => _line.ThicknessDots = v);
    }

    public bool IsVertical
    {
        get => _line.IsVertical;
        set => Edit(_line.IsVertical, value, v => _line.IsVertical = v);
    }

    /// <summary>Draws the bar in white, which on monochrome stock erases what is under
    /// it. Invisible over blank stock, because that is exactly what it prints.</summary>
    public bool IsWhite
    {
        get => _line.IsWhite;
        set => Edit(_line.IsWhite, value, v => _line.IsWhite = v);
    }
}

public sealed class BoxPropertiesViewModel : ElementPropertiesViewModel
{
    private readonly BoxElement _box;

    public BoxPropertiesViewModel(BoxElement element, LabelDocument document, Action<string> edited)
        : base(element, document, edited) => _box = element;

    public override string TypeName => "Box";

    public decimal BoxWidth
    {
        get => _box.WidthDots;
        set => Edit(_box.WidthDots, Math.Max((int)value, 4), v => _box.WidthDots = v);
    }

    public decimal BoxHeight
    {
        get => _box.HeightDots;
        set => Edit(_box.HeightDots, Math.Max((int)value, 4), v => _box.HeightDots = v);
    }

    public decimal Thickness
    {
        get => _box.ThicknessDots;
        set => Edit(_box.ThicknessDots, Math.Max((int)value, 1), v => _box.ThicknessDots = v);
    }

    /// <inheritdoc cref="LinePropertiesViewModel.IsWhite"/>
    public bool IsWhite
    {
        get => _box.IsWhite;
        set => Edit(_box.IsWhite, value, v => _box.IsWhite = v);
    }

    /// <summary>^GB's rounding index, 0 to 8. An index rather than a radius because that
    /// is what the command takes, and because it keeps its proportion as the box is
    /// resized: the radius is the index eighths of half the shorter side.</summary>
    public decimal CornerRoundness
    {
        get => _box.CornerRoundness;
        set => Edit(
            _box.CornerRoundness, Math.Clamp((int)value, 0, 8), v => _box.CornerRoundness = v);
    }
}

public sealed partial class EllipsePropertiesViewModel : ElementPropertiesViewModel
{
    private readonly EllipseElement _ellipse;

    public EllipsePropertiesViewModel(
        EllipseElement element, LabelDocument document, Action<string> edited)
        : base(element, document, edited) => _ellipse = element;

    public override string TypeName => IsCircle ? "Circle" : "Ellipse";

    public decimal EllipseWidth
    {
        get => _ellipse.WidthDots;
        set
        {
            Edit(_ellipse.WidthDots, Clamp(value), v => _ellipse.WidthDots = v);
            NotifyShape();
        }
    }

    public decimal EllipseHeight
    {
        get => _ellipse.HeightDots;
        set
        {
            Edit(_ellipse.HeightDots, Clamp(value), v => _ellipse.HeightDots = v);
            NotifyShape();
        }
    }

    public decimal Thickness
    {
        get => _ellipse.ThicknessDots;
        set => Edit(_ellipse.ThicknessDots, Math.Max((int)value, 1), v => _ellipse.ThicknessDots = v);
    }

    /// <summary>True when the two sides match, which is all a circle is: ZPL's own ^GC
    /// draws exactly this shape and nothing more.</summary>
    public bool IsCircle => _ellipse.WidthDots == _ellipse.HeightDots;

    /// <inheritdoc cref="LinePropertiesViewModel.IsWhite"/>
    public bool IsWhite
    {
        get => _ellipse.IsWhite;
        set
        {
            Edit(_ellipse.IsWhite, value, v => _ellipse.IsWhite = v);
            OnPropertyChanged(nameof(WhiteNote));
            OnPropertyChanged(nameof(HasWhiteNote));
        }
    }

    /// <summary>The one thing on this panel the canvas cannot show. Measured rather than
    /// assumed: the offline renderer draws a white ^GE as nothing at any thickness, while
    /// it honours white on ^GB, ^GD and even ^GC drawing the very same circle. A printer
    /// erases as asked, so the panel says so instead of the canvas pretending either way.</summary>
    public string WhiteNote => IsWhite
        ? "A white ellipse prints (it clears what is under it), but the preview cannot "
          + "show it: the offline renderer does not draw ^GE in white."
        : string.Empty;

    public bool HasWhiteNote => IsWhite;

    /// <summary>Makes the height match the width, since dragging two boxes to the same
    /// number by hand is the fiddliest way to ask for a circle.</summary>
    [RelayCommand]
    private void MakeCircle() => EllipseHeight = _ellipse.WidthDots;

    private void NotifyShape()
    {
        OnPropertyChanged(nameof(IsCircle));
        OnPropertyChanged(nameof(TypeName));
    }

    private static int Clamp(decimal value) => Math.Clamp(
        (int)value, ElementResizer.MinShapeSideDots, ElementResizer.MaxEllipseSideDots);
}

public sealed class DiagonalPropertiesViewModel : ElementPropertiesViewModel
{
    private readonly DiagonalLineElement _diagonal;

    public DiagonalPropertiesViewModel(
        DiagonalLineElement element, LabelDocument document, Action<string> edited)
        : base(element, document, edited) => _diagonal = element;

    public override string TypeName => "Diagonal line";

    public decimal DiagonalWidth
    {
        get => _diagonal.WidthDots;
        set => Edit(
            _diagonal.WidthDots,
            Math.Max((int)value, ElementResizer.MinShapeSideDots),
            v => _diagonal.WidthDots = v);
    }

    public decimal DiagonalHeight
    {
        get => _diagonal.HeightDots;
        set => Edit(
            _diagonal.HeightDots,
            Math.Max((int)value, ElementResizer.MinShapeSideDots),
            v => _diagonal.HeightDots = v);
    }

    public decimal Thickness
    {
        get => _diagonal.ThicknessDots;
        set
        {
            Edit(_diagonal.ThicknessDots, Math.Max((int)value, 1), v => _diagonal.ThicknessDots = v);
            OnPropertyChanged(nameof(ThicknessNote));
            OnPropertyChanged(nameof(HasThicknessNote));
        }
    }

    /// <summary>True for ^GD's "R": bottom-left up to top-right.</summary>
    public bool LeansRight
    {
        get => _diagonal.LeansRight;
        set => Edit(_diagonal.LeansRight, value, v => _diagonal.LeansRight = v);
    }

    /// <inheritdoc cref="LinePropertiesViewModel.IsWhite"/>
    public bool IsWhite
    {
        get => _diagonal.IsWhite;
        set => Edit(_diagonal.IsWhite, value, v => _diagonal.IsWhite = v);
    }

    /// <summary>A one-dot diagonal prints and the preview draws nothing for it, measured.
    /// Worth saying rather than clamping: the label is right and the canvas is not.</summary>
    public string ThicknessNote => _diagonal.ThicknessDots < 2
        ? "At one dot this line prints but the preview cannot draw it. Two or more shows "
          + "on the canvas."
        : string.Empty;

    public bool HasThicknessNote => _diagonal.ThicknessDots < 2;
}
