namespace LabelForge.Core.Editing;

/// <summary>
/// Snapshot-based undo/redo history. States are opaque strings (serialized
/// documents); the cursor points at the current state. Snapshot undo was chosen
/// over command-pattern undo because label documents are tiny, it is trivially
/// testable, and it cannot drift out of sync with the model.
///
/// Tiny is not a guarantee, though: an image element carries its source file, so one
/// placed photo makes every state megabytes long. Besides the step count, the history
/// therefore holds a size budget, and past it the oldest states go, down to a floor of
/// recent steps that are always kept.
/// </summary>
public sealed class SnapshotHistory
{
    /// <summary>About 256 MB of UTF-16 text.</summary>
    public const long DefaultMaxChars = 128_000_000;

    public const int DefaultMinStates = 10;

    private readonly List<string> _states = [];
    private readonly int _capacity;
    private readonly long _maxChars;
    private readonly int _minStates;
    private long _totalChars;
    private int _cursor = -1;

    public SnapshotHistory(
        int capacity = 100, long maxChars = DefaultMaxChars, int minStates = DefaultMinStates)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "History needs room for at least two states.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChars);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minStates);

        _capacity = capacity;
        _maxChars = maxChars;
        _minStates = minStates;
    }

    public bool CanUndo => _cursor > 0;

    public bool CanRedo => _cursor < _states.Count - 1;

    /// <summary>The state the cursor points at, or null before anything is recorded.
    /// Undo and redo move it, so comparing it with a saved state says whether the
    /// document is back where it was saved.</summary>
    public string? Current => _cursor >= 0 ? _states[_cursor] : null;

    /// <summary>Records a new state after an edit, discarding any redo tail.</summary>
    public void Record(string state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // A no-op edit (identical state) must not create an empty undo step.
        if (_cursor >= 0 && _states[_cursor] == state)
        {
            return;
        }

        DropRedoTail();
        _states.Add(state);
        _totalChars += state.Length;
        _cursor = _states.Count - 1;
        Trim();
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

        DropRedoTail();
        _totalChars += state.Length - _states[_cursor].Length;
        _states[_cursor] = state;
        Trim();
    }

    /// <summary>Empties the history (e.g. when a new document is loaded).</summary>
    public void Clear()
    {
        _states.Clear();
        _totalChars = 0;
        _cursor = -1;
    }

    private void DropRedoTail()
    {
        for (int i = _cursor + 1; i < _states.Count; i++)
        {
            _totalChars -= _states[i].Length;
        }

        _states.RemoveRange(_cursor + 1, _states.Count - _cursor - 1);
    }

    /// <summary>Drops the oldest states while there are more than the capacity, or while
    /// they add up to more than the budget and more than the floor of recent steps remain.
    /// Only ever called with the cursor on the newest state.</summary>
    private void Trim()
    {
        while (_states.Count > _capacity ||
               (_totalChars > _maxChars && _states.Count > _minStates))
        {
            _totalChars -= _states[0].Length;
            _states.RemoveAt(0);
            _cursor--;
        }
    }

    /// <summary>Steps back and returns the previous state, or null at the beginning.</summary>
    public string? Undo() => CanUndo ? _states[--_cursor] : null;

    /// <summary>Steps forward and returns the next state, or null at the end.</summary>
    public string? Redo() => CanRedo ? _states[++_cursor] : null;
}
