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
/// It describes the font the offline renderer draws with, and that is a choice rather
/// than a convenience, because Labelary draws the same font differently. Two things
/// decided it. The canvas shows the offline render, so an outline agreeing with something
/// else would visibly not fit the text under it. And Labelary's own numbers do not survive
/// inspection for this font: it gives '%', '+', '-', '=' and '@' one shared advance of
/// 0.90 em and '&lt;' and '&gt;' another of 0.99, both wider than its own 'W' at 0.81,
/// which is what a renderer does when it has no metrics for a glyph rather than what a
/// typeface does. For the bitmapped fonts, where the manual publishes the numbers and no
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
        (0.194, "'"),
        (0.228, "|"),
        (0.231, " ,./I\\ijlÍí"),
        (0.275, "!()-:;[]`ft"),
        (0.300, "º"),
        (0.303, "ª"),
        (0.319, "*r{}"),
        (0.389, "\""),
        (0.400, "°"),
        (0.408, "z"),
        (0.411, "1"),
        (0.456, "#$023456789J_aceksvxyáàâãäçéê"),
        (0.481, "+<=>"),
        (0.503, "?FLTZbdghnopquóôõöúüñ"),
        (0.547, "EPSVXYÉÊ"),
        (0.592, "&ABCDHKNRUÁÀÂÃÄÇÚÜÑ"),
        (0.639, "GOQwÓÔÕÖ"),
        (0.683, "M"),
        (0.731, "%m"),
        (0.775, "W"),
        (0.803, "@"),
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
