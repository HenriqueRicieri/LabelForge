using System.Collections.Generic;

namespace LabelForge.App.ViewModels;

/// <param name="Keys">The keys or the gesture, written the way a person says them.</param>
/// <param name="Does">What it does, in the same words the menus use for it.</param>
public sealed record ShortcutEntry(string Keys, string Does);

/// <param name="Note">Something the list itself cannot say, or empty.</param>
public sealed record ShortcutGroup(string Title, string Note, IReadOnlyList<ShortcutEntry> Entries)
{
    public bool HasNote => Note.Length > 0;
}

/// <summary>
/// What the designer answers to, written down.
///
/// It existed only in the source until now, which is the whole reason for the window:
/// nudging with the arrow keys, holding Alt to escape snapping and middle-dragging to pan
/// are worth knowing and nothing anywhere said so.
///
/// The list is hand-kept rather than derived, and that is a real cost to accept
/// deliberately: a shortcut added without a line here goes undocumented. Deriving it is
/// not possible in any honest way, because these live in three places at once (a window
/// key handler, the canvas control, and pointer gestures with no key at all) and a
/// generated list would describe the first and miss the rest, which is worse than a short
/// list that is true.
/// </summary>
public sealed class ShortcutsViewModel
{
    public IReadOnlyList<ShortcutGroup> Groups { get; } =
    [
        new("File", string.Empty,
        [
            new("Ctrl + N", "New label"),
            new("Ctrl + O", "Open a .lfl"),
            new("Ctrl + S", "Save (asks where the first time)"),
        ]),

        new("Editing", string.Empty,
        [
            new("Ctrl + Z", "Undo"),
            new("Ctrl + Y  /  Ctrl + Shift + Z", "Redo"),
            new("Ctrl + C", "Copy the selection"),
            new("Ctrl + V", "Paste, cascaded from the last one"),
            new("Ctrl + D", "Duplicate the selection"),
            new("Delete", "Delete the selection"),
            new("Escape", "Cancel an insert that is waiting for a click"),
        ]),

        new("Moving things", "A locked element sits every one of these out.",
        [
            new("Arrow keys", "Nudge by one dot"),
            new("Shift + arrows", "Nudge by ten dots"),
            new("Drag", "Move the whole selection together"),
            new("Alt + drag", "Move without snapping to anything"),
            new("Drag a handle", "Resize; barcodes step in whole modules"),
            new("Drag the round handle", "Rotate, snapping to 0, 90, 180 and 270"),
        ]),

        new("Selecting", string.Empty,
        [
            new("Click", "Select what is under the pointer"),
            new("Ctrl / Shift + click", "Add to or remove from the selection"),
            new("Drag on empty stock", "Marquee-select"),
            new("Right-click an element", "Its menu, selecting it first if it was not"),
            new("Right-click empty stock", "Paste, insert and zoom for that spot"),
        ]),

        new("The view", "Zoom stops at the pointer, so what you were looking at stays put.",
        [
            new("Ctrl + wheel", "Zoom in and out"),
            new("Wheel", "Scroll up and down"),
            new("Shift + wheel", "Scroll sideways"),
            new("Middle-drag", "Pan"),
            new("Ctrl + 0", "Fit the label to the window"),
            new("Ctrl + Shift + 0", "Frame the selection"),
        ]),

        new("Rulers and guides", "Guides are saved with the label and never printed.",
        [
            new("Hold on a ruler", "A guide that follows the pointer until you let go"),
            new("Double-click a ruler", "Leave a guide there"),
            new("Right-click a ruler", "Insert a guide, or clear them all"),
            new("Drag a guide", "Move it; drop it on a ruler to delete it"),
            new("Right-click a guide", "Remove that one"),
        ]),
    ];
}
