using System.Globalization;
using LabelForge.Core.Imaging;
using LabelForge.Core.Model;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;

namespace LabelForge.Core.Io;

/// <param name="Document">The label that was recovered. Never null: a stream with
/// nothing we can model still gives an empty document of the declared size.</param>
/// <param name="LabelCount">How many ^XA blocks the stream holds, so a caller can offer
/// the others.</param>
/// <param name="SelectedIndex">Which block the document came from, so a caller can say
/// which one of several it is showing.</param>
/// <param name="Warnings">What did not survive, in encounter order. A ZPL file can say
/// things this model has no room for, and the honest answer is to name them rather than
/// hand back a label that quietly lost a field.</param>
/// <param name="BlockElementCounts">How much each ^XA block in the file holds, in order.
/// Reported because one block is all a caller gets back, and without this it has no way
/// to offer the others or to say which of them are worth opening. Counted during the same
/// pass, since every block is built anyway.</param>
/// <param name="MeasuredSize">Set when the file stated no size and one was measured from
/// its content, describing what was worked out; null when the file said so itself.
///
/// Deliberately not a warning. `^PW` and `^LL` are optional and real files leave them out
/// - 28 of the 29 in the sample corpus state neither - so putting this in the warning list
/// would fire on nearly every import and bury the entries that name real losses. It is the
/// same kind of thing as an inferred text encoding, and it is reported the same way: beside
/// the result, for the caller to state once.</param>
public sealed record ZplDocumentImportResult(
    LabelDocument Document,
    int LabelCount,
    int SelectedIndex,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<int> BlockElementCounts,
    string? MeasuredSize = null);

/// <summary>
/// Reads a ZPL label back into the document model: the inverse of
/// <see cref="ZplGenerator"/>.
///
/// Scope is deliberately staged. This round covers the commands our own generator emits,
/// so our files round-trip exactly, and that property is what the tests pin: generate,
/// parse, generate again, and the bytes must match. Foreign labels are read as far as
/// those commands take them and everything else is reported, which is the honest state
/// to ship rather than a parser that pretends to understand a whole corpus.
///
/// One thing ZPL genuinely does not say is the print density: ^PW and ^LL are in dots,
/// and only the operator knows which printer the file was written for. The caller
/// supplies dpmm and the millimetre size follows from it.
/// </summary>
public static class ZplDocumentImport
{
    /// <summary>Commands this importer consumes. Either they produce an element, or they
    /// are structural, or the generator re-emits them from the document.</summary>
    private static readonly HashSet<string> Handled = new(StringComparer.Ordinal)
    {
        "XA", "XZ", "CI", "FS", "FH", "LH", "PW", "LL", "BY", "PQ", "MD", "PR",
        "DG", "FO", "FT", "FD", "FB", "FR", "SN", "GB", "GE", "GC", "GD", "GF", "XG",
        "BC", "B3", "BE", "BU", "BX", "BQ", "B7",
    };

    /// <summary>Media tracking modes that mean continuous stock: no gaps, notches or
    /// marks to sense, the printer advances by ^LL alone. "N" is what the generator
    /// writes; a real printer also accepts the variable-length form.</summary>
    private static readonly HashSet<string> ContinuousTracking = new(StringComparer.OrdinalIgnoreCase)
    {
        "N", "V",
    };

    /// <summary>
    /// Commands that configure the printer rather than describe the label: media type,
    /// tear-off position, darkness curves, the printer's own defaults.
    ///
    /// Dropping these is correct, not a shortfall. They belong to whoever set the printer
    /// up, and a label the designer re-emits should not be carrying someone else's tear
    /// offset. Naming them separately keeps the warning list to actual losses, which is
    /// the only way that list stays worth reading.
    /// </summary>
    /// Kept deliberately narrow. Commands that change what the label looks like or where
    /// it prints stay off this list even when they look like setup, because losing one of
    /// those is a real loss the user has to hear about: ^LR and ^PM invert or mirror the
    /// whole label, ^LS and ^LT shift every field, ^DF stores the format on the printer,
    /// and ^CC and ^CT redefine the characters this parser reads.
    /// ^MN is the one that moved off this list conditionally rather than wholesale.
    /// Gap and mark sensing stay printer setup, because several modes are valid and the
    /// operator's matches what is loaded; continuous is different, because a roll with
    /// no gaps is a fact about the label's own stock and about how long it prints.
    private static readonly HashSet<string> PrinterConfiguration = new(StringComparer.Ordinal)
    {
        "MM", "MT", "MP", "MU", "MF", "MC", "MW", "CO",
        "IS", "ID", "JU", "JA", "SS", "SZ", "SC", "ST", "XS", "SX",
        "NI", "NC", "NR", "NS", "WD", "KL", "KN", "KP", "HS", "HH", "HW", "HM",
    };

    /// <param name="labelIndex">Which ^XA block to import, or -1 to take the first block
    /// that holds anything. Real files routinely open with a bare configuration block,
    /// so counting from zero would hand back an empty label for the commonest shape.</param>
    /// <param name="markers">Delimiters this file writes its template markers with.
    /// Needed because a marker must survive ^FH hex escaping verbatim; see Unescape.</param>
    public static ZplDocumentImportResult FromZpl(
        string zpl, int dpmm = 8, int labelIndex = -1, MarkerSyntax? markers = null)
    {
        ArgumentNullException.ThrowIfNull(zpl);
        MarkerSyntax syntax = markers ?? MarkerSyntax.Default;
        var state = new ParseState(dpmm, labelIndex, syntax);

        // Downloaded graphics are defined outside the block that recalls them, so they
        // are collected up front by the same scanner the viewer and the graphic import
        // use rather than by a second pass written here.
        state.Graphics = ZplGraphicScanner.Scan(zpl);

        // A ^SN counter needs a marker name of its own, and the only names it must not
        // take are the ones the file already writes. Collected up front because a field
        // further down the file is just as much a collision as one already read.
        state.ReservedNames = TemplateScanner.Scan(zpl, syntax)
            .Where(s => s.Kind == TemplateSegmentKind.Variable)
            .Select(s => syntax.NameOf(s.Inner))
            .ToHashSet(StringComparer.Ordinal);

        foreach (ZplCommand command in ZplCommandReader.Read(zpl))
        {
            state.Apply(command);
        }

        return state.Build();
    }

    /// <summary>Everything one pass needs to remember. ZPL is a stream of modal
    /// commands, so an element only exists once the field that owns it is closed.</summary>
    private sealed class ParseState(int dpmm, int labelIndex, MarkerSyntax markers)
    {
        /// <summary>What a counter recovered from ^SN is called, after ZPL's own name for
        /// the command. A second one that is not the same counter gets a number after it.</summary>
        private const string SerialVariableName = "SERIAL";


        /// <summary>One ^XA block's worth of findings. Every block is built, because
        /// which one is the label cannot be known until they have all been seen.</summary>
        private sealed class BlockDraft
        {
            public List<Element> Elements { get; } = [];

            public List<string> Warnings { get; } = [];

            public HashSet<string> SeenWarnings { get; } = new(StringComparer.Ordinal);

            /// <summary>Printer setup seen and deliberately left behind, kept apart from
            /// the warnings so the list of real losses stays worth reading.</summary>
            public SortedSet<string> IgnoredConfiguration { get; } = new(StringComparer.Ordinal);

            /// <summary>Counters recovered from ^SN, by the marker name standing in for
            /// each one in the field it came from.</summary>
            public Dictionary<string, VariableDefinition> Variables { get; } =
                new(StringComparer.Ordinal);

            public int? WidthDots { get; set; }

            public int? HeightDots { get; set; }

            public bool IsContinuous { get; set; }

            public PrintSettings Print { get; } = new();
        }

        private readonly List<BlockDraft> _blocks = [];
        private BlockDraft? _current;

        /// <summary>
        /// The label size in force, which outlives the block that set it.
        ///
        /// `^LL` is "retained until you turn off the printer or send a new ^LL", and `^PW`
        /// defaults to "the last permanently saved value": both are printer settings rather
        /// than properties of one `^XA` block. A driver states them once in a setup block at
        /// the top of the file and never again, so scoping them to their own block leaves
        /// every real label after it with no stated size at all.
        /// </summary>
        private int? _widthDots;
        private int? _heightDots;

        private int _homeX;
        private int _homeY;
        private int? _originX;
        private int? _originY;
        private bool _typeset;
        private Orientation _orientation = Orientation.Normal;
        private bool _hexEscapes;
        private char _hexMarker = '_';
        private bool _reversed;

        // Font state. ^BY is not here on purpose: in ZPL it is a format-level default
        // that outlives the field that set it.
        private bool _haveFont;
        private char _font = ZplFont.Scalable;
        private int _fontHeight;
        private int _fontWidth;

        private int _moduleWidth = 2;
        private double _wideRatio = 3.0;

        private BarcodeSymbology? _symbology;
        private int _barcodeHeight;
        private bool _interpretation = true;
        private bool _addCheckDigit;

        private int? _dataMatrixModule;
        private int? _qrMagnification;
        private Pdf417Element? _pdf417;

        /// <summary>The ^CI in force. Zero is the printer's own default, and most real
        /// labels never say otherwise, which is why the default has to be right rather
        /// than merely documented.</summary>
        private int _internationalSet;

        // ^FB state, field-level like the font.
        private int _blockWidth;
        private int _blockMaxLines = 1;
        private int _blockSpacing;
        private int _blockIndent;
        private TextJustification _justification = TextJustification.Left;

        public ZplGraphicScan Graphics { get; set; } = new([], [], []);

        /// <summary>Marker names the file already writes, which a recovered ^SN counter
        /// must not take for itself.</summary>
        public HashSet<string> ReservedNames { get; set; } = new(StringComparer.Ordinal);

        public void Apply(ZplCommand command)
        {
            if (command.Code == "XA")
            {
                // The size in force carries into the new block: a block that states its own
                // overwrites it below, one that does not inherits what the file last said.
                _current = new BlockDraft { WidthDots = _widthDots, HeightDots = _heightDots };
                _blocks.Add(_current);
                ResetField();
                _homeX = 0;
                _homeY = 0;
                return;
            }

            // Read before the preamble guard below, because these are printer modes rather
            // than parts of a label: they can be set outside any ^XA block and they stay set
            // across the ones that follow. ^PW and ^LL are here for the reason the manual
            // gives and a driver relies on, stated on the fields they set.
            switch (command.Code)
            {
                case "CI":
                    _internationalSet = command.Int(0);
                    return;

                case "PW":
                    _widthDots = command.Int(0);
                    if (_current is { } widthBlock)
                    {
                        widthBlock.WidthDots = _widthDots;
                    }

                    return;

                case "LL":
                    _heightDots = command.Int(0);
                    if (_current is { } heightBlock)
                    {
                        heightBlock.HeightDots = _heightDots;
                    }

                    return;
            }

            // Anything before the first ^XA is preamble: printer configuration, and the
            // ~DG downloads that the graphic scan already collected.
            if (_current is not { } block)
            {
                return;
            }

            switch (command.Code)
            {
                case "LH":
                    _homeX = command.Int(0);
                    _homeY = command.Int(1);
                    return;

                case "FO":
                    _originX = command.Int(0);
                    _originY = command.Int(1);
                    _typeset = false;
                    return;

                case "FT":
                    // Remembered rather than converted here: ^FT is the field's bottom
                    // left, and how far that is from the top depends on what the field
                    // turns out to be, which ^FD has not said yet.
                    _originX = command.Int(0);
                    _originY = command.Int(1);
                    _typeset = true;
                    return;

                case "FS":
                    ResetField();
                    return;

                case "FH":
                    _hexEscapes = true;
                    _hexMarker = command.Parameters.Length > 0 ? command.Parameters[0] : '_';
                    return;

                case "FR":
                    _reversed = true;
                    return;

                case "FB":
                    // ^FBwidth,maxLines,lineSpacing,justification,hangingIndent. Every
                    // argument is optional in the wild ("^FB931,1,,C" is the commonest
                    // corpus form), so each falls back to the ZPL default on its own.
                    _blockWidth = command.Int(0);
                    _blockMaxLines = Math.Max(command.Int(1, 1), 1);
                    _blockSpacing = command.Int(2);
                    _justification = command.Arg(3).ToUpperInvariant() switch
                    {
                        "C" => TextJustification.Center,
                        "R" => TextJustification.Right,
                        "J" => TextJustification.Justified,
                        _ => TextJustification.Left,
                    };
                    _blockIndent = command.Int(4);
                    return;

                case "BY":
                    _moduleWidth = command.Int(0, _moduleWidth);
                    if (double.TryParse(command.Arg(1), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double ratio) && ratio > 0)
                    {
                        _wideRatio = ratio;
                    }

                    return;

                case "BC":
                    StartBarcode(BarcodeSymbology.Code128, command, heightIndex: 1, printIndex: 2);
                    return;

                case "B3":
                    StartBarcode(BarcodeSymbology.Code39, command, heightIndex: 2, printIndex: 3);
                    return;

                case "BE":
                    StartBarcode(BarcodeSymbology.Ean13, command, heightIndex: 1, printIndex: 2);
                    return;

                case "BU":
                    StartBarcode(BarcodeSymbology.UpcA, command, heightIndex: 1, printIndex: 2);
                    return;

                case "B2":
                    // ^B2's fifth argument asks the printer to work out a Mod 10 check
                    // digit. It falls back to ZPL's own default rather than to this
                    // model's, which is the call ^B7 already makes: an imported label has
                    // to keep printing what it printed.
                    StartBarcode(
                        BarcodeSymbology.Interleaved2of5, command,
                        heightIndex: 1, printIndex: 2, checkDigitIndex: 4);
                    return;

                case "BX":
                    _orientation = ToOrientation(command.Arg(0));
                    _dataMatrixModule = Math.Clamp(command.Int(1, 4), 1, 20);
                    return;

                case "BQ":
                    _orientation = ToOrientation(command.Arg(0));
                    _qrMagnification = Math.Clamp(command.Int(2, 5), 1, 10);
                    return;

                case "B7":
                    StartPdf417(command);
                    return;

                case "FD":
                    Field(Unescape(command.Parameters));
                    return;

                case "SN":
                    Serialize(command);
                    return;

                case "GB":
                    Graphic(command);
                    return;

                case "GE":
                    Ellipse(command.Int(0), command.Int(1), command);
                    return;

                case "GC":
                    // A circle is an ellipse with equal sides, measured pixel-identical
                    // against the renderer, so it needs no type of its own. Its arguments
                    // are shifted by one: diameter, thickness, colour.
                    Ellipse(command.Int(0, 3), command.Int(0, 3), command, argumentOffset: 1);
                    return;

                case "GD":
                    Diagonal(command);
                    return;

                case "GF":
                    InlineImage(command);
                    return;

                case "XG":
                    RecalledImage(command);
                    return;

                case "MN":
                    // Continuous stock belongs to the label; any other tracking mode is
                    // the printer's and is left where it was found.
                    if (ContinuousTracking.Contains(command.Arg(0)))
                    {
                        block.IsContinuous = true;
                    }
                    else
                    {
                        block.IgnoredConfiguration.Add($"{command.Prefix}{command.Code}");
                    }

                    return;

                case "PQ":
                    block.Print.Copies = Math.Max(command.Int(0, 1), 1);
                    return;

                case "MD":
                    block.Print.DarknessDelta = command.Int(0);
                    return;

                case "PR":
                    block.Print.SpeedIps = command.Int(0);
                    return;
            }

            if (command.Code.Length > 1 && command.Code[0] == 'A')
            {
                // ^A carries its font designator in the command name rather than in an
                // argument, so ^AD is the same command as ^A0 asking for another font.
                char font = char.ToUpperInvariant(command.Code[1]);
                if (!ZplFont.IsSupported(font))
                {
                    // A font this printer was given rather than born with: it lives in
                    // the printer's memory, so there is nothing here to draw it with.
                    Warn($"Font {command.Prefix}{command.Code} is not one of the printer's "
                         + $"built-in fonts, so the scalable font {ZplFont.Scalable} was used "
                         + "instead. Text in it will be a different width than it prints.");
                    font = ZplFont.Scalable;
                }

                _haveFont = true;
                _font = font;
                _orientation = ToOrientation(command.Arg(0));

                // A bitmapped font with no size asked for is its own cell, which is what
                // the printer falls back to; the scalable font has no cell to fall back
                // on, so it keeps the model's own default.
                FontCell? cell = ZplFont.Cell(font, dpmm);
                _fontHeight = command.Int(1, cell?.HeightDots ?? 30);
                _fontWidth = command.Int(2, cell is null ? 0 : cell.Value.WidthDots);
                return;
            }

            if (PrinterConfiguration.Contains(command.Code))
            {
                block.IgnoredConfiguration.Add($"{command.Prefix}{command.Code}");
            }
            else if (!Handled.Contains(command.Code))
            {
                Warn($"{command.Prefix}{command.Code} is not modelled and was dropped.");
            }
        }

        public ZplDocumentImportResult Build()
        {
            // An explicit index is honoured even when that block is empty: the caller
            // asked for that one. Otherwise take the first block that holds something,
            // because a bare configuration block ahead of the real label is routine.
            int index = labelIndex >= 0
                ? labelIndex
                : _blocks.FindIndex(b => b.Elements.Count > 0);

            if (index < 0 || index >= _blocks.Count)
            {
                index = 0;
            }

            int[] counts = _blocks.Select(b => b.Elements.Count).ToArray();

            var document = new LabelDocument { Dpmm = Math.Max(dpmm, 1), Markers = markers };
            if (_blocks.Count == 0 || index >= _blocks.Count)
            {
                return new ZplDocumentImportResult(document, _blocks.Count, 0, [], counts);
            }

            BlockDraft block = _blocks[index];
            document.Print.Copies = block.Print.Copies;
            document.Print.DarknessDelta = block.Print.DarknessDelta;
            document.Print.SpeedIps = block.Print.SpeedIps;

            for (int i = 0; i < block.Elements.Count; i++)
            {
                block.Elements[i].ZOrder = i;
                document.Elements.Add(block.Elements[i]);
            }

            // Only this block's counters: a variable belongs to the markers that name it,
            // and the fields of the blocks not taken are not on the document either.
            foreach ((string name, VariableDefinition definition) in block.Variables)
            {
                document.Variables[name] = definition;
            }

            // Sizing comes after the elements, because a file that does not state a size
            // is measured from what it draws.
            string? measured = Size(document, block);

            if (block.IsContinuous)
            {
                // ZPL never states the trailing blank, so it is recovered from the one
                // place it is visible: the difference between the length the file asked
                // for and the content that fills it. That is exactly how it was produced,
                // so a label of ours comes back with the margin it left with, and a
                // foreign one keeps whatever slack its author gave it.
                document.IsContinuous = true;
                double declaredMm = block.HeightDots is { } length && length > 0
                    ? (double)length / document.Dpmm
                    : 0;
                document.ContinuousMarginMm = Math.Max(
                    declaredMm - ContinuousLength.ContentMm(document), 0);
            }

            var warnings = new List<string>(block.Warnings);
            if (block.IgnoredConfiguration.Count > 0)
            {
                warnings.Add(
                    "Printer setup in the file was left behind, which is deliberate: it "
                    + "belongs to whoever configured the printer, not to this label ("
                    + string.Join(", ", block.IgnoredConfiguration) + ").");
            }

            return new ZplDocumentImportResult(
                document, _blocks.Count, index, warnings, counts, measured);
        }

        /// <summary>
        /// Sets the label's size, and says so when the file did not.
        ///
        /// `^PW` and `^LL` are optional, and real files leave them out: 28 of the 29 in
        /// the sample corpus state neither, because a label sent to a printer already set
        /// up for its stock has no reason to. A fixed default is a guess, and one that
        /// comes out too small is not a cosmetic problem: everything past the edge is off
        /// the label, so it stops printing and leaves the generated ZPL. Ten of those
        /// files draw past a 150 mm default and lost the difference.
        ///
        /// Measuring the content is the smallest claim that loses nothing, and it works
        /// in both directions: 603.zpl comes out 45 by 16 mm, which is what its own drawn
        /// border says it is, rather than being floated on a page six times its size.
        /// Each axis is decided on its own, since a file can state one and not the other.
        /// </summary>
        /// <returns>What to tell the user, or null when the file stated its own size.</returns>
        private static string? Size(LabelDocument document, BlockDraft block)
        {
            bool hasWidth = block.WidthDots is > 0;
            bool hasHeight = block.HeightDots is > 0;
            if (hasWidth)
            {
                document.WidthMm = (double)block.WidthDots!.Value / document.Dpmm;
            }

            if (hasHeight)
            {
                document.HeightMm = (double)block.HeightDots!.Value / document.Dpmm;
            }

            if (hasWidth && hasHeight)
            {
                return null;
            }

            // Nothing drawn is nothing to measure, so an empty block keeps the default
            // rather than collapsing to no label at all.
            if (LabelExtent.MeasureMm(
                    document.Elements, document.Dpmm, document.Markers) is not { } extent)
            {
                return null;
            }

            if (!hasWidth)
            {
                document.WidthMm = extent.WidthMm;
            }

            if (!hasHeight)
            {
                document.HeightMm = extent.HeightMm;
            }

            string missing = (hasWidth, hasHeight) switch
            {
                (true, false) => "no label length (^LL)",
                (false, true) => "no label width (^PW)",
                _ => "no label size (^PW, ^LL)",
            };

            return string.Format(
                CultureInfo.CurrentCulture,
                "The file states {0}, so it was measured from what the label draws: "
                + "{1:0.#} by {2:0.#} mm at {3:0} dpi. Set the real size if you know it.",
                missing,
                document.WidthMm,
                document.HeightMm,
                document.Dpmm * 25.4);
        }

        private void StartBarcode(
            BarcodeSymbology symbology, ZplCommand command, int heightIndex, int printIndex,
            int checkDigitIndex = -1)
        {
            _symbology = symbology;
            _orientation = ToOrientation(command.Arg(0));
            _barcodeHeight = command.Int(heightIndex, 100);
            _interpretation = !string.Equals(command.Arg(printIndex), "N", StringComparison.OrdinalIgnoreCase);
            _addCheckDigit = checkDigitIndex >= 0 &&
                             string.Equals(command.Arg(checkDigitIndex), "Y", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// ^B7 orientation, row height, security level, columns, rows, truncate.
        ///
        /// Every argument falls back to the ZPL default on its own rather than to this
        /// model's default, because an imported label has to keep printing what it
        /// printed: ZPL's security level really is 0, however poor a choice that is for
        /// a symbol created here.
        /// </summary>
        private void StartPdf417(ZplCommand command)
        {
            _orientation = ToOrientation(command.Arg(0));
            _pdf417 = new Pdf417Element
            {
                ModuleWidthDots = Math.Clamp(_moduleWidth, 1, 10),
                RowHeightDots = Math.Max(command.Int(1, 8), 1),
                SecurityLevel = Math.Clamp(command.Int(2, 0), 0, 8),
                DataColumns = Math.Clamp(
                    command.Int(3, 0), 0, Pdf417Metrics.MaxColumns),
                Truncate = string.Equals(command.Arg(5), "Y", StringComparison.OrdinalIgnoreCase),
            };

            // The model sizes a PDF417 by its column count, so a row count is a real
            // loss: it is the other way the file could have pinned the shape.
            if (command.Arg(4).Length > 0)
            {
                Warn($"^B7 asked for {command.Arg(4)} rows, which was dropped: this model "
                     + "sizes a PDF417 by its column count.");
            }
        }

        /// <summary>A ^FD closes whatever field type is pending. Which one it is decides
        /// the element, so the order here mirrors the generator's emission.</summary>
        private void Field(string data)
        {
            if (_symbology is { } symbology)
            {
                Add(new BarcodeElement
                {
                    Symbology = symbology,
                    Data = data,
                    HeightDots = _barcodeHeight,
                    ModuleWidthDots = _moduleWidth,
                    WideBarRatio = _wideRatio,
                    PrintInterpretationLine = _interpretation,
                    AddCheckDigit = _addCheckDigit,
                });
                return;
            }

            if (_dataMatrixModule is { } module)
            {
                Add(new DataMatrixElement { Data = data, ModuleSizeDots = module });
                return;
            }

            if (_qrMagnification is { } magnification)
            {
                Add(BuildQr(data, magnification));
                return;
            }

            if (_pdf417 is { } pdf)
            {
                pdf.Data = data;
                Add(pdf);
                return;
            }

            if (_haveFont)
            {
                Add(new TextElement
                {
                    Text = data,
                    Font = _font,
                    FontHeightDots = _fontHeight,
                    FontWidthDots = _fontWidth,
                    BlockWidthDots = _blockWidth,
                    BlockMaxLines = _blockMaxLines,
                    BlockLineSpacingDots = _blockSpacing,
                    BlockHangingIndentDots = _blockIndent,
                    Justification = _justification,
                });
                return;
            }

            Warn("Field data arrived with no font or barcode in force and was dropped.");
        }

        /// <summary>
        /// ^SN is field data the printer advances per copy, so it closes the field exactly
        /// the way ^FD does and the element is whichever type is pending: the manual puts
        /// both alphanumeric and barcode fields in scope, and the generator emits it in
        /// place of ^FD rather than beside it.
        ///
        /// Read without this the field produces nothing at all, since ^SN is where its data
        /// lives, so a serialized field did not merely lose its numbering, it vanished.
        ///
        /// The number becomes a counter variable and a marker takes its place in the text,
        /// which is the model C3 already built: what comes back out is ^SN again, at the
        /// same value, step and padding. A ^SN whose value carries trailing text cannot,
        /// because the printer advances the end of the field and our own eligibility rule
        /// says so; that one regenerates as a counter numbered here, and DynamicField
        /// states the reason on every render rather than this repeating it in other words.
        /// </summary>
        private void Serialize(ZplCommand command)
        {
            SerialNumberField serial = ZplSerialNumber.Read(command);
            if (serial.Counter is not { } counter)
            {
                Field(serial.Prefix);
                return;
            }

            Field(serial.Prefix + markers.Marker(NameFor(counter)) + serial.Suffix);
        }

        /// <summary>
        /// The marker name a recovered counter is given, reusing one when the numbers match.
        ///
        /// Two ^SN fields started at the same value with the same step advance in lockstep,
        /// which is one counter seen in two places rather than two counters. Reusing the
        /// variable is what the Variables panel should show, and both fields still
        /// regenerate the ^SN they came from.
        /// </summary>
        private string NameFor(VariableDefinition counter)
        {
            if (_current is not { } block)
            {
                return SerialVariableName;
            }

            foreach ((string existing, VariableDefinition definition) in block.Variables)
            {
                if (definition.CounterStart == counter.CounterStart &&
                    definition.CounterStep == counter.CounterStep &&
                    definition.CounterPadding == counter.CounterPadding)
                {
                    return existing;
                }
            }

            string name = SerialVariableName;
            for (int i = 2; block.Variables.ContainsKey(name) || ReservedNames.Contains(name); i++)
            {
                name = SerialVariableName + i.ToString(CultureInfo.InvariantCulture);
            }

            block.Variables[name] = counter;
            return name;
        }

        /// <summary>^BQ carries the error correction level and input mode in front of the
        /// payload ("MA,text"). Anything else is taken as the payload itself.</summary>
        private static QrCodeElement BuildQr(string data, int magnification)
        {
            QrErrorCorrection correction = QrErrorCorrection.Medium;
            string payload = data;

            int comma = data.IndexOf(',');
            if (comma >= 2)
            {
                correction = char.ToUpperInvariant(data[0]) switch
                {
                    'L' => QrErrorCorrection.Low,
                    'Q' => QrErrorCorrection.Quartile,
                    'H' => QrErrorCorrection.High,
                    _ => QrErrorCorrection.Medium,
                };
                payload = data[(comma + 1)..];
            }

            return new QrCodeElement
            {
                Data = payload,
                Magnification = magnification,
                ErrorCorrection = correction,
            };
        }

        /// <summary>^GB is a box, and a box whose border fills it is how ZPL draws a
        /// line. Reading it back as a line loses nothing: both generate the same bytes.
        /// The colour parameter is carried through, because a white ^GB is how these
        /// labels clear an area and reading it as black paints over what it was meant
        /// to reveal.</summary>
        private void Graphic(ZplCommand command)
        {
            int width = command.Int(0);
            int height = command.Int(1);
            int thickness = Math.Max(command.Int(2, 1), 1);
            bool isWhite = string.Equals(command.Arg(3), "W", StringComparison.OrdinalIgnoreCase);

            if (height <= thickness && width > 0)
            {
                Add(new LineElement
                {
                    IsVertical = false,
                    LengthDots = width,
                    ThicknessDots = thickness,
                    IsWhite = isWhite,
                });
            }
            else if (width <= thickness && height > 0)
            {
                Add(new LineElement
                {
                    IsVertical = true,
                    LengthDots = height,
                    ThicknessDots = thickness,
                    IsWhite = isWhite,
                });
            }
            else
            {
                Add(new BoxElement
                {
                    WidthDots = width,
                    HeightDots = height,
                    ThicknessDots = thickness,
                    IsWhite = isWhite,

                    // A rounded ^GB stays a box; the index is part of what it draws.
                    // Read even for a line, since a thin ^GB carrying one is legal, and
                    // the line branch above drops it: rounding a bar one dot tall is
                    // nothing a printer can show.
                    CornerRoundness = Math.Clamp(command.Int(4), 0, 8),
                });
            }

            ResetField();
        }

        /// <summary>
        /// ^GE (width, height, thickness, colour) and ^GC (diameter, thickness, colour),
        /// which draw the same dots when the sides match, so both land on one element.
        /// </summary>
        /// <param name="argumentOffset">How far the thickness and colour sit into the
        /// argument list: 2 for ^GE, which states two sides, and 1 for ^GC's diameter.</param>
        private void Ellipse(int width, int height, ZplCommand command, int argumentOffset = 2)
        {
            Add(new EllipseElement
            {
                WidthDots = Math.Max(width, ElementResizer.MinShapeSideDots),
                HeightDots = Math.Max(height, ElementResizer.MinShapeSideDots),
                ThicknessDots = Math.Max(command.Int(argumentOffset, 1), 1),
                IsWhite = string.Equals(
                    command.Arg(argumentOffset + 1), "W", StringComparison.OrdinalIgnoreCase),
            });

            ResetField();
        }

        /// <summary>^GD width, height, thickness, colour, lean. The lean is "R" for a line
        /// running bottom-left to top-right and "L" for the other one, and ZPL's default
        /// is R.</summary>
        private void Diagonal(ZplCommand command)
        {
            var diagonal = new DiagonalLineElement
            {
                WidthDots = Math.Max(
                    command.Int(0, ElementResizer.MinShapeSideDots),
                    ElementResizer.MinShapeSideDots),
                HeightDots = Math.Max(
                    command.Int(1, ElementResizer.MinShapeSideDots),
                    ElementResizer.MinShapeSideDots),

                // Kept at what the file said rather than raised to what the preview can
                // draw: a printer prints a one-dot diagonal, so quietly thickening it
                // would change the label. The warning is what carries the difference.
                ThicknessDots = Math.Max(command.Int(2, 1), 1),
                IsWhite = string.Equals(command.Arg(3), "W", StringComparison.OrdinalIgnoreCase),
                LeansRight = !string.Equals(command.Arg(4), "L", StringComparison.OrdinalIgnoreCase),
            };

            if (diagonal.ThicknessDots < 2)
            {
                Warn("A one-dot diagonal line (^GD) prints, but the offline preview draws "
                     + "nothing for it. It is on the label and will come out; the canvas "
                     + "cannot show it.");
            }

            Add(diagonal);
            ResetField();
        }

        private void InlineImage(ZplCommand command)
        {
            // ^GFa,binaryBytes,graphicBytes,bytesPerRow,data
            int total = command.Int(2, command.Int(1));
            int bytesPerRow = command.Int(3);
            string data = command.Arguments.Length > 4
                ? command.Parameters[(IndexOfNth(command.Parameters, ',', 4) + 1)..]
                : string.Empty;

            AddImage(GraphicField.Decode(data, bytesPerRow, total), "Image", 1, 1);
            ResetField();
        }

        private void RecalledImage(ZplCommand command)
        {
            string name = ZplGraphicScanner.NormalizeName(command.Arg(0));
            ZplGraphicDefinition? definition = Graphics.Definitions
                .FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.Ordinal));

            if (definition is null)
            {
                Warn($"Graphic {name} is recalled but never downloaded in this file, "
                     + "so it lives in the printer's memory and could not be imported.");
                ResetField();
                return;
            }

            AddImage(
                GraphicField.Decode(definition.Data, definition.BytesPerRow, definition.TotalBytes),
                name,
                Math.Clamp(command.Int(1, 1), 1, 10),
                Math.Clamp(command.Int(2, 1), 1, 10));
            ResetField();
        }

        private void AddImage(GraphicBitmap? bitmap, string name, int magX, int magY)
        {
            if (bitmap is null)
            {
                Warn($"Graphic {name} could not be decoded and was dropped.");
                return;
            }

            (bool[] black, int width, int height) = Magnify(bitmap, magX, magY);
            Add(new ImageElement
            {
                Name = name,
                ImageData = MonochromePng.Encode(black, width, height),
                SourcePixelWidth = width,
                SourcePixelHeight = height,
                WidthDots = width,
                HeightDots = height,
                Dithering = DitherMode.Threshold,
            });
        }

        /// <summary>Dot replication, the way the printer magnifies a recalled graphic, so
        /// what comes back out is what the source label printed.</summary>
        private static (bool[] Black, int Width, int Height) Magnify(
            GraphicBitmap bitmap, int magX, int magY)
        {
            if (magX <= 1 && magY <= 1)
            {
                return (bitmap.Black, bitmap.Width, bitmap.Height);
            }

            int width = bitmap.Width * magX;
            int height = bitmap.Height * magY;
            var black = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                int source = y / magY * bitmap.Width;
                int target = y * width;
                for (int x = 0; x < width; x++)
                {
                    black[target + x] = bitmap.Black[source + x / magX];
                }
            }

            return (black, width, height);
        }

        private void Add(Element element)
        {
            element.Orientation = _orientation;
            element.IsReversed = _reversed;

            // A ^FT origin is the field's bottom left, and it is kept as one rather than
            // converted to a top-left ^FO. Which matters twice over: where a ^FT field
            // prints depends on how wide its content turns out to be, so a fixed ^FO
            // would freeze it at the width of the marker the label carries at design
            // time; and a field typeset by its baseline can legitimately extend up and
            // left of its own origin, which no ^FO can express.
            element.Anchor = _typeset ? FieldAnchor.Baseline : FieldAnchor.TopLeft;

            // ZPL has no negative origins, so a label home that pushes a field off the
            // top-left is clamped rather than producing a coordinate we cannot re-emit.
            element.X = Math.Max((_originX ?? 0) + _homeX, 0);
            element.Y = Math.Max((_originY ?? 0) + _homeY, 0);
            _current?.Elements.Add(element);
        }

        /// <summary>
        /// Applies ^FH hex escapes, skipping template markers.
        ///
        /// An escape names a byte in the code page ^CI selected, not a Unicode code
        /// point, and the two part company above 0x7F: 312.zpl writes "Minist_82rio",
        /// where 0x82 is code page 850's accented e, and reading the number as a code
        /// point yields a C1 control character instead. Consecutive escapes are decoded
        /// as one run because under ^CI28 a letter costs several bytes; a single-byte
        /// code page reads the same either way. See <see cref="ZplCodePage"/>.
        ///
        /// The exception is not a nicety, it is what real files require. '_' is both
        /// ZPL's default escape character and the commonest word separator in a field
        /// name, so in a field written "^FH^FD MA,##CODIGO_BARRAS##" the "_BA" is a
        /// perfectly valid escape for 0xBA and decoding it turns the marker into
        /// ##CODIGOºRRAS##. That label is not broken: the system that fills the marker in
        /// substitutes it long before a printer sees the field, so the escape never
        /// applies to it. Three separate corpus labels are mangled without this, one of
        /// them in five different places.
        ///
        /// The generator skips markers when escaping for the same reason, so the two
        /// sides stay symmetric and the round trip holds.
        /// </summary>
        private string Unescape(string data)
        {
            if (!_hexEscapes || data.Length == 0)
            {
                return data;
            }

            var sb = new System.Text.StringBuilder(data.Length);
            var run = new List<byte>();

            void FlushRun()
            {
                if (run.Count == 0)
                {
                    return;
                }

                if (!ZplCodePage.IsModelled(_internationalSet))
                {
                    Warn($"^CI{_internationalSet} selects a character encoding this reader "
                         + $"does not model; hex-escaped text was read as code page "
                         + $"{ZplCodePage.Default} and may show the wrong characters.");
                }

                sb.Append(ZplCodePage.Decode(run.ToArray(), _internationalSet));
                run.Clear();
            }

            foreach (TemplateSegment segment in TemplateScanner.Scan(data, markers))
            {
                // A marker is not data for the printer, so it ends any run of bytes.
                FlushRun();
                if (segment.Kind != TemplateSegmentKind.Literal)
                {
                    sb.Append(segment.Text);
                    continue;
                }

                string text = segment.Text;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == _hexMarker && i + 2 < text.Length &&
                        int.TryParse(text.AsSpan(i + 1, 2), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out int value))
                    {
                        run.Add((byte)value);
                        i += 2;
                    }
                    else
                    {
                        FlushRun();
                        sb.Append(text[i]);
                    }
                }
            }

            FlushRun();
            return sb.ToString();
        }

        private void ResetField()
        {
            _originX = null;
            _originY = null;
            _typeset = false;
            _orientation = Orientation.Normal;
            _haveFont = false;
            _font = ZplFont.Scalable;
            _fontHeight = 0;
            _fontWidth = 0;
            _symbology = null;
            _addCheckDigit = false;
            _dataMatrixModule = null;
            _qrMagnification = null;
            _pdf417 = null;
            _hexEscapes = false;
            _hexMarker = '_';
            _reversed = false;
            _blockWidth = 0;
            _blockMaxLines = 1;
            _blockSpacing = 0;
            _blockIndent = 0;
            _justification = TextJustification.Left;
        }

        private void Warn(string message)
        {
            if (_current is { } block && block.SeenWarnings.Add(message))
            {
                block.Warnings.Add(message);
            }
        }

        private static Orientation ToOrientation(string letter) =>
            letter.Length == 0 ? Orientation.Normal : char.ToUpperInvariant(letter[0]) switch
            {
                'R' => Orientation.Rotated90,
                'I' => Orientation.Rotated180,
                'B' => Orientation.Rotated270,
                _ => Orientation.Normal,
            };

        private static int IndexOfNth(string text, char value, int count)
        {
            int at = -1;
            for (int i = 0; i < count; i++)
            {
                at = text.IndexOf(value, at + 1);
                if (at < 0)
                {
                    return text.Length;
                }
            }

            return at;
        }
    }
}
