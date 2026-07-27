using System.Reflection;
using SkiaSharp;

namespace LabelForge.Core.Rendering;

/// <summary>
/// The typeface the offline preview draws ZPL's scalable font 0 with.
///
/// It has to be pinned, and the reason is not tidiness. `^A0` names font 0 unambiguously,
/// but font 0 is a file that lives in the printer (`0.FNT`), so a preview on a PC has to
/// substitute something, and the engine picked that substitute from whatever the machine
/// happened to have installed. The two machines this was found on disagreed by 22 per cent:
/// "HELLO" at 40 dots came out 107 dots of ink on one and 131 on the other, because one
/// fell through to Segoe UI and the other to Arial.
///
/// That is not a cosmetic difference. <see cref="Model.TextMetrics"/> is measured from what
/// this renderer draws, and it decides the selection outline, the snap targets, how long a
/// continuous label measures, and whether a field is reported as running off the edge. A
/// table measured against one machine's fallback tells a different machine that text fits
/// when it does not.
///
/// **The font was chosen by measurement, against Labelary, which renders what a printer
/// prints.** Mean error over eight synthetic strings at 40 dots: Roboto Condensed 4.5 per
/// cent, TeX Gyre Heros Cn 9.4, Arial Narrow 9.5, Arial 15.5. Arial is the worst of the
/// candidates and was what one of the two machines was silently using. Roboto Condensed is
/// as close as the best fallback observed, is redistributable under the SIL Open Font
/// License, and is a condensed grotesque, which is the shape a Zebra's own font 0 is; the
/// engine's own preference list is condensed faces for exactly that reason.
/// </summary>
public static class PreviewFont
{
    private const string ResourceName =
        "LabelForge.Core.Rendering.Fonts.RobotoCondensed-Regular.ttf";

    /// <summary>ZPL's designator for the scalable font, as the engine asks for it.</summary>
    private const string ScalableDesignator = "0";

    private static readonly Lazy<SKTypeface?> Embedded = new(Load, isThreadSafe: true);

    /// <summary>
    /// Fallbacks for the bitmapped fonts A to H, which are deliberately left as the engine
    /// had them. Their metrics do not come from this renderer at all: the manual publishes
    /// a cell per font and <see cref="Model.ZplFont"/> takes them from there, because the
    /// engine draws five of the eight at the wrong width. So pinning them would buy a
    /// steadier picture and no correctness, at the price of bundling a second font.
    /// </summary>
    private static readonly string[] MonospaceStack =
        ["DejaVu Sans Mono", "Lucida Console", "Andale Mono", "Droid Sans Mono"];

    /// <summary>The pinned scalable typeface, or null if the resource could not be read.</summary>
    public static SKTypeface? Scalable => Embedded.Value;

    /// <summary>True when the preview is drawing font 0 with the pinned typeface rather
    /// than with whatever the machine happens to have. False means every text footprint
    /// is back to being machine-dependent, so it is worth surfacing rather than hiding.</summary>
    public static bool IsPinned => Embedded.Value is not null;

    /// <summary>
    /// Resolves a ZPL font designator to a typeface, which is what the engine's font loader
    /// hook asks for. It asks by designator ("0", "A", ...) rather than by family name, so
    /// nothing here depends on what any font is called or on what is installed.
    ///
    /// Never returns null: the engine fails the whole render if this declines, so an
    /// unresolvable name falls back to Skia's default rather than losing the label.
    /// </summary>
    public static SKTypeface Resolve(string designator)
    {
        if (string.Equals(designator, ScalableDesignator, StringComparison.Ordinal) &&
            Embedded.Value is { } pinned)
        {
            return pinned;
        }

        foreach (string family in MonospaceStack)
        {
            SKTypeface? match = SKFontManager.Default.MatchFamily(family);
            if (match is not null)
            {
                return match;
            }
        }

        return Embedded.Value ?? SKTypeface.Default;
    }

    private static SKTypeface? Load()
    {
        try
        {
            using Stream? stream = typeof(PreviewFont).Assembly
                .GetManifestResourceStream(ResourceName);

            // SKTypeface.FromStream takes ownership, so the stream is copied into memory
            // first: the caller's using block would otherwise close it under the typeface.
            if (stream is null)
            {
                return null;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            memory.Position = 0;
            return SKTypeface.FromStream(memory);
        }
        catch (Exception)
        {
            // A preview that falls back to a system font is worse than one that does not,
            // but it is far better than an app that cannot draw. IsPinned reports it.
            return null;
        }
    }

    /// <summary>Every embedded resource name, so a failure to find the font can say what is
    /// actually there. A font that silently stops being embedded looks exactly like one
    /// that is present, until somebody measures a label on another machine.</summary>
    public static IReadOnlyList<string> EmbeddedResourceNames() =>
        typeof(PreviewFont).Assembly.GetManifestResourceNames();
}
