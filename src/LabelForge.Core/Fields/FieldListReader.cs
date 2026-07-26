using System.Text.RegularExpressions;
using LabelForge.Core.Model;

namespace LabelForge.Core.Fields;

/// <summary>
/// Reads a field list exported from whatever system will fill the markers in.
///
/// Deliberately tolerant rather than schema-driven, and the sample exports are why: three
/// files from one system had three different shapes. One was bare `##NAME##` and a tab,
/// one prefixed a "- " bullet and added `Tipo: String`, one added `Origem: tbFilial.nome`
/// on top of that. All three were named .csv and none was comma separated. A parser with
/// a fixed column layout would have read one of them and would break again on the next
/// export.
///
/// So the rule is: a line contributes a field if a marker can be found in it, and any
/// labelled `Key: value` pairs after it are kept as description. Everything else about
/// the line is ignored, including how it is delimited. Lines with no marker are skipped
/// rather than reported, because export files carry headers and notes and a warning list
/// full of those would not be read.
/// </summary>
public static partial class FieldListReader
{
    /// <summary>Matches "Tipo: String", "Origem: tbFilial.nome", "Type: int" and so on:
    /// a word, a colon, and the rest up to the next tab or the end.</summary>
    [GeneratedRegex(@"(?<key>[\p{L}][\p{L}\p{Nd}_ ]*)\s*:\s*(?<value>[^\t]*)", RegexOptions.CultureInvariant)]
    private static partial Regex LabelledPair();

    /// <summary>Type names that mean "many of these", so the marker takes an index. The
    /// check is on the shape of the text, not on a type system we would have to own.</summary>
    private static bool LooksLikeAList(string type) =>
        type.Contains("List<", StringComparison.OrdinalIgnoreCase) ||
        type.Contains("[]", StringComparison.Ordinal) ||
        type.Contains("IEnumerable<", StringComparison.OrdinalIgnoreCase) ||
        type.Contains("Collection<", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses field definitions out of an exported list.
    /// </summary>
    /// <param name="text">The file's contents.</param>
    /// <param name="syntax">Delimiters the export writes its markers with. The exports
    /// carry full markers rather than bare names, so they have to be recognised; a file
    /// of bare names still reads, because a line with no marker falls back to its first
    /// column.</param>
    public static IReadOnlyList<FieldDefinition> Read(string text, MarkerSyntax? syntax = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        MarkerSyntax markers = syntax ?? MarkerSyntax.Default;

        var fields = new List<FieldDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim('\r', ' ', '\t');
            if (line.Length == 0)
            {
                continue;
            }

            if (ReadLine(line, markers) is not { } field || field.Name.Length == 0)
            {
                continue;
            }

            // First definition wins: an export that lists a field twice describes one
            // field, and the later line is not more authoritative than the earlier.
            if (seen.Add(field.Name))
            {
                fields.Add(field);
            }
        }

        return fields;
    }

    private static FieldDefinition? ReadLine(string line, MarkerSyntax markers)
    {
        int open = line.IndexOf(markers.Open, StringComparison.Ordinal);
        int close = open < 0
            ? -1
            : line.IndexOf(markers.Close, open + markers.Open.Length, StringComparison.Ordinal);

        string name;
        int after;
        if (close > 0)
        {
            name = line[(open + markers.Open.Length)..close].Trim();
            after = close + markers.Close.Length;
        }
        else
        {
            // No marker on the line: treat the first column as a bare field name, which
            // is what a plainer export looks like. Anything with spaces is prose, not a
            // field, so it is left alone.
            string[] columns = line.Split('\t', ';', ',');
            name = columns[0].Trim().TrimStart('-', '*', ' ').Trim();

            // Only something shaped like an identifier. Prose has spaces and a line of
            // source has punctuation; neither is a field name, and reading them as one
            // would fill a catalog with rubbish that then matches nothing.
            if (name.Length == 0 ||
                !name.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '[' or ']'))
            {
                return null;
            }

            after = line.Length;
        }

        // A marker's own modifier is not part of the field's name: an export that lists
        // ##DATA@dd/MM/yyyy## is still describing DATA.
        name = markers.NameOf(name).Trim();

        string type = string.Empty;
        string origin = string.Empty;
        foreach (Match match in LabelledPair().Matches(line[after..]))
        {
            string value = match.Groups["value"].Value.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            // Whichever words the exporting system used, the first labelled pair is what
            // the value is and the second is where it comes from. Naming them in the
            // exporter's language would tie this to one system.
            if (type.Length == 0)
            {
                type = value;
            }
            else if (origin.Length == 0)
            {
                origin = value;
            }
        }

        return new FieldDefinition(name, type, origin, LooksLikeAList(type));
    }
}
