using LabelForge.Core.Model;

namespace LabelForge.Core.Fields;

/// <param name="Fields">Field names the file described, empty when it was a script.</param>
/// <param name="Functions">Calls the file offered, empty when it was a field list.</param>
public sealed record FieldCatalogImport(
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<FieldFunction> Functions)
{
    /// <summary>
    /// Reads a file as whichever of the two kinds it is.
    ///
    /// The decision belongs here rather than at the point of import, because it cannot be
    /// made a line at a time. A field list and a line of source are not always
    /// distinguishable on their own: "break;" splits into one identifier and an empty
    /// column, and so does a field list row that ends in its delimiter. What does
    /// distinguish them is the file as a whole, since only a script offers callable
    /// signatures. So a file that offers any is a script and its lines are never read as
    /// field names.
    ///
    /// It also means one import can accept both without asking which was picked, which is
    /// the reason the distinction has to be reliable rather than merely usually right.
    /// </summary>
    public static FieldCatalogImport Read(string text, MarkerSyntax? syntax = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        IReadOnlyList<FieldFunction> functions = ScriptFunctionReader.Read(text);
        return functions.Count > 0
            ? new FieldCatalogImport([], functions)
            : new FieldCatalogImport(FieldListReader.Read(text, syntax), []);
    }

    public bool IsEmpty => Fields.Count == 0 && Functions.Count == 0;

    /// <summary>What was found, for a message that says which kind of file it turned out
    /// to be rather than reporting a count of the wrong thing.</summary>
    public string Describe() =>
        Functions.Count > 0
            ? Functions.Count == 1 ? "1 function" : $"{Functions.Count} functions"
            : Fields.Count == 1 ? "1 field" : $"{Fields.Count} fields";
}
