using LabelForge.Core.Model;

namespace LabelForge.Core.Templating;

/// <summary>
/// Finds the template variables (##NAME## and ##NAME@FUNCTION(args)## markers) used
/// by a document's elements, in encounter order. Directives (##@...##) are not
/// variables. Implemented as a visitor so a new element type with textual content
/// cannot be forgotten silently.
/// </summary>
public sealed class TemplateVariables : IElementVisitor
{
    private readonly string _open;
    private readonly string _close;
    private readonly List<string> _names = [];
    private readonly HashSet<string> _seen = [];

    public TemplateVariables(string open = "##", string close = "##")
    {
        _open = open;
        _close = close;
    }

    /// <summary>Distinct variable names in the order they first appear.</summary>
    public static IReadOnlyList<string> Discover(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var finder = new TemplateVariables();
        foreach (Element element in document.Elements)
        {
            element.Accept(finder);
        }

        return finder._names;
    }

    /// <summary>The variable name of a marker's inner expression: the part before
    /// the optional @FUNCTION suffix. Shared with sample-value resolution so the
    /// panel and the substitutor agree on what a marker is called.</summary>
    public static string NameOf(string innerExpression)
    {
        ArgumentNullException.ThrowIfNull(innerExpression);
        int at = innerExpression.IndexOf('@');
        return at < 0 ? innerExpression : innerExpression[..at];
    }

    public void Visit(TextElement element) => Scan(element.Text);

    public void Visit(BarcodeElement element) => Scan(element.Data);

    public void Visit(QrCodeElement element) => Scan(element.Data);

    public void Visit(DataMatrixElement element) => Scan(element.Data);

    public void Visit(Pdf417Element element) => Scan(element.Data);

    public void Visit(ImageElement element)
    {
        // No textual content.
    }

    public void Visit(LineElement element)
    {
        // No textual content.
    }

    public void Visit(BoxElement element)
    {
        // No textual content.
    }

    private void Scan(string content)
    {
        foreach (TemplateSegment segment in TemplateScanner.Scan(content, _open, _close))
        {
            if (segment.Kind != TemplateSegmentKind.Variable)
            {
                continue;
            }

            string name = NameOf(segment.Inner);
            if (name.Length > 0 && _seen.Add(name))
            {
                _names.Add(name);
            }
        }
    }
}
