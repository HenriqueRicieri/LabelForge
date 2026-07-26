using System.Text.RegularExpressions;
using LabelForge.Core.Model;

namespace LabelForge.Core.Fields;

/// <param name="Owner">The type the function is called on, which is what precedes the
/// dot in a marker.</param>
/// <param name="Name">The function's own name.</param>
/// <param name="Parameters">Its parameter names in order. These are field names at the
/// call site, which is exactly why they are worth capturing: the marker reads
/// "Abate.maturidade(COD_MATURIDADE)", so the signature already spells out what to
/// pass.</param>
public sealed record FieldFunction(
    string Owner,
    string Name,
    IReadOnlyList<string> Parameters)
{
    /// <summary>The marker that calls this function, with its parameter names left in
    /// place as the fields to replace.</summary>
    public string Marker(MarkerSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        return syntax.Open
               + syntax.ModifierSeparator + Owner + "." + Name
               + "(" + string.Join(",", Parameters) + ")"
               + syntax.Close;
    }

    public override string ToString() =>
        $"{Owner}.{Name}({string.Join(", ", Parameters)})";
}

/// <summary>
/// Reads callable helpers out of a script file, so a marker that calls one can be picked
/// instead of retyped.
///
/// These exist because a field on its own is sometimes not enough: the label needs a
/// value derived from one, and the derivation is written as a small script alongside the
/// design. The call is the hardest marker shape to type from memory, since it names a
/// type, a method and the fields to pass, so it is the one most worth completing.
///
/// Signatures only. Nothing here executes, compiles or interprets a single line of the
/// file: it reads what can be called and what to pass, and the system that fills markers
/// in does the rest. That also keeps it honest about a file it may only partly
/// understand, since anything it fails to recognise is simply not offered.
/// </summary>
public static partial class ScriptFunctionReader
{
    /// <summary>A class or similar declaration, whose name owns the methods after it.</summary>
    [GeneratedRegex(
        @"\b(?:class|struct|record)\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex Owner();

    /// <summary>A public method: a return type, a name, and a parameter list. A
    /// constructor has no return type and a property has no parentheses, so neither
    /// matches, which is the point of requiring both.</summary>
    [GeneratedRegex(
        @"\bpublic\s+(?:static\s+|virtual\s+|override\s+|async\s+)*"
        + @"[\w<>\[\],.?]+\s+(?<name>[A-Za-z_]\w*)\s*\((?<args>[^)]*)\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex Method();

    public static IReadOnlyList<FieldFunction> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var functions = new List<FieldFunction>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match method in Method().Matches(text))
        {
            // The owner is whichever declaration most recently opened above this method.
            string owner = LastOwnerBefore(text, method.Index);
            if (owner.Length == 0)
            {
                continue;
            }

            string name = method.Groups["name"].Value;
            var function = new FieldFunction(owner, name, Parameters(method.Groups["args"].Value));
            if (seen.Add($"{owner}.{name}"))
            {
                functions.Add(function);
            }
        }

        return functions;
    }

    private static string LastOwnerBefore(string text, int index)
    {
        string owner = string.Empty;
        foreach (Match match in Owner().Matches(text))
        {
            if (match.Index > index)
            {
                break;
            }

            owner = match.Groups["name"].Value;
        }

        return owner;
    }

    /// <summary>Parameter names, dropping the types. A name is the last word of each
    /// declaration, which holds for "string COD_MATURIDADE" and for the qualified and
    /// generic forms alike without having to understand any of them.</summary>
    private static IReadOnlyList<string> Parameters(string arguments)
    {
        var names = new List<string>();
        foreach (string part in arguments.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] words = part.Trim().Split(
                [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0)
            {
                names.Add(words[^1].Trim());
            }
        }

        return names;
    }
}
