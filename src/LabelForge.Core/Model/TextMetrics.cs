namespace LabelForge.Core.Model;

/// <summary>
/// How wide a string of the scalable font is.
///
/// Font 0 is proportional, so one average advance for every character is wrong by a lot
/// in both directions: measured against the renderer, an "i" costs 0.22 of the font size
/// and a "W" costs 0.77. A single constant tuned for capitals made a lowercase word
/// nearly half again too wide and "iiiii" almost three times too wide, which is what a
/// selection outline and every snap target were built on.
///
/// The table is measured rather than derived, character by character, by rendering one
/// glyph and then ten and taking a ninth of the difference, which cancels the run's own
/// side bearings and leaves the pitch a repeated glyph really costs.
///
/// It describes one specific typeface, and it can only be a fact because that typeface is
/// pinned. ZPL names font 0 unambiguously, but font 0 is a file inside the printer, so the
/// preview substitutes something, and it used to substitute whatever the machine had
/// installed: two machines drew the same label 22 per cent apart. The table would then have
/// been a description of one developer's font folder. <see cref="Rendering.PreviewFont"/>
/// pins it, and this table is measured against what it pins.
///
/// The typeface was chosen against Labelary, which renders what a printer prints, so the
/// preview is as close to the printed label as a redistributable font gets: 4.5 per cent
/// mean error over eight strings, against 15.5 for the Arial one machine had been falling
/// back to.
///
/// Labelary is still not the source for these numbers, and that split is deliberate. Its
/// own font 0 metrics do not survive inspection: it gives '%', '+', '-', '=' and '@' one
/// shared advance of 0.90 em and '&lt;' and '&gt;' another of 0.99, both wider than its own
/// 'W' at 0.81, which is what a renderer does when it has no metrics for a glyph rather
/// than what a typeface does. The canvas draws the offline render, so the table has to
/// describe that. For the bitmapped fonts, where the manual publishes the numbers and no
/// font file is involved, Labelary is exact and <see cref="ZplFont"/> follows it instead.
///
/// None of this reaches the printer: a field's width is never stated in the ZPL, only its
/// text. It decides the selection outline, the snap targets, how long a continuous label
/// is measured to be, and whether a field is reported as running off the edge.
/// </summary>
public static class TextMetrics
{
    /// <summary>Advance as a fraction of the font size, grouped by the value measured.</summary>
    private static readonly (double Ratio, string Characters)[] Measured =
    [
        (0.123, "'"),
        (0.197, ","),
        (0.203, ";"),
        (0.224, "j"),
        (0.230, " "),
        (0.231, "il"),
        (0.234, ":"),
        (0.236, "í"),
        (0.244, "|"),
        (0.246, "!"),
        (0.248, "-"),
        (0.250, "I[]Í"),
        (0.262, "/"),
        (0.264, "."),
        (0.268, "\""),
        (0.297, "t"),
        (0.304, "r"),
        (0.307, "{}"),
        (0.308, "`"),
        (0.314, "("),
        (0.317, "f"),
        (0.318, ")"),
        (0.369, "\\"),
        (0.371, "^"),
        (0.373, "°"),
        (0.397, "ª"),
        (0.404, "_º"),
        (0.416, "y"),
        (0.422, "?"),
        (0.428, "v"),
        (0.432, "*"),
        (0.439, "xz"),
        (0.446, "<"),
        (0.451, "k"),
        (0.457, "s"),
        (0.461, ">"),
        (0.463, "cç"),
        (0.469, "eéê"),
        (0.477, "L"),
        (0.478, "=aáàâãä"),
        (0.481, "F"),
        (0.482, "h"),
        (0.484, "Jnuúüñ"),
        (0.494, "$0123456789bgp"),
        (0.496, "d"),
        (0.498, "+EÉÊ"),
        (0.500, "q"),
        (0.502, "oóôõö"),
        (0.519, "S"),
        (0.523, "Z"),
        (0.529, "T"),
        (0.531, "R"),
        (0.535, "Y"),
        (0.545, "&K"),
        (0.547, "B"),
        (0.549, "#"),
        (0.551, "X"),
        (0.554, "P"),
        (0.561, "V"),
        (0.563, "UÚÜ"),
        (0.567, "CÇ"),
        (0.571, "D"),
        (0.576, "AÁÀÂÃÄ"),
        (0.590, "~"),
        (0.592, "G"),
        (0.602, "OQÓÔÕÖ"),
        (0.619, "NÑ"),
        (0.621, "H"),
        (0.633, "%"),
        (0.651, "w"),
        (0.754, "m"),
        (0.756, "M"),
        (0.762, "W"),
        (0.769, "@"),
    ];

    /// <summary>Anything the table does not name: accented letters and the rest of
    /// Unicode. Half the font size sits between a lowercase and a capital, and an accented
    /// letter is about as wide as the letter it decorates, so it is the right side of
    /// wrong for the characters this actually meets.</summary>
    public const double DefaultRatio = 0.5;

    private static readonly Dictionary<char, double> Ratios = Build();

    private static Dictionary<char, double> Build()
    {
        var map = new Dictionary<char, double>();
        foreach ((double ratio, string characters) in Measured)
        {
            foreach (char c in characters)
            {
                map[c] = ratio;
            }
        }

        return map;
    }

    /// <summary>The advance of one character, as a fraction of the font size.</summary>
    public static double Ratio(char character) =>
        Ratios.TryGetValue(character, out double ratio) ? ratio : DefaultRatio;

    /// <summary>
    /// How wide a text field is, in dots, whichever font draws it.
    ///
    /// The two kinds of font are measured differently because they are different things.
    /// The scalable font is proportional, so its width is the sum of the characters'
    /// advances; a bitmapped font is fixed pitch, so its width is a count of cells and
    /// the characters do not enter into it. See <see cref="ZplFont"/> for where those
    /// cells come from and how far the offline renderer can be trusted to draw them.
    /// </summary>
    public static int WidthDots(TextElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (ZplFont.Cell(element.Font, Dpmm203) is not { } cell)
        {
            return WidthDots(element.Text, element.FontHeightDots, element.FontWidthDots);
        }

        // A bitmapped font prints in whole multiples of its cell, so the requested size
        // is read back as the multiple the printer will actually use. The width follows
        // the height when no width was asked for, which is what ^A does.
        int requested = element.FontWidthDots > 0
            ? element.FontWidthDots
            : cell.WidthDots * ZplFont.Magnification(
                element.Font, element.FontHeightDots, vertical: true, Dpmm203);

        return ZplFont.WidthDots(
            element.Font,
            element.Text.Length,
            ZplFont.Magnification(element.Font, requested, vertical: false, Dpmm203));
    }

    /// <summary>The cells of every font but OCR-A and OCR-B are the same number of dots
    /// on every printhead, and those two are the ones a designer is least likely to
    /// resize; asking for the 203 dpi matrix keeps this a pure function of the element.
    /// See <see cref="ZplFont.Cell"/>, which takes the density for callers that have it.</summary>
    private const int Dpmm203 = 8;

    /// <summary>
    /// Width of a string in the scalable font, in dots.
    /// </summary>
    /// <param name="fontHeightDots">The ^A0 character height.</param>
    /// <param name="fontWidthDots">The ^A0 character width, or 0 to let it follow the
    /// height. Either way it scales every advance by the same factor: measured at widths
    /// of 20, 40 and 60 against a height of 40, each glyph kept its proportion exactly,
    /// so the font stretches rather than becoming fixed pitch.</param>
    public static int WidthDots(string text, int fontHeightDots, int fontWidthDots)
    {
        ArgumentNullException.ThrowIfNull(text);

        int em = fontWidthDots > 0 ? fontWidthDots : fontHeightDots;
        double advances = 0;
        foreach (char c in text)
        {
            advances += Ratio(c);
        }

        return (int)Math.Round(advances * em);
    }
}
