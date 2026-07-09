using System.Globalization;
using System.Text;
using LabelForge.Core.Model;

namespace LabelForge.Core.Zpl;

/// <summary>
/// Generates ZPL from a <see cref="LabelDocument"/>. This is our own code, kept pure
/// and deterministic so it can be covered by golden tests. It emits ^FO (top-left)
/// origins and the scalable font 0 in v1, and always declares UTF-8 (^CI28).
/// </summary>
public sealed class ZplGenerator : IElementVisitor
{
    private readonly StringBuilder _sb = new();

    public string Generate(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _sb.Clear();
        Line("^XA");
        Line("^CI28");
        Line($"^PW{document.WidthDots}");
        Line($"^LL{document.HeightDots}");
        Line("^LH0,0");

        foreach (var element in document.Elements
                     .Where(e => e.IsVisible)
                     .OrderBy(e => e.ZOrder))
        {
            element.Accept(this);
        }

        _sb.Append("^XZ");
        return _sb.ToString();
    }

    public void Visit(TextElement element)
    {
        string font = element.FontWidthDots > 0
            ? $"^A0{element.Orientation.Letter()},{element.FontHeightDots},{element.FontWidthDots}"
            : $"^A0{element.Orientation.Letter()},{element.FontHeightDots}";
        Line($"^FO{element.X},{element.Y}{font}{ZplEncoding.FieldData(element.Text)}");
    }

    public void Visit(BoxElement element) =>
        Line($"^FO{element.X},{element.Y}^GB{element.WidthDots},{element.HeightDots},{element.ThicknessDots},B^FS");

    public void Visit(LineElement element)
    {
        // Draw a solid bar so orientation is never ambiguous: a vertical line is a
        // thin-wide bar, a horizontal line is a wide-thin bar.
        (int w, int h) = element.IsVertical
            ? (element.ThicknessDots, element.LengthDots)
            : (element.LengthDots, element.ThicknessDots);
        Line($"^FO{element.X},{element.Y}^GB{w},{h},{element.ThicknessDots},B^FS");
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

        Line($"{by}^FO{element.X},{element.Y}{command}{ZplEncoding.FieldData(element.Data)}");
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
        Line($"^FO{element.X},{element.Y}^BQ{element.Orientation.Letter()},2,{element.Magnification}{payload}");
    }

    private void Line(string text) => _sb.Append(text).Append('\n');
}
