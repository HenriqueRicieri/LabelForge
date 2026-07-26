using LabelForge.Core.Model;

namespace LabelForge.Core.Templating;

/// <summary>
/// Resolves what a ##marker## is worth. Single place for the lookup so the designer
/// preview, the exported ZPL, and the print job cannot disagree about a counter's
/// value or the format of a date.
/// </summary>
public static class VariableValues
{
    /// <summary>The definition for a marker's inner expression, or null when the
    /// variable has none and is therefore a plain external marker.</summary>
    public static VariableDefinition? Find(LabelDocument document, string innerExpression)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(innerExpression);
        return document.Variables.TryGetValue(
            document.Markers.NameOf(innerExpression), out VariableDefinition? definition)
            ? definition
            : null;
    }

    /// <summary>
    /// The value the designer preview shows for a marker. The canvas renders a single
    /// label, so a counter shows its first copy and a clock shows the current time,
    /// formatted here even when the printer would do it, because the offline renderer
    /// has no real time clock. External markers resolve to the user's sample.
    /// </summary>
    /// <returns>Null when nothing better than the default sample is available.</returns>
    public static string? ForPreview(LabelDocument document, string innerExpression, DateTime now)
    {
        VariableDefinition? definition = Find(document, innerExpression);
        if (definition is { Kind: VariableKind.Counter })
        {
            return definition.FormatCounterAt(0);
        }

        if (definition is { Kind: VariableKind.Clock })
        {
            return definition.FormatClock(now);
        }

        string name = document.Markers.NameOf(innerExpression);
        return document.SampleValues.TryGetValue(name, out string? sample) && sample.Length > 0
            ? sample
            : null;
    }

    /// <summary>True when the document expands any variable itself, which is what makes
    /// a print run more than one identical label.</summary>
    public static bool HasManagedVariables(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Variables.Values.Any(v => v.Kind != VariableKind.External);
    }
}
