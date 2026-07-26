namespace LabelForge.App.ViewModels;

/// <summary>
/// One ^XA block of an imported file, for the picker.
///
/// The element count is shown because it is the only thing that distinguishes them
/// before one is opened, and it distinguishes the important case: real files open with a
/// bare printer-configuration block, so "Label 1" is routinely empty and the label
/// somebody wants is further down.
/// </summary>
public sealed record ImportedBlockViewModel(int Index, int ElementCount)
{
    public override string ToString()
    {
        string what = ElementCount switch
        {
            0 => "empty",
            1 => "1 element",
            _ => $"{ElementCount} elements",
        };

        return $"Label {Index + 1} ({what})";
    }
}
