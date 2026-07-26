using System.Text;
using LabelForge.Core.Model;

namespace LabelForge.Core.Zpl;

/// <summary>Low-level ZPL text helpers: orientation letters and field-data escaping.</summary>
public static class ZplEncoding
{
    /// <summary>The ZPL orientation letter for an <see cref="Orientation"/>.</summary>
    public static string Letter(this Orientation orientation) => orientation switch
    {
        Orientation.Normal => "N",
        Orientation.Rotated90 => "R",
        Orientation.Rotated180 => "I",
        Orientation.Rotated270 => "B",
        _ => "N",
    };

    /// <summary>
    /// Builds a field-data segment, escaping the ZPL control characters '^', '~', and
    /// the field-hex escape character '_'. When any are present it emits the field
    /// hexadecimal indicator (^FH_) and hex-escapes those characters as _XX. Other
    /// characters (including UTF-8 text handled by ^CI28) pass through unchanged.
    ///
    /// Template markers are the exception, and they have to be: '_' is both ZPL's escape
    /// character and the commonest word separator in a field name, so escaping inside a
    /// marker turns ##CODIGO_BARRAS## into ##CODIGO_5FBARRAS## and the system that fills
    /// the marker in no longer recognises its own field. A marker is not data for the
    /// printer, it is a placeholder another system replaces first, so it travels through
    /// verbatim. The importer skips markers for the same reason and the two stay
    /// symmetric.
    /// </summary>
    /// <returns>Either "^FD{data}^FS" or "^FH_^FD{escaped}^FS".</returns>
    public static string FieldData(string data, Model.MarkerSyntax? markers = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        Model.MarkerSyntax syntax = markers ?? Model.MarkerSyntax.Default;

        Templating.TemplateSegment[] segments =
            Templating.TemplateScanner.Scan(data, syntax).ToArray();

        bool needsHex = segments.Any(
            s => s.Kind == Templating.TemplateSegmentKind.Literal && NeedsHex(s.Text));

        if (!needsHex)
        {
            return $"^FD{data}^FS";
        }

        var sb = new StringBuilder(data.Length + 8);
        sb.Append("^FH_^FD");
        foreach (Templating.TemplateSegment segment in segments)
        {
            if (segment.Kind != Templating.TemplateSegmentKind.Literal)
            {
                sb.Append(segment.Text);
                continue;
            }

            foreach (char c in segment.Text)
            {
                if (c is '^' or '~' or '_')
                {
                    sb.Append('_').Append(((int)c).ToString("X2"));
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        sb.Append("^FS");
        return sb.ToString();
    }

    private static bool NeedsHex(string text)
    {
        foreach (char c in text)
        {
            if (c is '^' or '~' or '_')
            {
                return true;
            }
        }

        return false;
    }
}
