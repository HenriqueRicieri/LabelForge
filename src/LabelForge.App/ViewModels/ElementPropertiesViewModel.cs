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

    public OrientationOption SelectedOrientation
    {
        get => Orientations.First(o => o.Value == Element.Orientation);
        set => Edit(Element.Orientation, value?.Value ?? Orientation.Normal, v => Element.Orientation = v);
    }

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
}
