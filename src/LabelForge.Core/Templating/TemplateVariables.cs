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
    private readonly MarkerSyntax _syntax;
    private readonly List<string> _names = [];
    private readonly HashSet<string> _seen = [];

    public TemplateVariables(MarkerSyntax? syntax = null) =>
        _syntax = syntax ?? MarkerSyntax.Default;

    /// <summary>Distinct variable names in the order they first appear.</summary>
    public static IReadOnlyList<string> Discover(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var finder = new TemplateVariables(document.Markers);
        foreach (Element element in document.Elements)
        {
            element.Accept(finder);
        }

        return finder._names;
    }

    /// <summary>The variable name of a marker's inner expression, under the default
    /// syntax. Prefer <see cref="MarkerSyntax.NameOf"/> with the document's own syntax;
    /// this overload serves the callers that have no document in hand.</summary>
    public static string NameOf(string innerExpression) =>
        MarkerSyntax.Default.NameOf(innerExpression);

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

    public void Visit(EllipseElement element)
    {
        // No textual content.
    }

    public void Visit(DiagonalLineElement element)
    {
        // No textual content.
    }

    private void Scan(string content)
    {
        foreach (TemplateSegment segment in TemplateScanner.Scan(content, _syntax))
        {
            if (segment.Kind != TemplateSegmentKind.Variable)
            {
                continue;
            }

            string name = _syntax.NameOf(segment.Inner);
            if (name.Length > 0 && _seen.Add(name))
            {
                _names.Add(name);
            }
        }
    }
}
