namespace LabelForge.Core.Zpl;

/// <summary>
/// How many symbols a Code 128 payload encodes to, honouring the escapes ZPL allows
/// inside field data.
///
/// Counting characters is wrong the moment a label uses them, and real labels do. Inside
/// `^FD`, "&gt;;" switches to subset C, where a *pair* of digits costs one symbol rather
/// than two, and "&gt;8" is a single FNC1. A GS1-128 payload opens with "&gt;;&gt;8" for
/// exactly that reason: written without it, the same data is nearly twice as wide and
/// will not fit the stock.
///
/// Measured against the rendered ink, exactly, for every case tried. The width in modules
/// is 11 per symbol plus the start and check symbols, plus a 13-module stop pattern.
/// </summary>
public static class Code128Encoding
{
    private enum Subset
    {
        A,
        B,
        C,
    }

    /// <summary>Modules across, from the symbol count: start, data, check and stop.</summary>
    public static int WidthModules(string data) => 11 * (CountSymbols(data) + 2) + 13;

    /// <summary>
    /// Data symbols the payload encodes to, not counting the start, check and stop.
    ///
    /// A payload with no escapes counts one symbol per character, which is what the
    /// printer does in `^BC`'s default mode: it does not hunt for digit runs to compress,
    /// so neither does this.
    /// </summary>
    public static int CountSymbols(string data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // ^BC starts in subset B unless the data opens by asking for another.
        var subset = Subset.B;
        int symbols = 0;
        int i = 0;

        while (i < data.Length)
        {
            if (data[i] == '>' && i + 1 < data.Length)
            {
                char code = data[i + 1];
                i += 2;

                switch (code)
                {
                    case ';':
                    case ':':
                    case '9':
                    {
                        Subset next = code switch
                        {
                            ';' => Subset.C,
                            ':' => Subset.B,
                            _ => Subset.A,
                        };

                        // A switch at the very start is the start code and costs nothing;
                        // anywhere else it is a symbol of its own.
                        if (symbols > 0 || i > 2)
                        {
                            symbols++;
                        }

                        subset = next;
                        continue;
                    }

                    // FNC1 and the other function characters are one symbol each, and so
                    // is an escaped literal '>'.
                    default:
                        symbols++;
                        continue;
                }
            }

            if (subset == Subset.C && char.IsAsciiDigit(data[i]))
            {
                // Subset C packs digits two at a time. A lone trailing digit cannot be
                // packed, so the encoder leaves the subset for it, which costs the switch
                // as well as the character.
                if (i + 1 < data.Length && char.IsAsciiDigit(data[i + 1]))
                {
                    symbols++;
                    i += 2;
                }
                else
                {
                    symbols += 2;
                    subset = Subset.B;
                    i++;
                }

                continue;
            }

            symbols++;
            i++;
        }

        return symbols;
    }
}
