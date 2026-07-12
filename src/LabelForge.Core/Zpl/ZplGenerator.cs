using System.Globalization;
using System.Text;
using LabelForge.Core.Model;

namespace LabelForge.Core.Zpl;

/// <summary>
/// Generates ZPL from a <see cref="LabelDocument"/>. This is our own code, kept pure
/// and deterministic so it can be covered by golden tests. It emits ^FO (top-left)
/// origins and the scalable font 0 in v1, and always declares UTF-8 (^CI28).
/// Two modes share the same emission: <see cref="Generate"/> is what prints and
/// exports (elements whose origin is off the label are skipped, matching what the
/// printer could do), while <see cref="GeneratePreview"/> feeds the designer underlay
/// (every visible element is kept and all origins shift by the pasteboard margin so
/// off-label content still renders).
/// </summary>
public sealed class ZplGenerator : IElementVisitor
{
    private readonly StringBuilder _sb = new();
    private int _offset;

    public string Generate(LabelDocument document) =>
        Generate(document, offsetDots: 0, includeOffLabel: false);

    /// <summary>Preview-only variant: the whole coordinate space shifts right/down by
    /// <paramref name="offsetDots"/> so the label sits centered in a canvas expanded by
    /// that margin on every side, and elements parked off the label stay visible.</summary>
    public string GeneratePreview(LabelDocument document, int offsetDots) =>
        Generate(document, offsetDots, includeOffLabel: true);

    private string Generate(LabelDocument document, int offsetDots, bool includeOffLabel)
    {
        ArgumentNullException.ThrowIfNull(document);

        _sb.Clear();
        _offset = offsetDots;
        Line("^XA");
        Line("^CI28");
        Line($"^PW{document.WidthDots + 2 * offsetDots}");
        Line($"^LL{document.HeightDots + 2 * offsetDots}");
        Line("^LH0,0");

        foreach (var element in document.Elements
                     .Where(e => e.IsVisible)
                     .OrderBy(e => e.ZOrder))
        {
            if (!includeOffLabel &&
                !ElementPlacement.IsPrintable(element, document.WidthDots, document.HeightDots))
            {
                continue;
            }

            // Even the preview cannot express an origin left of / above the pasteboard.
            if (element.X + offsetDots < 0 || element.Y + offsetDots < 0)
            {
                continue;
            }

            element.Accept(this);
        }

        _sb.Append("^XZ");
        return _sb.ToString();
    }

    private string Fo(Element element) => $"^FO{element.X + _offset},{element.Y + _offset}";

    public void Visit(TextElement element)
    {
        string font = element.FontWidthDots > 0
            ? $"^A0{element.Orientation.Letter()},{element.FontHeightDots},{element.FontWidthDots}"
            : $"^A0{element.Orientation.Letter()},{element.FontHeightDots}";
        Line($"{Fo(element)}{font}{ZplEncoding.FieldData(element.Text)}");
    }

    public void Visit(BoxElement element) =>
        Line($"{Fo(element)}^GB{element.WidthDots},{element.HeightDots},{element.ThicknessDots},B^FS");

    public void Visit(LineElement element)
    {
        // Draw a solid bar so orientation is never ambiguous: a vertical line is a
        // thin-wide bar, a horizontal line is a wide-thin bar.
        (int w, int h) = element.IsVertical
            ? (element.ThicknessDots, element.LengthDots)
            : (element.LengthDots, element.ThicknessDots);
        Line($"{Fo(element)}^GB{w},{h},{element.ThicknessDots},B^FS");
    }

    public void Visit(BarcodeElement element)
    {
        string o = element.Orientation.Letter();
        string print = element.PrintInterpretationLine ? "Y" : "N";
        string by = element.Symbology == BarcodeSymbology.Code39
            ? $"^BY{element.ModuleWidthDots},{element.WideBarRatio.ToString("0.0", CultureInfo.InvariantCulture)}"
            : $"^BY{element.ModuleWidthDots}";

        string command = element.Symbology switch
        {
            BarcodeSymbology.Code128 => $"^BC{o},{element.HeightDots},{print},N,N",
            BarcodeSymbology.Code39 => $"^B3{o},N,{element.HeightDots},{print},N",
            BarcodeSymbology.Ean13 => $"^BE{o},{element.HeightDots},{print},N",
            BarcodeSymbology.UpcA => $"^BU{o},{element.HeightDots},{print},N,Y",
            _ => throw new NotSupportedException($"Unsupported symbology: {element.Symbology}"),
        };

        Line($"{by}{Fo(element)}{command}{ZplEncoding.FieldData(element.Data)}");
    }

    public void Visit(QrCodeElement element)
    {
        string ec = element.ErrorCorrection switch
        {
            QrErrorCorrection.Low => "L",
            QrErrorCorrection.Medium => "M",
            QrErrorCorrection.Quartile => "Q",
            QrErrorCorrection.High => "H",
            _ => "M",
        };
        string payload = ZplEncoding.FieldData($"{ec}A,{element.Data}");
        Line($"{Fo(element)}^BQ{element.Orientation.Letter()},2,{element.Magnification}{payload}");
    }

    private void Line(string text) => _sb.Append(text).Append('\n');
}
