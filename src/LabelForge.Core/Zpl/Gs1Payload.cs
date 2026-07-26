using System.Text;

namespace LabelForge.Core.Zpl;

/// <param name="Code">The application identifier.</param>
/// <param name="Value">Its value, exactly as it will be encoded.</param>
public sealed record Gs1Field(string Code, string Value);

/// <param name="Fields">The identifier and value pairs the payload carries.</param>
/// <param name="Problems">What is wrong with it, in encounter order, or empty.</param>
public sealed record Gs1Reading(IReadOnlyList<Gs1Field> Fields, IReadOnlyList<string> Problems);

/// <summary>
/// Builds and reads back the data half of a GS1-128 barcode.
///
/// A GS1 payload is a run of application identifiers and their values with no separators
/// between them, which only works because the reader knows how long each fixed-length
/// value is. A variable-length value has to be terminated, and the terminator is FNC1.
/// Getting that wrong does not make the barcode fail to scan: it makes it scan as one
/// long field with the wrong value, which is a far worse way for a label to be broken,
/// and it is the whole reason for assembling this rather than typing it.
///
/// The ZPL side of it is two escapes, both measured against the renderer. "&gt;;" puts
/// Code 128 into subset C, where a pair of digits costs one symbol instead of two, and
/// "&gt;8" is FNC1. A payload written without the subset switch is nearly twice as wide,
/// which is why the real labels open with "&gt;;&gt;8".
/// </summary>
public static class Gs1Payload
{
    /// <summary>ZPL's escape for FNC1 inside Code 128 field data.</summary>
    public const string Fnc1 = ">8";

    /// <summary>ZPL's escape for the Code 128 subset that packs digit pairs.</summary>
    public const string SubsetC = ">;";

    /// <summary>ZPL's escape back to the subset that carries letters.</summary>
    public const string SubsetB = ">:";

    /// <summary>
    /// Assembles the `^FD` data for a GS1-128 barcode.
    ///
    /// A separator goes after a variable-length value only when something follows it,
    /// because a trailing one encodes a symbol that buys nothing. The subset is switched
    /// as the content requires: digits pack two to a symbol, letters cannot, so a payload
    /// that mixes them moves between the two rather than paying subset B for everything.
    /// </summary>
    public static string Build(IEnumerable<Gs1Field> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        Gs1Field[] present = fields.Where(f => !string.IsNullOrEmpty(f.Code)).ToArray();
        if (present.Length == 0)
        {
            return string.Empty;
        }

        // Laid out as tokens first, where null is a separator. FNC1 belongs to no subset,
        // so deciding the subset per token rather than per field keeps the switching in
        // one place instead of spread through the assembly.
        var tokens = new List<string?> { null };
        for (int i = 0; i < present.Length; i++)
        {
            tokens.Add(present[i].Code);
            if (present[i].Value.Length > 0)
            {
                tokens.Add(present[i].Value);
            }

            // A separator goes after a variable-length value only when something follows
            // it; a trailing one encodes a symbol that buys nothing.
            bool variable = Gs1Catalog.Find(present[i].Code)?.IsVariableLength ?? true;
            if (variable && i < present.Length - 1)
            {
                tokens.Add(null);
            }
        }

        // Subset B is where Code 128 starts, so only subset C has to be asked for.
        bool wantsC = Packs(tokens.First(t => t is not null)!);
        var sb = new StringBuilder();
        if (wantsC)
        {
            sb.Append(SubsetC);
        }

        bool inSubsetC = wantsC;
        foreach (string? token in tokens)
        {
            if (token is null)
            {
                sb.Append(Fnc1);
                continue;
            }

            bool packs = Packs(token);
            if (packs != inSubsetC)
            {
                sb.Append(packs ? SubsetC : SubsetB);
                inSubsetC = packs;
            }

            sb.Append(token);
        }

        return sb.ToString();
    }

    /// <summary>True when a run is worth carrying in the subset that packs digit pairs.
    /// A single character never is: the switch would cost as much as it saves.</summary>
    private static bool Packs(string text) =>
        text.Length >= 2 && text.All(char.IsAsciiDigit);

    /// <summary>
    /// Reads a payload back into its fields, so one assembled elsewhere can be checked
    /// and shown in the bracketed form a person reads.
    ///
    /// It reports rather than refuses. A payload can use an identifier this does not know
    /// or carry a template marker instead of a value, and neither is a reason to hand
    /// back nothing.
    /// </summary>
    public static Gs1Reading Read(string data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var fields = new List<Gs1Field>();
        var problems = new List<string>();
        string text = StripEscapes(data, out bool separated);

        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '')
            {
                i++;
                continue;
            }

            // Identifiers are two, three or four digits; the shortest match that names a
            // known one wins, and an unknown one is assumed to be the usual two.
            Gs1ApplicationIdentifier? ai = null;
            int codeLength = 0;
            for (int length = 2; length <= 4 && i + length <= text.Length; length++)
            {
                if (Gs1Catalog.Find(text[i..(i + length)]) is { } found)
                {
                    ai = found;
                    codeLength = length;
                    break;
                }
            }

            if (ai is null)
            {
                codeLength = Math.Min(2, text.Length - i);
                problems.Add($"({text[i..(i + codeLength)]}) is not an identifier this build knows.");
            }

            string code = text[i..(i + codeLength)];
            i += codeLength;

            int end = ai is { IsVariableLength: false }
                ? EndOfFixedValue(text, i, ai.Length)
                : NextSeparator(text, i);

            string value = text[i..end];
            bool templated = value.Contains('#', StringComparison.Ordinal);
            fields.Add(new Gs1Field(code, value));
            i = end;

            // A marker stands in for a value whose length is only known once the
            // filling system substitutes it, so neither check can be applied to one.
            if (!templated)
            {
                if (ai is { IsVariableLength: false } fixedAi && value.Length != fixedAi.Length)
                {
                    problems.Add(
                        $"({code}) should carry {fixedAi.Length} characters and carries {value.Length}.");
                }

                if (ai is { NumericOnly: true } && !value.All(char.IsAsciiDigit))
                {
                    problems.Add($"({code}) should be digits only.");
                }
            }
        }

        // A variable-length field in the middle with nothing to end it swallows whatever
        // follows, which scans as one wrong value rather than as a failure.
        for (int f = 0; f < fields.Count - 1; f++)
        {
            if ((Gs1Catalog.Find(fields[f].Code)?.IsVariableLength ?? false) && !separated)
            {
                problems.Add(
                    $"({fields[f].Code}) has no fixed length and nothing separates it from what "
                    + "follows, so a scanner reads them as one value.");
                break;
            }
        }

        return new Gs1Reading(fields, problems);
    }

    /// <summary>The bracketed form a person reads, which is also what belongs under the
    /// bars when a GS1-128 prints its interpretation line.</summary>
    public static string Describe(string data) =>
        string.Concat(Read(data).Fields.Select(f => $"({f.Code}){f.Value}"));

    /// <summary>True when the data looks like a GS1 payload rather than plain content:
    /// it opens with FNC1, which is what makes a Code 128 a GS1-128 at all.</summary>
    public static bool IsGs1(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.Contains(Fnc1, StringComparison.Ordinal) ||
               data.Contains('', StringComparison.Ordinal);
    }

    /// <summary>Removes the ZPL escapes, leaving the characters a scanner would read.
    /// Separators become the group separator a scanner reports, so the reader can tell a
    /// terminated variable-length field from one that runs on.</summary>
    private static string StripEscapes(string data, out bool separated)
    {
        var sb = new StringBuilder(data.Length);
        separated = false;

        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != '>' || i + 1 >= data.Length)
            {
                sb.Append(data[i]);
                continue;
            }

            switch (data[i + 1])
            {
                case '8':
                    // The opening FNC1 marks the payload; a later one separates fields.
                    if (sb.Length > 0)
                    {
                        sb.Append('');
                        separated = true;
                    }

                    i++;
                    break;

                case ';':
                case ':':
                case '9':
                    i++;
                    break;

                case '0':
                    sb.Append('>');
                    i++;
                    break;

                default:
                    sb.Append(data[i]);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Where a fixed-length value ends.
    ///
    /// Normally that is simply its stated length, but a designed label carries markers
    /// rather than values, and a marker is not the length of what will replace it. Slicing
    /// at the stated length would cut one in half and read the remainder as the next
    /// identifier, so a marker that begins inside the value carries it to its own end
    /// instead.
    /// </summary>
    private static int EndOfFixedValue(string text, int from, int length)
    {
        int end = Math.Min(from + length, text.Length);
        int marker = text.IndexOf("##", from, StringComparison.Ordinal);
        if (marker < 0 || marker >= end)
        {
            return end;
        }

        int close = text.IndexOf("##", marker + 2, StringComparison.Ordinal);
        return close < 0 ? end : close + 2;
    }

    private static int NextSeparator(string text, int from)
    {
        int at = text.IndexOf('', from);
        return at < 0 ? text.Length : at;
    }
}
