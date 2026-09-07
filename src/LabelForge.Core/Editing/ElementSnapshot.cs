using System.Reflection;
using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

/// <summary>
/// What some elements looked like a moment ago, and putting them back.
///
/// This exists because a gesture cannot be undone by arithmetic. A move could be reversed
/// from the X and Y it started at, but a resize cannot: <see cref="ElementResizer"/>
/// quantizes, so a barcode dragged from 3 module widths to 4 and back does not return to 3
/// by subtracting what was added. The only reliable answer to "what was it before" is a
/// copy of before.
///
/// The values are put back onto the SAME instances rather than replacing them, because the
/// selection, the drag list and the canvas all hold references to those objects, and
/// swapping them out would leave every one of those pointing at an element the document no
/// longer contains.
/// </summary>
public static class ElementSnapshot
{
    /// <summary>Every settable property of an element, found once per type. Reflection
    /// rather than a per-type visitor for the same reason the copy goes through the
    /// serializer: a property added later is included without anyone remembering to.</summary>
    private static readonly Dictionary<Type, PropertyInfo[]> Properties = [];
    private static readonly Lock Gate = new();

    /// <summary>The state to come back to, taken before a gesture starts.</summary>
    public static string Capture(IEnumerable<Element> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        return LabelDocumentJson.SerializeElements(elements);
    }

    /// <summary>
    /// Puts a captured state back onto the live elements, matched by
    /// <see cref="Element.Id"/>. Elements the snapshot does not mention are left alone, and
    /// so is anything in the snapshot that is no longer in the list: a gesture that added
    /// or removed elements is the caller's to unwind, and this only speaks for the ones
    /// present in both.
    /// </summary>
    /// <returns>How many elements were restored.</returns>
    public static int Restore(string snapshot, IEnumerable<Element> live)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(live);

        Dictionary<Guid, Element> before = LabelDocumentJson.DeserializeElements(snapshot)
            .GroupBy(e => e.Id)
            .ToDictionary(g => g.Key, g => g.First());

        int restored = 0;
        foreach (Element element in live)
        {
            if (!before.TryGetValue(element.Id, out Element? was)
                || was.GetType() != element.GetType())
            {
                continue;
            }

            foreach (PropertyInfo property in SettableProperties(element.GetType()))
            {
                property.SetValue(element, property.GetValue(was));
            }

            restored++;
        }

        return restored;
    }

    private static PropertyInfo[] SettableProperties(Type type)
    {
        lock (Gate)
        {
            if (Properties.TryGetValue(type, out PropertyInfo[]? cached))
            {
                return cached;
            }

            PropertyInfo[] found = [.. type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)];
            Properties[type] = found;
            return found;
        }
    }
}
