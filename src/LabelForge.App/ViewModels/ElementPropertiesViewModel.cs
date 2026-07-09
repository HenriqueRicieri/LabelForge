using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private readonly Action _edited;

    protected ElementPropertiesViewModel(Element element, Action edited)
    {
        Element = element;
        _edited = edited;
    }

    protected Element Element { get; }

    public abstract string TypeName { get; }

    public IReadOnlyList<OrientationOption> Orientations => OrientationOption.All;

    public decimal X
    {
        get => Element.X;
        set => Edit(Element.X, (int)value, v => Element.X = v);
    }

    public decimal Y
    {
        get => Element.Y;
        set => Edit(Element.Y, (int)value, v => Element.Y = v);
    }

    public OrientationOption SelectedOrientation
    {
        get => Orientations.First(o => o.Value == Element.Orientation);
        set => Edit(Element.Orientation, value?.Value ?? Orientation.Normal, v => Element.Orientation = v);
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
        _edited();
    }
}

public sealed class TextPropertiesViewModel : ElementPropertiesViewModel
{
    private readonly TextElement _text;

    public TextPropertiesViewModel(TextElement element, Action edited)
        : base(element, edited) => _text = element;

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
}

public sealed class BarcodePropertiesViewModel : ElementPropertiesViewModel
{
    private readonly BarcodeElement _barcode;

    public BarcodePropertiesViewModel(BarcodeElement element, Action edited)
        : base(element, edited) => _barcode = element;

    public override string TypeName => "Barcode";

    public IReadOnlyList<BarcodeSymbology> Symbologies { get; } = Enum.GetValues<BarcodeSymbology>();

    public BarcodeSymbology Symbology
    {
        get => _barcode.Symbology;
        set
        {
            Edit(_barcode.Symbology, value, v => _barcode.Symbology = v);
            OnPropertyChanged(nameof(IsCode39));
        }
    }

    public bool IsCode39 => _barcode.Symbology == BarcodeSymbology.Code39;

    public string Data
    {
        get => _barcode.Data;
        set => Edit(_barcode.Data, value ?? string.Empty, v => _barcode.Data = v);
    }

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
}

public sealed class QrPropertiesViewModel : ElementPropertiesViewModel
{
    private readonly QrCodeElement _qr;

    public QrPropertiesViewModel(QrCodeElement element, Action edited)
        : base(element, edited) => _qr = element;

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

public sealed class LinePropertiesViewModel : ElementPropertiesViewModel
{
    private readonly LineElement _line;

    public LinePropertiesViewModel(LineElement element, Action edited)
        : base(element, edited) => _line = element;

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
}

public sealed class BoxPropertiesViewModel : ElementPropertiesViewModel
{
    private readonly BoxElement _box;

    public BoxPropertiesViewModel(BoxElement element, Action edited)
        : base(element, edited) => _box = element;

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
}
