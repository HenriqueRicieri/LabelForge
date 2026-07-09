using System.Text;

namespace LabelForge.Core.Templating;

/// <summary>
/// Replaces Atak template markers with preview values so a label can be rendered.
///
/// Atak labels are templates. A field such as ^FD##CODIGO_BARRAS##^FS carries a
/// variable, not final data. Text fields render such markers as literal text, but a
/// linear barcode cannot encode "##...##", so the live preview substitutes a sample
/// value first. The saved ZPL is never modified; substitution happens only on the
/// copy handed to the renderer.
///
/// Marker grammar (delimiters are configurable; defaults are "##"):
///   ##NAME##                      a variable, resolved to a preview value
///   ##NAME@FUNCTION(args)##       a variable with a function; resolved by NAME
///   ##@DIRECTIVE(args)##          an internal directive (e.g. PRINT_REGION); removed for preview
///
/// The value resolver is user-overridable: callers pass their own map so a user can
/// set what any variable resolves to, falling back to a sensible default sample.
/// </summary>
public sealed class TemplateSubstitutor
{
    private readonly string _open;
    private readonly string _close;

    /// <summary>A numeric default that satisfies both text fields and numeric barcodes
    /// (Code128 and EAN/UPC accept it; EAN computes its own check digit from 12 digits).</summary>
    public const string DefaultSampleValue = "123456789012";

    public TemplateSubstitutor(string open = "##", string close = "##")
    {
        if (string.IsNullOrEmpty(open)) throw new ArgumentException("Delimiter required.", nameof(open));
        if (string.IsNullOrEmpty(close)) throw new ArgumentException("Delimiter required.", nameof(close));
        _open = open;
        _close = close;
    }

    /// <param name="resolve">
    /// Maps a marker's inner expression to its preview value. Return null to fall back to
    /// the default sample. If null, every variable uses <see cref="DefaultSampleValue"/>.
    /// </param>
    public string Substitute(string zpl, Func<string, string?>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(zpl);

        var sb = new StringBuilder(zpl.Length);
        int i = 0;
        while (i < zpl.Length)
        {
            int start = zpl.IndexOf(_open, i, StringComparison.Ordinal);
            if (start < 0)
            {
                sb.Append(zpl, i, zpl.Length - i);
                break;
            }

            int contentStart = start + _open.Length;
            int end = zpl.IndexOf(_close, contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                // Unterminated marker; leave the remainder untouched.
                sb.Append(zpl, i, zpl.Length - i);
                break;
            }

            sb.Append(zpl, i, start - i);
            string inner = zpl.Substring(contentStart, end - contentStart);

            if (inner.StartsWith('@'))
            {
                // Internal directive (##@...##): not printable data, drop it for preview.
            }
            else
            {
                sb.Append(resolve?.Invoke(inner) ?? DefaultSampleValue);
            }

            i = end + _close.Length;
        }

        return sb.ToString();
    }
}
