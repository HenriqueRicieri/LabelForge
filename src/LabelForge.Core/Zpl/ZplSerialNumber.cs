using System.Globalization;
using LabelForge.Core.Model;

namespace LabelForge.Core.Zpl;

/// <summary>A ^SN field taken apart: the text that stays put and the counter the printer
/// advances inside it.</summary>
/// <param name="Prefix">Everything before the indexed digits. Printed unchanged on every
/// copy, so it belongs to the field's text rather than to the counter.</param>
/// <param name="Suffix">Everything after them, which is only ever non-digits: the printer
/// scans back past a trailing alpha run to find the number it advances.</param>
/// <param name="Counter">The counter the digits describe, or null when the value holds no
/// digits at all and there is nothing for the printer to index.</param>
public readonly record struct SerialNumberField(
    string Prefix, string Suffix, VariableDefinition? Counter);

/// <summary>
/// Reads ^SN, the printer's own serialization, back into a counter variable. The writing
/// half is <see cref="DynamicField"/>, and the two are deliberately kept as a pair: what
/// the generator emits for a counter is exactly what this reads, so a label carrying one
/// survives a round trip through the ZPL.
///
/// ^SN is field data rather than a modifier of it. It stands where ^FD would and carries
/// the value itself, which is why a ^SN field imported without this produces no element at
/// all rather than an unnumbered one.
///
/// Which part of the value is the number is the manual's rule, not a guess: scanning from
/// the end, the first digit found ends the indexed run and the run continues left through
/// consecutive digits, with at most the right-most 12 subject to indexing. So the trailing
/// alpha characters of "LOT001AB" stay put and "001" is what advances.
/// </summary>
public static class ZplSerialNumber
{
    /// <summary>ZPL's own defaults for ^SNv,n,z, which an imported label has to keep
    /// printing what it printed: a starting value of 1, a step of 1, and no padding.</summary>
    private const string DefaultValue = "1";

    public static SerialNumberField Read(ZplCommand command)
    {
        string value = command.Arg(0);
        if (value.Length == 0)
        {
            value = DefaultValue;
        }

        long step = long.TryParse(
            command.Arg(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
            out long parsed) && parsed != 0
            ? parsed
            : 1;

        // "Add leading zeros" is what decides whether the width is fixed. Without it the
        // number is free to grow a digit, which is what a padding of 0 means here.
        bool leadingZeros = string.Equals(command.Arg(2), "Y", StringComparison.OrdinalIgnoreCase);

        int end = value.Length;
        while (end > 0 && !char.IsAsciiDigit(value[end - 1]))
        {
            end--;
        }

        if (end == 0)
        {
            // A value with no digits anywhere: the printer prints it unchanged on every
            // copy, so it is ordinary field data and there is no counter to model.
            return new SerialNumberField(value, string.Empty, null);
        }

        int start = end;
        while (start > 0 && char.IsAsciiDigit(value[start - 1]))
        {
            start--;
        }

        // Only the right-most 12 digits are indexed; anything longer is literal text in
        // front of them, and reading it as part of the number would carry digits the
        // printer never touches.
        start = Math.Max(start, end - VariableDefinition.MaxCounterDigits);

        string digits = value[start..end];
        return new SerialNumberField(
            value[..start],
            value[end..],
            new VariableDefinition
            {
                Kind = VariableKind.Counter,
                CounterStart = long.Parse(digits, CultureInfo.InvariantCulture),
                CounterStep = step,
                CounterPadding = leadingZeros ? digits.Length : 0,

                // It arrived as the printer's own serialization, so that is what it goes
                // back out as wherever the field still allows it.
                UsePrinterCounter = true,
            });
    }
}
