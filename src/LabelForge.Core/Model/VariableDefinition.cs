using System.Globalization;

namespace LabelForge.Core.Model;

/// <summary>Where a template variable's value comes from.</summary>
public enum VariableKind
{
    /// <summary>Filled in downstream, outside LabelForge (the Atak default). The marker
    /// survives verbatim into exported and printed ZPL; only the preview substitutes a
    /// sample value so barcodes can be rendered.</summary>
    External,

    /// <summary>A serial number that advances once per printed copy.</summary>
    Counter,

    /// <summary>The current date and time, formatted when the label is produced.</summary>
    Clock,
}

/// <summary>
/// How one template variable (a ##NAME## marker) is filled. A variable with no entry
/// on the document is <see cref="VariableKind.External"/>, which is the historical
/// behaviour, so documents saved before counters existed keep working unchanged.
///
/// Counter and Clock variables are ours to expand. Each can be produced two ways:
/// in software (we compute every value and emit one label per copy) or by the printer
/// (^SN serialization and ^FC field clock), which is faster but only expressible for
/// fields that meet the ZPL syntax constraints. See LabelForge.Core.Zpl.DynamicField
/// for the eligibility rules and the fallback.
/// </summary>
public sealed class VariableDefinition
{
    /// <summary>The largest serial number ^SN accepts, and the ceiling we hold software
    /// counters to as well so both paths behave the same.</summary>
    public const int MaxCounterDigits = 12;

    public VariableKind Kind { get; set; } = VariableKind.External;

    /// <summary>First value of the run; never negative.</summary>
    public long CounterStart { get; set; } = 1;

    /// <summary>Added per copy. Negative counts down.</summary>
    public long CounterStep { get; set; } = 1;

    /// <summary>Minimum digits, left-padded with zeros. 0 prints the bare number.</summary>
    public int CounterPadding { get; set; } = 3;

    /// <summary>Prefer the printer's own serialization (^SN) over expanding every copy
    /// here. Only a hint: a field whose data cannot be expressed as ^SN falls back to
    /// software expansion and says why.</summary>
    public bool UsePrinterCounter { get; set; } = true;

    /// <summary>.NET date/time format used for the value we compute.</summary>
    public string ClockFormat { get; set; } = "dd/MM/yyyy";

    /// <summary>Prefer the printer's real time clock (^FC) over stamping the PC time
    /// into the ZPL. Only a hint, for the same reason as <see cref="UsePrinterCounter"/>,
    /// and it additionally requires a printer with the RTC option installed.</summary>
    public bool UsePrinterClock { get; set; }

    /// <summary>The counter value for a zero-based copy index, floored at 0: a run that
    /// would count below zero holds at zero rather than printing a negative serial.</summary>
    public long ValueAt(int copyIndex)
    {
        long value = CounterStart + (long)copyIndex * CounterStep;
        return value < 0 ? 0 : value;
    }

    /// <summary>The printed form of a counter value: left-padded to
    /// <see cref="CounterPadding"/> digits.</summary>
    public string FormatCounter(long value) =>
        value.ToString(CultureInfo.InvariantCulture)
            .PadLeft(Math.Clamp(CounterPadding, 0, MaxCounterDigits), '0');

    /// <summary>The printed form of the counter at a zero-based copy index.</summary>
    public string FormatCounterAt(int copyIndex) => FormatCounter(ValueAt(copyIndex));

    /// <summary>Fallback used when <see cref="ClockFormat"/> is empty or malformed, so a
    /// half-typed format prints a readable date instead of throwing mid-render.</summary>
    public const string FallbackClockFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>The clock value for a timestamp. Invariant culture keeps the output
    /// identical on every machine, matching the renderer's culture pinning; the format
    /// string itself is what decides how the date reads.</summary>
    public string FormatClock(DateTime now) =>
        TryFormatClock(ClockFormat, now, out string text)
            ? text
            : now.ToString(FallbackClockFormat, CultureInfo.InvariantCulture);

    /// <summary>Formats a timestamp with a user-typed format, reporting malformed input
    /// instead of throwing. A single-character format is treated as a custom specifier
    /// ("d" means day of month, not .NET's culture-dependent short date pattern).</summary>
    public static bool TryFormatClock(string format, DateTime now, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrEmpty(format))
        {
            return false;
        }

        try
        {
            text = now.ToString(
                format.Length == 1 ? "%" + format : format, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public VariableDefinition Clone() => new()
    {
        Kind = Kind,
        CounterStart = CounterStart,
        CounterStep = CounterStep,
        CounterPadding = CounterPadding,
        UsePrinterCounter = UsePrinterCounter,
        ClockFormat = ClockFormat,
        UsePrinterClock = UsePrinterClock,
    };
}
