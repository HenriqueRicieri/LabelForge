using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

/// <summary>
/// Copying elements within a document. The copy goes through the .lfl serializer rather
/// than through a hand-written clone per type, which is the only way it stays true as
/// element types gain properties: a field nobody remembered to copy is a field that
/// silently reverts to its default on every duplicate.
///
/// Identity is the caller's problem to state and this settles it: a copy gets a fresh
/// <see cref="Element.Id"/>, because two elements sharing one id break selection, undo
/// restore and anything else that finds an element by it.
/// </summary>
public static class ElementDuplicator
{
    /// <summary>
    /// Copies elements and stacks the copies above everything already in the document,
    /// keeping their order among themselves and leaving their positions exactly where the
    /// originals are. Nothing is added to the document; the caller decides that.
    ///
    /// Positions are untouched on purpose. This is the copy a duplicating drag makes,
    /// where the copies start under the pointer and the drag moves them; a copy made from
    /// a menu wants a visible offset instead, and that is the caller's to apply.
    /// </summary>
    public static List<Element> Clone(LabelDocument document, IEnumerable<Element> elements)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(elements);

        List<Element> ordered = [.. elements.OrderBy(e => e.ZOrder)];
        if (ordered.Count == 0)
        {
            return [];
        }

        List<Element> clones = LabelDocumentJson.DeserializeElements(
            LabelDocumentJson.SerializeElements(ordered));

        int nextZ = document.Elements.Count == 0
            ? 0
            : document.Elements.Max(e => e.ZOrder) + 1;

        foreach (Element clone in clones)
        {
            clone.Id = Guid.NewGuid();
            clone.ZOrder = nextZ++;
        }

        // A copied group is a group of its own, not more members of the original, and a
        // copy of only part of one is not a group at all.
        Groups.Remap(clones);
        return clones;
    }
}
