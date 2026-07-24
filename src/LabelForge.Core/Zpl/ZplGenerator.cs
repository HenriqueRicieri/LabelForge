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
    private readonly List<string> _warnings = [];
    private readonly HashSet<string> _seenWarnings = new(StringComparer.Ordinal);
    private int _offset;
    private LabelDocument? _document;
    private GenerationContext _context = new();
    private bool _preview;
    private bool _printerCounter;
    private bool _printerClock;
    private bool _softwareCounter;

    /// <summary>How the last <see cref="Generate(LabelDocument)"/> produced its dynamic
    /// fields. Reset at the start of every call; meaningless after a preview, which
    /// leaves markers alone.</summary>
    public GenerationInfo LastRun { get; private set; } = GenerationInfo.Empty;

    /// <summary>The first copy of the run, stamped with the current time.</summary>
    public string Generate(LabelDocument document) => Generate(document, new GenerationContext());

    public string Generate(LabelDocument document, GenerationContext context) =>
        Generate(document, context, offsetDots: 0, includeOffLabel: false);

    /// <summary>Preview-only variant: the whole coordinate space shifts right/down by
    /// <paramref name="offsetDots"/> so the label sits centered in a canvas expanded by
    /// that margin on every side, and elements parked off the label stay visible.
    /// Template markers are left literal here; the designer substitutes preview values
    /// on the generated string so the viewer's raw-ZPL path shares that code.</summary>
    public string GeneratePreview(LabelDocument document, int offsetDots) =>
        Generate(document, new GenerationContext(), offsetDots, includeOffLabel: true);

    private string Generate(
        LabelDocument document, GenerationContext context, int offsetDots, bool includeOffLabel)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        _sb.Clear();
        _warnings.Clear();
        _seenWarnings.Clear();
        _offset = offsetDots;
        _document = document;
        _context = context;
        _preview = includeOffLabel;
        _printerCounter = false;
        _printerClock = false;
        _softwareCounter = false;
        Line("^XA");
        Line("^CI28");
        Line($"^PW{document.WidthDots + 2 * offsetDots}");
        Line($"^LL{document.HeightDots + 2 * offsetDots}");
        Line("^LH0,0");

        // Job settings ride only the printable output; the preview renderer would
        // just flag them as unknown commands.
        if (!includeOffLabel)
        {
            if (document.Print.SpeedIps > 0)
            {
                Line($"^PR{Math.Clamp(document.Print.SpeedIps, 2, 14)}");
            }

            if (document.Print.DarknessDelta != 0)
            {
                Line($"^MD{Math.Clamp(document.Print.DarknessDelta, -30, 30)}");
            }
        }

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

        if (!includeOffLabel && context.EmitCopies && document.Print.Copies > 1)
        {
            Line($"^PQ{document.Print.Copies}");
        }

        _sb.Append("^XZ");
        LastRun = new GenerationInfo(
            _printerCounter, _printerClock, _softwareCounter, _warnings.ToArray());
        return _sb.ToString();
    }

    private string Fo(Element element) => $"^FO{element.X + _offset},{element.Y + _offset}";

    /// <summary>Emits a field's data, resolving the document's counters and clocks. The
    /// preview keeps markers literal so the designer can substitute sample values.</summary>
    private string Field(string text)
    {
        if (_preview || _document is null)
        {
            return ZplEncoding.FieldData(text);
        }

        FieldEncoding encoding = DynamicField.Encode(
            text, _document, _context.CopyIndex, _context.Now);
        _printerCounter |= encoding.UsesPrinterCounter;
        _printerClock |= encoding.UsesPrinterClock;
        _softwareCounter |= encoding.UsesSoftwareCounter;
        foreach (string warning in encoding.Warnings)
        {
            if (_seenWarnings.Add(warning))
            {
                _warnings.Add(warning);
            }
        }

        return encoding.Zpl;
    }

    public void Visit(TextElement element)
    {
        string font = element.FontWidthDots > 0
            ? $"^A0{element.Orientation.Letter()},{element.FontHeightDots},{element.FontWidthDots}"
            : $"^A0{element.Orientation.Letter()},{element.FontHeightDots}";
        Line($"{Fo(element)}{font}{Field(element.Text)}");
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

        Line($"{by}{Fo(element)}{command}{Field(element.Data)}");
    }

    public void Visit(DataMatrixElement element) =>
        Line($"{Fo(element)}^BX{element.Orientation.Letter()},{element.ModuleSizeDots},200"
             + Field(element.Data));

    public void Visit(ImageElement element)
    {
        // Undecodable or empty image data degrades to an omitted field, mirroring
        // the renderer's never-throw rule; the designer still shows the placeholder.
        byte[]? gray = element.ImageData.Length > 0
            ? Imaging.ImageRasterizer.ToGrayscale(element.ImageData, element.WidthDots, element.HeightDots)
            : null;
        if (gray is null)
        {
            return;
        }

        bool[] black = Imaging.ImageDitherer.Dither(
            gray, element.WidthDots, element.HeightDots, element.Dithering);
        Line(Fo(element) + Imaging.ZplImageEncoder.EncodeGfa(
            black, element.WidthDots, element.HeightDots));
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
        string payload = Field($"{ec}A,{element.Data}");
        Line($"{Fo(element)}^BQ{element.Orientation.Letter()},2,{element.Magnification}{payload}");
    }

    private void Line(string text) => _sb.Append(text).Append('\n');
}
