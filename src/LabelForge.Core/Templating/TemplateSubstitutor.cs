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

    public TemplateSubstitutor(Model.MarkerSyntax? syntax = null)
    {
        Model.MarkerSyntax resolved = syntax ?? Model.MarkerSyntax.Default;
        if (!resolved.IsValid)
        {
            throw new ArgumentException("Delimiters required.", nameof(syntax));
        }

        _open = resolved.Open;
        _close = resolved.Close;
    }

    /// <param name="resolve">
    /// Maps a marker's inner expression to its preview value. Return null to fall back to
    /// the default sample. If null, every variable uses <see cref="DefaultSampleValue"/>.
    /// </param>
    public string Substitute(string zpl, Func<string, string?>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(zpl);

        var sb = new StringBuilder(zpl.Length);
        foreach (TemplateSegment segment in TemplateScanner.Scan(zpl, _open, _close))
        {
            switch (segment.Kind)
            {
                case TemplateSegmentKind.Literal:
                    sb.Append(segment.Text);
                    break;

                case TemplateSegmentKind.Variable:
                    sb.Append(resolve?.Invoke(segment.Inner) ?? DefaultSampleValue);
                    break;

                // Internal directive (##@...##): not printable data, dropped for preview.
            }
        }

        return sb.ToString();
    }
}
