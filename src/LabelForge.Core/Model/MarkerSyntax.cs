namespace LabelForge.Core.Model;

/// <summary>
/// How a template marker is written: what opens it, what closes it, and what separates
/// the field's name from whatever modifies it.
///
/// This is a property of the label rather than a constant in the code, because the
/// delimiters belong to whichever system fills the markers in. `##NAME##` is the default
/// only because it is what the first system LabelForge met happened to use; a label
/// destined for a system that writes `{{NAME}}` or `${NAME}` sets its own and everything
/// downstream follows, because every part of the app reads markers through one scanner.
///
/// What is deliberately NOT modelled is the grammar inside a marker. `@dd/MM/yyyy` is a
/// format to one system and `@Class.fn(ARG)` is a call to another; both are that system's
/// business. All this needs to know is where the name stops, so the modifier travels
/// through untouched as text.
/// </summary>
public sealed record MarkerSyntax(string Open, string Close, char ModifierSeparator)
{
    /// <summary>The syntax a new label starts with, and the one every label saved before
    /// this was a choice already used.</summary>
    public static MarkerSyntax Default { get; } = new("##", "##", '@');

    public bool IsValid => Open.Length > 0 && Close.Length > 0;

    /// <summary>Writes a marker for a field name, for completion and for tests.</summary>
    public string Marker(string name) => Open + name + Close;

    /// <summary>The field name inside a marker's expression: everything before the
    /// modifier separator. A marker that opens with the separator is a directive and has
    /// no field name at all.</summary>
    public string NameOf(string innerExpression)
    {
        ArgumentNullException.ThrowIfNull(innerExpression);
        int at = innerExpression.IndexOf(ModifierSeparator);
        return at < 0 ? innerExpression : innerExpression[..at];
    }
}
