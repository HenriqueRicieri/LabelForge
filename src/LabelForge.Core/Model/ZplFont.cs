namespace LabelForge.Core.Model;

/// <summary>
/// One built-in font's cell, from the ZPL manual's font matrices.
/// </summary>
/// <param name="HeightDots">The character cell's height at magnification 1.</param>
/// <param name="WidthDots">The cell's width. Fonts A to H are fixed pitch, so every
/// character occupies exactly this much plus the gap.</param>
/// <param name="IntercharacterGapDots">The blank between one cell and the next.</param>
/// <param name="BaselineDots">How far the baseline sits below the top of the cell, which
/// is what `^FT` places a field by.</param>
public readonly record struct FontCell(
    int HeightDots, int WidthDots, int IntercharacterGapDots, int BaselineDots);

/// <summary>
/// The printer's built-in fonts: the scalable font 0 and the bitmapped fonts A to H.
///
/// Every number here comes from the ZPL manual's font matrices (Table 29 for the
/// intercharacter gap and baseline, Tables 30 to 33 for the cell at each printhead
/// density) and every one was then confirmed against Labelary, which renders what a
/// printer prints. The two agree exactly: the measured advance per character is
/// `width + gap` for all eight fonts, and the measured baseline is the manual's number
/// for all eight. That is why this table is the model's authority rather than a
/// measurement of our own offline renderer.
///
/// The offline renderer is a different matter, and the difference is worth stating
/// plainly because it decides what the canvas can promise. It draws fonts A, C and D
/// exactly right, and is wrong about the other five: measured advances of 7.33 against 9
/// for B, 18.56 against 20 for E, 17.22 against 16 for F, 39.78 against 48 for G, and
/// 13.89 against 19 for H. So a label using those fonts prints correctly - the ZPL says
/// what the file said - while the preview is approximate, and the properties panel says
/// so rather than letting the canvas pretend.
///
/// Only E and H change with print density; the rest are the same number of dots on every
/// printhead, which is why a 9 dot cell is a smaller mark on a 300 dpi printer.
/// </summary>
public static class ZplFont
{
    /// <summary>The scalable font, and the only one whose size is free rather than a
    /// multiple of a fixed cell. It is the default because it is what nearly every real
    /// label uses: 1238 of the 1249 `^A` commands in the sample corpus.</summary>
    public const char Scalable = '0';

    /// <summary>What the panel offers, in the manual's own order.</summary>
    public static readonly IReadOnlyList<char> Supported =
        ['0', 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H'];

    /// <summary>ZPL's own cap: a bitmapped font prints at 1 to 10 times its cell.</summary>
    public const int MaxMagnification = 10;

    /// <summary>True for the one font whose height and width are free values in dots
    /// rather than multiples of a cell.</summary>
    public static bool IsScalable(char font) => font is Scalable;

    public static bool IsSupported(char font) => Supported.Contains(char.ToUpperInvariant(font));

    /// <summary>Whether the offline renderer draws this font at the width a printer
    /// does. Measured against Labelary: true for A, C and D, false for the rest.
    /// Callers use it to say the preview is approximate rather than to change what is
    /// generated, which is right either way.</summary>
    public static bool RendersFaithfully(char font) =>
        char.ToUpperInvariant(font) is Scalable or 'A' or 'C' or 'D';

    /// <summary>
    /// The cell for a bitmapped font at a given printhead density, or null for the
    /// scalable font and for anything not modelled (a downloaded font, say, which is in
    /// the printer rather than in the manual).
    /// </summary>
    public static FontCell? Cell(char font, int dpmm) => char.ToUpperInvariant(font) switch
    {
        // Same cell on every printhead.
        'A' => new FontCell(9, 5, 1, 7),
        'B' => new FontCell(11, 7, 2, 11),
        'C' or 'D' => new FontCell(18, 10, 2, 14),
        'F' => new FontCell(26, 13, 3, 21),
        'G' => new FontCell(60, 40, 8, 48),

        // OCR-B and OCR-A are the two that grow with the printhead. The manual states a
        // gap and a baseline once, against the 203 dpi cell, so the other densities take
        // them scaled by the same ratio as the cell rather than from a number nobody
        // published.
        'E' => Scale(new FontCell(28, 15, 5, 23), dpmm switch
        {
            <= 6 => (21, 10),
            <= 8 => (28, 15),
            _ => (42, 20),
        }),
        'H' => Scale(new FontCell(21, 13, 6, 21), dpmm switch
        {
            <= 6 => (17, 11),
            <= 8 => (21, 13),
            _ => (34, 22),
        }),

        _ => null,
    };

    /// <summary>
    /// How wide a run of characters is, in dots.
    ///
    /// Fonts A to H are fixed pitch, so this is arithmetic rather than a measurement: the
    /// manual says the gap between M and W is the same as between I and E, and Labelary
    /// agrees to the dot. The trailing gap is not counted, because it follows the last
    /// character rather than separating it from anything.
    /// </summary>
    public static int WidthDots(char font, int characters, int magnification)
    {
        if (Cell(font, 8) is not { } cell || characters <= 0)
        {
            return 0;
        }

        int scale = Math.Clamp(magnification, 1, MaxMagnification);
        return (characters * (cell.WidthDots + cell.IntercharacterGapDots) * scale)
               - (cell.IntercharacterGapDots * scale);
    }

    /// <summary>
    /// Which multiple of the cell a requested size in dots amounts to.
    ///
    /// ZPL takes `^A`'s height and width in dots even for a bitmapped font, but only
    /// prints whole multiples of the cell, 1 to 10. Reading the requested number back as
    /// a multiple is what lets the model keep storing dots - which is what round-trips
    /// through the file - while everything that measures the field agrees with what the
    /// printer will actually lay down.
    /// </summary>
    public static int Magnification(char font, int requestedDots, bool vertical, int dpmm)
    {
        if (Cell(font, dpmm) is not { } cell)
        {
            return 1;
        }

        int baseDots = vertical ? cell.HeightDots : cell.WidthDots;
        if (baseDots <= 0 || requestedDots <= 0)
        {
            return 1;
        }

        return Math.Clamp(requestedDots / baseDots, 1, MaxMagnification);
    }

    private static FontCell Scale(FontCell at203, (int Height, int Width) cell)
    {
        if (cell.Height == at203.HeightDots)
        {
            return at203;
        }

        double ratio = (double)cell.Height / at203.HeightDots;
        return new FontCell(
            cell.Height,
            cell.Width,
            (int)Math.Round(at203.IntercharacterGapDots * ratio),
            (int)Math.Round(at203.BaselineDots * ratio));
    }
}
