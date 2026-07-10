using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

/// <summary>
/// The set of currently selected elements, shared between the canvas (which mutates
/// it from pointer input) and the view model (which reacts and also selects
/// programmatically). The last item is the primary selection, shown in the
/// properties panel when it is the only one.
/// </summary>
public sealed class SelectionSet
{
    private readonly List<Element> _items = [];

    public event EventHandler? Changed;

    public IReadOnlyList<Element> Items => _items;

    public int Count => _items.Count;

    public Element? Primary => _items.Count > 0 ? _items[^1] : null;

    public bool Contains(Element element) => _items.Contains(element);

    /// <summary>Replaces the selection with a single element, or clears it with null.</summary>
    public void Set(Element? element)
    {
        _items.Clear();
        if (element is not null)
        {
            _items.Add(element);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetMany(IEnumerable<Element> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        _items.Clear();
        _items.AddRange(elements.Distinct());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds or removes one element (Ctrl+click).</summary>
    public void Toggle(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!_items.Remove(element))
        {
            _items.Add(element);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
