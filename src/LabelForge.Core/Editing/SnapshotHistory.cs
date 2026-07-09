namespace LabelForge.Core.Editing;

/// <summary>
/// Snapshot-based undo/redo history. States are opaque strings (serialized
/// documents); the cursor points at the current state. Snapshot undo was chosen
/// over command-pattern undo because label documents are tiny, it is trivially
/// testable, and it cannot drift out of sync with the model.
/// </summary>
public sealed class SnapshotHistory
{
    private readonly List<string> _states = [];
    private readonly int _capacity;
    private int _cursor = -1;

    public SnapshotHistory(int capacity = 100)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "History needs room for at least two states.");
        }

        _capacity = capacity;
    }

    public bool CanUndo => _cursor > 0;

    public bool CanRedo => _cursor < _states.Count - 1;

    /// <summary>Records a new state after an edit, discarding any redo tail.</summary>
    public void Record(string state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // A no-op edit (identical state) must not create an empty undo step.
        if (_cursor >= 0 && _states[_cursor] == state)
        {
            return;
        }

        _states.RemoveRange(_cursor + 1, _states.Count - _cursor - 1);
        _states.Add(state);

        if (_states.Count > _capacity)
        {
            _states.RemoveAt(0);
        }

        _cursor = _states.Count - 1;
    }

    /// <summary>
    /// Overwrites the current state instead of pushing a new one. Used to coalesce
    /// bursts of small edits (typing, arrow-key nudges) into a single undo step.
    /// </summary>
    public void ReplaceCurrent(string state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_cursor < 0)
        {
            Record(state);
            return;
        }

        _states.RemoveRange(_cursor + 1, _states.Count - _cursor - 1);
        _states[_cursor] = state;
    }

    /// <summary>Steps back and returns the previous state, or null at the beginning.</summary>
    public string? Undo() => CanUndo ? _states[--_cursor] : null;

    /// <summary>Steps forward and returns the next state, or null at the end.</summary>
    public string? Redo() => CanRedo ? _states[++_cursor] : null;
}
