using LabelForge.Core.Templating;

namespace LabelForge.Core.Model;

/// <summary>
/// The smallest label that holds a set of elements.
///
/// This exists for the file that does not say how big it is, which in the sample corpus
/// is 28 of 29 files: `^PW` and `^LL` are optional, and a label sent to a printer already
/// set up for its stock simply leaves them out. Falling back to a fixed default then is a
/// guess, and one that comes out too small does not merely look wrong - everything past
/// the edge is off the label, so it stops printing and leaves the generated ZPL. Ten of
/// those files draw past a 150 mm default and lost the difference.
///
/// Deliberately not <see cref="ContinuousLength"/>, which answers a similar-sounding
/// question for a different reason. That one measures what will print, so it asks
/// <see cref="ElementPlacement.IsPrintable"/>, which needs a label size to answer. Here
/// the size is what is being worked out, so every element counts whatever it would do on
/// a label it does not have yet.
/// </summary>
public static class LabelExtent
{
    /// <summary>
    /// How far right and down the content reaches, in millimetres rounded up to the next
    /// whole one, or null when there is nothing to measure. Whole millimetres because
    /// stock comes in them and a dot-exact size reads as a measurement artifact; rounding
    /// up also means nothing lands past the edge it was measured from.
    /// </summary>
    /// <param name="markers">Delimiters this document writes its markers with. A field
    /// carrying one is measured by its origin rather than by its footprint, because the
    /// footprint is the marker's and the marker is not what prints: 101.zpl writes
    /// `##PESO_LIQUIDO@0,000##` at 89 dots per character, which is two and a half times
    /// the width of the number that replaces it, and measuring that placeholder made the
    /// label three quarters longer than it is. An origin is solid ground by comparison,
    /// because a field whose origin is off the label prints nothing at all, so the author
    /// cannot have meant one to be there.</param>
    public static (double WidthMm, double HeightMm)? MeasureMm(
        IEnumerable<Element> elements, int dpmm, MarkerSyntax? markers = null)
    {
        ArgumentNullException.ThrowIfNull(elements);
        MarkerSyntax syntax = markers ?? MarkerSyntax.Default;

        var calculator = new ElementBoundsCalculator();
        var content = new ElementContent();
        int right = 0;
        int bottom = 0;
        bool any = false;

        foreach (Element element in elements)
        {
            any = true;
            if (HasMarker(content.Of(element), syntax))
            {
                right = Math.Max(right, element.X);
                bottom = Math.Max(bottom, element.Y);
                continue;
            }

            DotRect bounds = calculator.GetBounds(element);
            right = Math.Max(right, bounds.X + bounds.Width);
            bottom = Math.Max(bottom, bounds.Y + bounds.Height);
        }

        if (!any || right <= 0 || bottom <= 0)
        {
            return null;
        }

        int density = Math.Max(dpmm, 1);
        return (Math.Ceiling((double)right / density), Math.Ceiling((double)bottom / density));
    }

    private static bool HasMarker(string content, MarkerSyntax syntax) =>
        content.Length > 0 &&
        TemplateScanner.Scan(content, syntax)
            .Any(s => s.Kind != TemplateSegmentKind.Literal);

    /// <summary>An element's textual content, or empty for the ones that have none. A
    /// visitor rather than a type switch so a new element type cannot be forgotten.</summary>
    private sealed class ElementContent : IElementVisitor
    {
        private string _result = string.Empty;

        public string Of(Element element)
        {
            _result = string.Empty;
            element.Accept(this);
            return _result;
        }

        public void Visit(TextElement element) => _result = element.Text;

        public void Visit(BarcodeElement element) => _result = element.Data;

        public void Visit(QrCodeElement element) => _result = element.Data;

        public void Visit(DataMatrixElement element) => _result = element.Data;

        public void Visit(Pdf417Element element) => _result = element.Data;

        public void Visit(ImageElement element)
        {
            // Its size is its own, whatever the label's data says.
        }

        public void Visit(LineElement element)
        {
            // As above.
        }

        public void Visit(BoxElement element)
        {
            // As above.
        }
    }
}
