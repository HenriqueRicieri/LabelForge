using System.Globalization;
using System.Text;
using LabelForge.Core.Model;
using LabelForge.Core.Templating;

namespace LabelForge.Core.Zpl;

/// <summary>The ZPL one field's data turned into, and how its dynamic parts are produced.</summary>
/// <param name="Zpl">The complete field-data segment, terminator included.</param>
/// <param name="UsesPrinterCounter">The field became a ^SN serialized field.</param>
/// <param name="UsesPrinterClock">The field carries ^FC clock placeholders.</param>
/// <param name="UsesSoftwareCounter">A counter value was written into the ZPL, which
/// means this label is only valid for one copy of the run.</param>
/// <param name="Warnings">User-facing reasons a requested printer-side feature was not
/// used. Empty when everything the user asked for was expressible.</param>
public readonly record struct FieldEncoding(
    string Zpl,
    bool UsesPrinterCounter,
    bool UsesPrinterClock,
    bool UsesSoftwareCounter,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Turns a field's text into ZPL, applying the document's variable definitions.
///
/// Three shapes come out of here. A counter the printer can serialize becomes ^SN and
/// the whole run stays one job. A date the printer can format becomes ^FC placeholders.
/// Everything else is expanded here and written into the ZPL as literal data. External
/// markers are never touched: they leave verbatim, which is what makes the output a
/// template rather than a finished label.
///
/// Falling back is normal, not a failure, but it is never silent: each fallback states
/// why, because "the printer will count for me" and "we counted for the printer" have
/// very different throughput.
///
/// Pure and deterministic given a copy index and a timestamp, so every branch can be
/// pinned by a golden test.
/// </summary>
public static class DynamicField
{
    /// <summary>Characters that would break ^SN's parameter syntax. ^SN takes its value
    /// as a bare parameter, so the ^FH hex escape that rescues ^FD data is unavailable.</summary>
    private const string SerialUnsafe = ",^~";

    private static readonly IReadOnlyList<string> NoWarnings = [];

    public static FieldEncoding Encode(
        string text, LabelDocument document, int copyIndex, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(document);

        // Nothing to resolve: the overwhelmingly common field, kept on the cheap path
        // and byte-identical to what the generator emitted before variables existed.
        if (document.Variables.Count == 0 ||
            !text.Contains(document.Markers.Open, StringComparison.Ordinal))
        {
            return new FieldEncoding(
                ZplEncoding.FieldData(text, document.Markers), false, false, false, NoWarnings);
        }

        List<TemplateSegment> segments = TemplateScanner.Scan(text, document.Markers).ToList();
        var warnings = new List<string>();

        if (TrySerialize(segments, document, copyIndex, warnings, out string serialized))
        {
            return new FieldEncoding(serialized, true, false, false, warnings);
        }

        return Expand(segments, document, copyIndex, now, warnings);
    }

    /// <summary>
    /// Decides whether the field can be handed to the printer's own serialization.
    /// ^SN replaces the field data entirely and advances the digits at the end of its
    /// value, so the field has to be static text followed by exactly one counter, and
    /// the resulting value has to survive ^SN's bare-parameter syntax.
    /// </summary>
    private static bool TrySerialize(
        List<TemplateSegment> segments,
        LabelDocument document,
        int copyIndex,
        List<string> warnings,
        out string zpl)
    {
        zpl = string.Empty;

        var native = segments
            .Where(s => s.Kind == TemplateSegmentKind.Variable)
            .Select(s => (Segment: s, Definition: VariableValues.Find(document, s.Inner)))
            .Where(t => t.Definition is { Kind: VariableKind.Counter, UsePrinterCounter: true })
            .ToList();
        if (native.Count == 0)
        {
            return false;
        }

        string name = document.Markers.NameOf(native[0].Segment.Inner);
        VariableDefinition definition = native[0].Definition!;

        int dynamicParts = segments.Count(s => s.Kind != TemplateSegmentKind.Literal);
        if (dynamicParts > 1)
        {
            warnings.Add(CounterFallback(
                name, "the printer serializes one field at a time and this field holds more than one marker"));
            return false;
        }

        if (segments[^1].Kind != TemplateSegmentKind.Variable)
        {
            warnings.Add(CounterFallback(
                name, "the printer advances the end of the field, so nothing may follow the marker"));
            return false;
        }

        // Everything before the marker is static text and rides along inside ^SN's value.
        string value = string.Concat(segments.Take(segments.Count - 1).Select(s => s.Text))
            + definition.FormatCounterAt(copyIndex);

        if (value.AsSpan().IndexOfAny(SerialUnsafe) >= 0)
        {
            warnings.Add(CounterFallback(name, "^SN cannot carry a comma, '^' or '~'"));
            return false;
        }

        int digits = TrailingDigits(value);
        if (digits == 0 || digits > VariableDefinition.MaxCounterDigits)
        {
            warnings.Add(CounterFallback(
                name,
                $"the printer serializes at most {VariableDefinition.MaxCounterDigits} digits at the end of the field"));
            return false;
        }

        // Padding is what the leading-zeros flag preserves; without it the number is
        // free to grow a digit and ^SN should not pad it.
        string leadingZeros = definition.CounterPadding > 0 ? "Y" : "N";
        zpl = $"^SN{value},{definition.CounterStep.ToString(CultureInfo.InvariantCulture)},{leadingZeros}^FS";
        return true;
    }

    /// <summary>Writes the field out with its markers resolved: counters become numbers,
    /// clocks become either ^FC placeholders or a formatted timestamp, and external
    /// markers stay exactly as the user typed them.</summary>
    private static FieldEncoding Expand(
        List<TemplateSegment> segments,
        LabelDocument document,
        int copyIndex,
        DateTime now,
        List<string> warnings)
    {
        bool wantsPrinterClock = segments.Any(s => IsPrinterClock(s, document));

        // A '%' already in the field would be read as a clock placeholder once ^FC is
        // in force, so build the software version first and check what it contains.
        string software = Build(segments, document, copyIndex, now, printerClock: false, out _);
        if (wantsPrinterClock && software.Contains(ZplClockFormat.Indicator, StringComparison.Ordinal))
        {
            foreach (string name in PrinterClockNames(segments, document))
            {
                warnings.Add(ClockFallback(
                    name, $"the field contains a '{ZplClockFormat.Indicator}', which the printer would read as a clock code"));
            }

            wantsPrinterClock = false;
        }

        if (!wantsPrinterClock)
        {
            return new FieldEncoding(
                ZplEncoding.FieldData(software, document.Markers),
                UsesPrinterCounter: false,
                UsesPrinterClock: false,
                UsesSoftwareCounter: UsesSoftwareCounter(segments, document),
                warnings);
        }

        string withClock = Build(segments, document, copyIndex, now, printerClock: true, out bool clockUsed);
        foreach (string message in ClockWarnings(segments, document))
        {
            warnings.Add(message);
        }

        string data = ZplEncoding.FieldData(withClock, document.Markers);
        return new FieldEncoding(
            clockUsed ? $"^FC{ZplClockFormat.Indicator}{data}" : data,
            UsesPrinterCounter: false,
            UsesPrinterClock: clockUsed,
            UsesSoftwareCounter: UsesSoftwareCounter(segments, document),
            warnings);
    }

    private static string Build(
        List<TemplateSegment> segments,
        LabelDocument document,
        int copyIndex,
        DateTime now,
        bool printerClock,
        out bool clockUsed)
    {
        clockUsed = false;
        var sb = new StringBuilder();
        foreach (TemplateSegment segment in segments)
        {
            if (segment.Kind != TemplateSegmentKind.Variable)
            {
                // Literals and Atak directives ride through untouched.
                sb.Append(segment.Text);
                continue;
            }

            VariableDefinition? definition = VariableValues.Find(document, segment.Inner);
            switch (definition?.Kind)
            {
                case VariableKind.Counter:
                    sb.Append(definition.FormatCounterAt(copyIndex));
                    break;

                case VariableKind.Clock:
                    if (printerClock && definition.UsePrinterClock &&
                        ZplClockFormat.TryTranslate(definition.ClockFormat, out string pattern))
                    {
                        sb.Append(pattern);
                        clockUsed = true;
                    }
                    else
                    {
                        sb.Append(definition.FormatClock(now));
                    }

                    break;

                default:
                    // External, or a marker with no definition at all: the template keeps it.
                    sb.Append(segment.Text);
                    break;
            }
        }

        return sb.ToString();
    }

    private static bool IsPrinterClock(TemplateSegment segment, LabelDocument document) =>
        segment.Kind == TemplateSegmentKind.Variable &&
        VariableValues.Find(document, segment.Inner) is { Kind: VariableKind.Clock, UsePrinterClock: true };

    private static IEnumerable<string> PrinterClockNames(
        List<TemplateSegment> segments, LabelDocument document) =>
        segments.Where(s => IsPrinterClock(s, document))
            .Select(s => document.Markers.NameOf(s.Inner))
            .Distinct(StringComparer.Ordinal);

    /// <summary>Printer-clock variables whose format has no ZPL equivalent, reported so
    /// the user knows the PC clock produced the value.</summary>
    private static IEnumerable<string> ClockWarnings(
        List<TemplateSegment> segments, LabelDocument document) =>
        segments.Where(s => IsPrinterClock(s, document))
            .Where(s => !ZplClockFormat.TryTranslate(
                VariableValues.Find(document, s.Inner)!.ClockFormat, out _))
            .Select(s => ClockFallback(
                document.Markers.NameOf(s.Inner),
                $"the printer clock cannot express \"{VariableValues.Find(document, s.Inner)!.ClockFormat}\""))
            .Distinct(StringComparer.Ordinal);

    private static bool UsesSoftwareCounter(List<TemplateSegment> segments, LabelDocument document) =>
        segments.Any(s => s.Kind == TemplateSegmentKind.Variable &&
                          VariableValues.Find(document, s.Inner) is { Kind: VariableKind.Counter });

    private static int TrailingDigits(string value)
    {
        int count = 0;
        for (int i = value.Length - 1; i >= 0 && char.IsAsciiDigit(value[i]); i--)
        {
            count++;
        }

        return count;
    }

    private static string CounterFallback(string name, string reason) =>
        $"Counter {name} is numbered by the PC (one job per label): {reason}.";

    private static string ClockFallback(string name, string reason) =>
        $"Date {name} uses the PC clock: {reason}.";
}
