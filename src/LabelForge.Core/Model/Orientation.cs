namespace LabelForge.Core.Model;

/// <summary>
/// Field orientation. ZPL only supports these four values (command letters N/R/I/B),
/// so a label element can never carry a free rotation angle.
/// </summary>
public enum Orientation
{
    /// <summary>No rotation (ZPL "N").</summary>
    Normal,

    /// <summary>Rotated 90 degrees clockwise (ZPL "R").</summary>
    Rotated90,

    /// <summary>Rotated 180 degrees (ZPL "I").</summary>
    Rotated180,

    /// <summary>Rotated 270 degrees, i.e. bottom-up (ZPL "B").</summary>
    Rotated270,
}
