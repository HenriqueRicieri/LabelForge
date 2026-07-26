namespace LabelForge.Core.Fields;

/// <param name="Name">The field's name, without the marker delimiters.</param>
/// <param name="Type">What the data source says the value is, verbatim and unparsed
/// ("String", "Nullable&lt;DateTime&gt;"). Free text on purpose: it comes from whatever
/// system produced the list, and guessing a type system for it would be inventing.</param>
/// <param name="Origin">Where the value comes from, if the source says so.</param>
/// <param name="IsList">True when the type reads as a collection, which is the one thing
/// worth deriving: a list field is written with an index in the marker.</param>
public sealed record FieldDefinition(
    string Name,
    string Type = "",
    string Origin = "",
    bool IsList = false)
{
    /// <summary>What the picker shows: the name, then whatever the source knew about it.</summary>
    public string Describe()
    {
        string detail = string.Join(
            " - ", new[] { Type, Origin }.Where(p => p.Length > 0));
        return detail.Length > 0 ? $"{Name}  ({detail})" : Name;
    }

    public override string ToString() => Name;
}

/// <summary>
/// A named list of the fields a label's data source provides.
///
/// This is data, never code. LabelForge does not know what any of these fields mean, and
/// nothing about a particular system is compiled in: a catalog is imported from a file,
/// named by the user, and the name is the user's own vocabulary ("Etiqueta externa"),
/// not the exporting file's. That is what keeps the app usable for a second system
/// without touching it.
///
/// A catalog never restricts what can be typed. It ranks what is offered and names what
/// is not recognised, because a field list is a good description of a data source and a
/// poor law: a list one version out of date must not stop someone finishing a label.
/// </summary>
public sealed record FieldCatalog(string Name, IReadOnlyList<FieldDefinition> Fields)
{
    public static FieldCatalog Empty { get; } = new(string.Empty, []);

    public bool Contains(string fieldName) =>
        Fields.Any(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));

    public FieldDefinition? Find(string fieldName) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));

    public override string ToString() =>
        Fields.Count == 1 ? $"{Name} (1 field)" : $"{Name} ({Fields.Count} fields)";
}
