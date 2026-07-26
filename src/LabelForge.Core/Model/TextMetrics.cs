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
/// The table is measured rather than derived, character by character, from a run of ten
/// of each glyph so the run's own side bearings matter little and the pitch is what a
/// repeated glyph really costs. It describes the font the offline renderer draws with,
/// which is the right thing to match: the canvas shows that render, so an outline that
/// agreed with a printer's font instead would disagree with what is on screen.
/// </summary>
public static class TextMetrics
{
    /// <summary>Advance as a fraction of the font size, grouped by the value measured.</summary>
    private static readonly (double Ratio, string Characters)[] Measured =
    [
        (0.17, " "),
        (0.19, "'"),
        (0.22, "Iil.,"),
        (0.23, "j/"),
        (0.26, "():;!"),
        (0.27, "t-[]"),
        (0.28, "f"),
        (0.31, "r"),
        (0.32, "*"),
        (0.38, "\""),
        (0.40, "1"),
        (0.41, "z"),
        (0.45, "Jaceksvxy023456789#"),
        (0.46, "_"),
        (0.47, "+=<>"),
        (0.49, "FLbdghnpqu?"),
        (0.50, "TZo"),
        (0.54, "EPS"),
        (0.55, "VXY"),
        (0.58, "BCDHKNRU"),
        (0.59, "A&"),
        (0.63, "GOQ"),
        (0.64, "w"),
        (0.67, "M"),
        (0.72, "m%"),
        (0.77, "W"),
        (0.80, "@"),
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
    /// Width of a string in dots.
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
