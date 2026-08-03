using System.Globalization;
using System.Security.Cryptography;
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

    /// <summary>~DG downloads, which have to precede the ^XA block that recalls them.</summary>
    private readonly StringBuilder _downloads = new();
    private readonly List<string> _warnings = [];
    private readonly HashSet<string> _seenWarnings = new(StringComparer.Ordinal);

    /// <summary>Rasterized images keyed by everything that decides their bits, so a
    /// stamp placed twice is converted once and recognized as the same graphic.</summary>
    private readonly Dictionary<string, SharedGraphic> _graphics = new(StringComparer.Ordinal);
    private int _offset;

    /// <summary>Extra X applied to every origin while a column other than the first is
    /// being emitted, and the copy index that column's counters read.</summary>
    private int _columnX;
    private int _columnCopyIndex;
    private int _columns = 1;
    private LabelDocument? _document;
    private GenerationContext _context = new();
    private bool _preview;
    private bool _printerCounter;
    private bool _printerClock;
    private bool _softwareCounter;

    /// <summary>An image that appears more than once, downloaded once under a name.</summary>
    /// <param name="Name">Storage name; short, because Zebra caps it at eight characters.</param>
    /// <param name="Uses">How many elements share these bits.</param>
    private sealed record SharedGraphic(string Name, int Uses)
    {
        public bool Downloaded { get; set; }
    }

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
        _downloads.Clear();
        _warnings.Clear();
        _seenWarnings.Clear();
        _graphics.Clear();
        _offset = offsetDots;
        _document = document;
        _context = context;
        _preview = includeOffLabel;
        _printerCounter = false;
        _printerClock = false;
        _softwareCounter = false;
        _columnX = 0;
        _columnCopyIndex = context.CopyIndex;

        // A preview is one label on the canvas whatever the stock carries: the designer
        // draws the label being designed, not the web it will be cut from.
        _columns = includeOffLabel ? 1 : Math.Clamp(context.Columns, 1, AcrossLayout.MaxAcross);
        int printWidth = AcrossLayout.WebWidthDots(document, _columns);
        Line("^XA");
        Line("^CI28");
        Line($"^PW{printWidth + 2 * offsetDots}");
        Line($"^LL{document.HeightDots + 2 * offsetDots}");
        Line("^LH0,0");

        // Label reverse is the one job setting that is ink rather than machine setup, so
        // it rides the preview as well: the offline engine honours ^LR exactly as it
        // honours ^FR, measured, and a canvas drawing the label the other way round would
        // be lying about what prints. It has to precede the fields either way, since the
        // manual is explicit that "only fields following this command are affected".
        if (document.Print.ReverseAll)
        {
            Line("^LRY");
        }

        // Job settings ride only the printable output; the preview renderer would
        // just flag them as unknown commands.
        if (!includeOffLabel)
        {
            // ^PM mirrors the whole printable area. A printer does it and the offline
            // engine ignores the command outright, both measured, so emitting it into
            // the preview would buy an unknown-command diagnostic on every render and
            // change nothing. The properties panel is where the canvas says it cannot
            // show this.
            if (document.Print.Mirror)
            {
                Line("^PMY");
            }

            // ^LT is a registration nudge, so it is stated before any field and only
            // when it was asked for: a label that emitted ^LT0 would be overwriting a
            // setting the operator made on the front panel.
            if (document.Print.LabelTopDots != 0)
            {
                Line($"^LT{Math.Clamp(document.Print.LabelTopDots, -120, 120)}");
            }

            // What the printer does with the label once it is printed. Nothing is
            // emitted for the printer's own default, so a label that says nothing about
            // it leaves the mode its operator set.
            if (PrintSettings.Letter(document.Print.MediaHandling) is { } mode)
            {
                // ^MM's prepeel parameter presents the next label early, which only
                // means something to a peeler. Stating it in any other mode would be
                // carrying a setting into a machine that has nothing to peel.
                Line(document.Print.MediaHandling == MediaHandling.PeelOff && document.Print.Prepeel
                    ? $"^MM{mode},Y"
                    : $"^MM{mode}");
            }

            // Media tracking is emitted for continuous stock and for nothing else.
            // A roll with no gaps has to be declared, because a printer left sensing
            // gaps will feed forward hunting for one that is not there. Gap and mark
            // sensing are the other way round: several modes are valid, the operator's
            // is the one that matches what is loaded, and overwriting it from a label
            // would be carrying someone else's printer setup.
            if (document.IsContinuous)
            {
                Line("^MNN");
            }

            if (document.Print.SpeedIps > 0)
            {
                Line($"^PR{Math.Clamp(document.Print.SpeedIps, 2, 14)}");
            }

            if (document.Print.DarknessDelta != 0)
            {
                Line($"^MD{Math.Clamp(document.Print.DarknessDelta, -30, 30)}");
            }
        }

        Element[] emitted = document.Elements
            .Where(e => e.IsVisible)
            .OrderBy(e => e.ZOrder)
            .Where(e => includeOffLabel || ElementPlacement.IsPrintable(e, document))

            // Even the preview cannot express an origin left of / above the pasteboard.
            .Where(e => e.X + offsetDots >= 0 && e.Y + offsetDots >= 0)
            .ToArray();

        PlanSharedGraphics(emitted, _columns);

        // One column is the ordinary case and runs the loop once, so an ordinary label's
        // bytes are exactly what they were before the web existed.
        //
        // The columns are emitted as repeated fields at baked X offsets rather than as a
        // ^LH shift per column, and the manual is what decided that: ^LH "must come
        // before the first ^FS to be compatible with existing printers", and the setting
        // "is retained until you turn off the printer or send a new ^LH". So a per-column
        // ^LH is both out of position and would leave the next job's home moved.
        int pitch = _columns > 1 ? AcrossLayout.PitchDots(document) : 0;
        for (int column = 0; column < _columns; column++)
        {
            _columnX = column * pitch;
            _columnCopyIndex = context.CopyIndex + column;
            foreach (Element element in emitted)
            {
                element.Accept(this);
            }
        }

        _columnX = 0;
        _columnCopyIndex = context.CopyIndex;

        // ^PQ counts pulls of the media, and a pull is the whole web. Stating the label
        // count on 3-across stock would print three times the run.
        int rows = AcrossLayout.Rows(document.Print.Copies, _columns);

        // The group a cut falls after is counted in pulls for the same reason the
        // quantity is, so it goes through the same conversion rather than a second one.
        //
        // Not gated on the mode being a cutter, deliberately. ^PQ's group and ^MM are
        // separate commands in ZPL and a file may state either without the other, so
        // gating here would mean reading a group and then declining to write it back.
        // Whether the machine has a cutter to obey it is the operator's question; the
        // panel is where it gets asked.
        // Rows floors at one, so "no group" has to be asked before converting rather than
        // read out of the answer.
        int cutAfter = Math.Max(document.Print.CutAfterLabels, 0);
        int cutRows = cutAfter > 0 ? AcrossLayout.Rows(cutAfter, _columns) : 0;
        if (!includeOffLabel && context.EmitCopies && (rows > 1 || cutRows > 0))
        {
            // The bare form stays exactly what it was, so every label written before the
            // cutter existed generates the bytes it always did. Reaching ^PQ's override
            // flag means stating the two parameters in between: the replicate count is
            // ZPL's own default, and Y is what makes the printer cut without also
            // pausing to wait for someone to press a button.
            Line(cutRows > 0 ? $"^PQ{rows},{cutRows},0,Y" : $"^PQ{rows}");
        }

        _sb.Append("^XZ");
        LastRun = new GenerationInfo(
            _printerCounter, _printerClock, _softwareCounter, _warnings.ToArray());

        // Downloads have to reach the printer before the block that recalls them.
        return _downloads.Length > 0 ? _downloads.ToString() + _sb : _sb.ToString();
    }

    /// <summary>
    /// Decides which images are worth putting in printer memory. An image placed once
    /// stays an inline ^GF, so an ordinary label touches no printer state at all. The
    /// same bits placed again become a single ~DG download recalled by ^XG, which is
    /// what a repeated stamp costs on a real label: the payload once instead of N times.
    /// </summary>
    /// <param name="columns">How many times the whole element list is emitted. A stamp
    /// placed once on 3-across stock is still three placements on the web, so the download
    /// pays for itself there exactly as a repeated placement does.</param>
    private void PlanSharedGraphics(IReadOnlyList<Element> elements, int columns)
    {
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Element element in elements)
        {
            if (element is not ImageElement image || GraphicKey(image) is not { } key)
            {
                continue;
            }

            if (!counts.TryGetValue(key, out int seen))
            {
                order.Add(key);
            }

            counts[key] = seen + Math.Max(columns, 1);
        }

        // Named in first-use order so the same document always generates the same ZPL.
        int index = 0;
        foreach (string key in order.Where(k => counts[k] > 1))
        {
            _graphics[key] = new SharedGraphic($"LFG{index++}", counts[key]);
        }
    }

    /// <summary>Identity of the bits an image will rasterize to. The pipeline is
    /// deterministic, so equal source bytes at equal size and dithering give equal bits
    /// and the images can share one download without converting either twice.
    /// Null when there is nothing to draw.</summary>
    private static string? GraphicKey(ImageElement image)
    {
        if (image.ImageData.Length == 0 || image.WidthDots <= 0 || image.HeightDots <= 0)
        {
            return null;
        }

        string digest = Convert.ToHexString(SHA256.HashData(image.ImageData));
        return $"{digest}:{image.WidthDots}x{image.HeightDots}:{image.Dithering}";
    }

    /// <summary>The field origin, plus the reverse marker when the field asks for it.
    /// ^FR goes here rather than next to the data because that is the one position that
    /// works for every field type, graphic commands included.
    ///
    /// Which command places the field is the element's own answer: ^FT names its
    /// bottom-left and ^FO its top-left, and an element that came in as one is written
    /// back as the same one. That is not only round-trip tidiness. A ^FT field's printed
    /// position depends on how wide its content turns out to be, so converting it to a
    /// fixed ^FO would freeze it at the width of whatever text the label carries at
    /// design time, which for a template field is a marker rather than the value.</summary>
    private string Fo(Element element) =>
        $"{(element.Anchor == FieldAnchor.Baseline ? "^FT" : "^FO")}"
        + $"{element.X + _offset + _columnX},{element.Y + _offset}"
        + (element.IsReversed ? "^FR" : string.Empty);

    /// <summary>Emits a field's data, resolving the document's counters and clocks. The
    /// preview keeps markers literal so the designer can substitute sample values.</summary>
    private string Field(string text)
    {
        if (_preview || _document is null)
        {
            return ZplEncoding.FieldData(text, _document?.Markers);
        }

        // The column's own copy index, and the stride the printer advances by: on a web
        // laid out three across, one pull prints three labels, so a ^SN field steps three
        // at a time and each column starts one further along.
        FieldEncoding encoding = DynamicField.Encode(
            text, _document, _columnCopyIndex, _context.Now, _columns);
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
        // The font designator is part of the command name, not an argument: ^A0 and ^AD
        // are the same command asking for different fonts.
        string name = $"^A{char.ToUpperInvariant(element.Font)}{element.Orientation.Letter()}";
        string font = element.FontWidthDots > 0
            ? $"{name},{element.FontHeightDots},{element.FontWidthDots}"
            : $"{name},{element.FontHeightDots}";

        // A plain line emits no ^FB at all, so a label that never asked for a block is
        // byte-for-byte what it always was.
        string block = element.IsBlock
            ? $"^FB{element.BlockWidthDots},{Math.Max(element.BlockMaxLines, 1)},"
              + $"{element.BlockLineSpacingDots},{Justify(element.Justification)},"
              + $"{element.BlockHangingIndentDots}"
            : string.Empty;

        Line($"{Fo(element)}{font}{block}{Field(element.Text)}");
    }

    private static string Justify(TextJustification justification) => justification switch
    {
        TextJustification.Center => "C",
        TextJustification.Right => "R",
        TextJustification.Justified => "J",
        _ => "L",
    };

    public void Visit(BoxElement element)
    {
        // The rounding index is emitted only when there is some, so a box that never
        // asked for it generates the bytes it always did. Same rule ^FB follows.
        string rounding = element.CornerRoundness > 0
            ? $",{Math.Clamp(element.CornerRoundness, 0, 8)}"
            : string.Empty;

        Line($"{Fo(element)}^GB{element.WidthDots},{element.HeightDots},"
             + $"{element.ThicknessDots},{Colour(element.IsWhite)}{rounding}^FS");
    }

    /// <summary>
    /// An ellipse, always as ^GE even when its sides are equal and ZPL's own ^GC would
    /// draw it. The two are pixel-identical, so choosing between them by shape would only
    /// mean the command flipping as a resize handle passes through square, and it would
    /// break the round trip for a foreign label that wrote ^GE with equal sides.
    /// </summary>
    public void Visit(EllipseElement element) =>
        Line($"{Fo(element)}^GE{element.WidthDots},{element.HeightDots},"
             + $"{element.ThicknessDots},{Colour(element.IsWhite)}^FS");

    public void Visit(DiagonalLineElement element) =>
        Line($"{Fo(element)}^GD{element.WidthDots},{element.HeightDots},"
             + $"{element.ThicknessDots},{Colour(element.IsWhite)},"
             + $"{(element.LeansRight ? "R" : "L")}^FS");

    public void Visit(LineElement element)
    {
        // Draw a solid bar so orientation is never ambiguous: a vertical line is a
        // thin-wide bar, a horizontal line is a wide-thin bar.
        (int w, int h) = element.IsVertical
            ? (element.ThicknessDots, element.LengthDots)
            : (element.LengthDots, element.ThicknessDots);
        Line($"{Fo(element)}^GB{w},{h},{element.ThicknessDots},{Colour(element.IsWhite)}^FS");
    }

    /// <summary>^GB's colour parameter. White paints the stock clear, which is how a
    /// label erases an area; see <see cref="LineElement.IsWhite"/>.</summary>
    private static string Colour(bool isWhite) => isWhite ? "W" : "B";

    public void Visit(BarcodeElement element)
    {
        string o = element.Orientation.Letter();
        string print = element.PrintInterpretationLine ? "Y" : "N";
        // The ratio rides on ^BY, so it has to be stated by every symbology that reads
        // one or the printer falls back to its default of 3.0 and draws a wider symbol
        // than the model measured. Only the two that use it state it, which is what keeps
        // every label written before Interleaved 2 of 5 existed byte-identical.
        string by = element.Symbology is BarcodeSymbology.Code39 or BarcodeSymbology.Interleaved2of5
            ? $"^BY{element.ModuleWidthDots},{element.WideBarRatio.ToString("0.0", CultureInfo.InvariantCulture)}"
            : $"^BY{element.ModuleWidthDots}";

        string command = element.Symbology switch
        {
            BarcodeSymbology.Code128 => $"^BC{o},{element.HeightDots},{print},N,N",
            BarcodeSymbology.Code39 => $"^B3{o},N,{element.HeightDots},{print},N",
            BarcodeSymbology.Ean13 => $"^BE{o},{element.HeightDots},{print},N",
            BarcodeSymbology.UpcA => $"^BU{o},{element.HeightDots},{print},N,Y",
            BarcodeSymbology.Interleaved2of5 =>
                $"^B2{o},{element.HeightDots},{print},N,{(element.AddCheckDigit ? "Y" : "N")}",
            _ => throw new NotSupportedException($"Unsupported symbology: {element.Symbology}"),
        };

        Line($"{by}{Fo(element)}{command}{Field(element.Data)}");
    }

    public void Visit(DataMatrixElement element) =>
        Line($"{Fo(element)}^BX{element.Orientation.Letter()},{element.ModuleSizeDots},200"
             + Field(element.Data));

    public void Visit(Pdf417Element element)
    {
        // ^B7 orientation, row height, security level, columns, rows, truncate.
        // The row count is deliberately left empty: the model sizes a symbol by its
        // column count, and stating both over-constrains the shape.
        string columns = element.DataColumns > 0
            ? element.DataColumns.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        Line($"^BY{element.ModuleWidthDots}{Fo(element)}"
             + $"^B7{element.Orientation.Letter()},{element.RowHeightDots},"
             + $"{element.SecurityLevel},{columns},,{(element.Truncate ? "Y" : "N")}"
             + Field(element.Data));
    }

    public void Visit(ImageElement element)
    {
        if (GraphicKey(element) is { } key &&
            _graphics.TryGetValue(key, out SharedGraphic? shared))
        {
            if (!shared.Downloaded)
            {
                if (Rasterize(element) is not { } bits)
                {
                    // Undecodable: drop the plan so the other placements take the
                    // inline path and degrade the same way a lone image would.
                    _graphics.Remove(key);
                    return;
                }

                if (_context.IncludeGraphicDownloads)
                {
                    _downloads
                        .Append(Imaging.ZplImageEncoder.EncodeDownload(
                            shared.Name, bits, element.WidthDots, element.HeightDots))
                        .Append('\n');
                }

                shared.Downloaded = true;
            }

            Line(Fo(element) + Imaging.ZplImageEncoder.RecallGraphic(shared.Name));
            return;
        }

        // Undecodable or empty image data degrades to an omitted field, mirroring
        // the renderer's never-throw rule; the designer still shows the placeholder.
        if (Rasterize(element) is { } black)
        {
            Line(Fo(element) + Imaging.ZplImageEncoder.EncodeGfa(
                black, element.WidthDots, element.HeightDots));
        }
    }

    private static bool[]? Rasterize(ImageElement element)
    {
        byte[]? gray = element.ImageData.Length > 0
            ? Imaging.ImageRasterizer.ToGrayscale(element.ImageData, element.WidthDots, element.HeightDots)
            : null;

        return gray is null
            ? null
            : Imaging.ImageDitherer.Dither(
                gray, element.WidthDots, element.HeightDots, element.Dithering);
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
