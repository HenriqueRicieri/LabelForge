namespace LabelForge.Core.Templating;

public enum TemplateSegmentKind
{
    /// <summary>Plain text between markers.</summary>
    Literal,

    /// <summary>A ##NAME## or ##NAME@FUNCTION(args)## marker.</summary>
    Variable,

    /// <summary>A ##@DIRECTIVE(args)## marker: an instruction, not data.</summary>
    Directive,
}

/// <param name="Text">The raw text, including the delimiters for a marker.</param>
/// <param name="Inner">The expression between the delimiters; empty for a literal.</param>
public readonly record struct TemplateSegment(TemplateSegmentKind Kind, string Text, string Inner);

/// <summary>
/// Splits template text into literals, variable markers, and directives. Every part of
/// the app that has to understand markers reads them through this one scanner, so the
/// variables panel, the preview substitution, and the print-time field encoder can
/// never disagree about where a marker starts and ends.
///
/// An unterminated marker is not an error: the remainder is returned as a literal, so
/// half-typed content still renders instead of vanishing.
/// </summary>
public static class TemplateScanner
{
    public const string DefaultOpen = "##";
    public const string DefaultClose = "##";

    public static IEnumerable<TemplateSegment> Scan(
        string text, string open = DefaultOpen, string close = DefaultClose)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrEmpty(open)) throw new ArgumentException("Delimiter required.", nameof(open));
        if (string.IsNullOrEmpty(close)) throw new ArgumentException("Delimiter required.", nameof(close));

        int i = 0;
        while (i < text.Length)
        {
            int start = text.IndexOf(open, i, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            int contentStart = start + open.Length;
            int end = text.IndexOf(close, contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            if (start > i)
            {
                yield return new TemplateSegment(
                    TemplateSegmentKind.Literal, text[i..start], string.Empty);
            }

            string inner = text[contentStart..end];
            int stop = end + close.Length;
            yield return new TemplateSegment(
                inner.StartsWith('@') ? TemplateSegmentKind.Directive : TemplateSegmentKind.Variable,
                text[start..stop],
                inner);
            i = stop;
        }

        if (i < text.Length)
        {
            yield return new TemplateSegment(
                TemplateSegmentKind.Literal, text[i..], string.Empty);
        }
    }
}
