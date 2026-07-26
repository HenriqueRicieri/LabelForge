using LabelForge.Core.Model;
using LabelForge.Core.Templating;

namespace LabelForge.Core.Fields;

/// <param name="Name">The marker's field name as the label writes it.</param>
/// <param name="Suggestion">The catalog field it was most likely meant to be, or null
/// when nothing is close enough to be worth naming.</param>
public sealed record UnknownField(string Name, string? Suggestion);

/// <summary>
/// Names the markers on a label that the chosen field catalog does not list.
///
/// This is the check the whole catalog idea is for. A marker the filling system does not
/// recognise is not an error anywhere: it simply is not substituted, so the label prints
/// the marker text itself. Nothing fails, nothing logs, and the roll comes out with
/// "##CODIGO##" on it. One of the sample labels already carries a tag with a tab
/// character inside its name for exactly this reason.
///
/// It reports and suggests; it never blocks. A catalog one export out of date would
/// otherwise stop someone finishing a label, which is a worse failure than the one being
/// prevented.
/// </summary>
public static class UnknownFieldCheck
{
    /// <summary>Edit distance at which a near miss is worth naming rather than guessed
    /// at. Two edits catches the realistic mistakes (a typo, a missing underscore, a
    /// stray character) without pairing genuinely different field names.</summary>
    public const int MaxSuggestionDistance = 2;

    public static IReadOnlyList<UnknownField> Check(LabelDocument document, FieldCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (catalog is null || catalog.Fields.Count == 0)
        {
            return [];
        }

        var unknown = new List<UnknownField>();
        foreach (string name in TemplateVariables.Discover(document))
        {
            // A list field is written with an index and a member ("TABELA[1].QUANTIDADE"),
            // so the catalog is asked about the field, not about the whole expression.
            string root = RootOf(name);
            if (root.Length == 0 || catalog.Contains(root))
            {
                continue;
            }

            unknown.Add(new UnknownField(name, Closest(root, catalog)));
        }

        return unknown;
    }

    /// <summary>The field a marker expression addresses: everything before the first
    /// index or member access.</summary>
    public static string RootOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        int cut = name.IndexOfAny(['[', '.']);
        return (cut < 0 ? name : name[..cut]).Trim();
    }

    private static string? Closest(string name, FieldCatalog catalog)
    {
        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (FieldDefinition field in catalog.Fields)
        {
            int distance = Distance(name, field.Name, bestDistance);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = field.Name;
            }
        }

        return bestDistance <= MaxSuggestionDistance ? best : null;
    }

    /// <summary>Levenshtein distance, case-insensitive, abandoned once it cannot beat
    /// <paramref name="ceiling"/>; the catalogs run to a few hundred fields and this is
    /// asked on every render.</summary>
    private static int Distance(string a, string b, int ceiling)
    {
        if (Math.Abs(a.Length - b.Length) >= ceiling)
        {
            return int.MaxValue;
        }

        Span<int> previous = stackalloc int[b.Length + 1];
        Span<int> current = stackalloc int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int rowBest = current[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
                rowBest = Math.Min(rowBest, current[j]);
            }

            if (rowBest >= ceiling)
            {
                return int.MaxValue;
            }

            previous = current.ToArray();
        }

        return previous[b.Length];
    }
}
