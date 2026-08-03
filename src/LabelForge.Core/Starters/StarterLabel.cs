using System.Globalization;
using LabelForge.Core.Model;

namespace LabelForge.Core.Starters;

/// <summary>
/// A ready-made label to start from: a name, a stock size, and the layout that fills it.
///
/// "Starter" rather than "template" on purpose. A template in this app is a label whose
/// fields carry `##MARKER##` placeholders for another system to fill, which most of these
/// are, so calling the gallery entries templates too would make the word mean two things
/// in one codebase. This is the thing you start from; what it produces is a document like
/// any other, editable from the first click and tied to no file.
///
/// The layout is a delegate rather than a stored document, and that is the point of the
/// whole type: <see cref="Create"/> builds a fresh document at the density it is given, so
/// picking a starter on a 300 dpi printer produces the same physical label as on a 203 dpi
/// one instead of the same dot coordinates at half the size. A stored document would carry
/// one density baked into every coordinate.
/// </summary>
public sealed class StarterLabel
{
    private readonly Action<StarterSheet> _layout;

    internal StarterLabel(
        string name, string summary, double widthMm, double heightMm, Action<StarterSheet> layout)
    {
        Name = name;
        Summary = summary;
        WidthMm = widthMm;
        HeightMm = heightMm;
        _layout = layout;
    }

    /// <summary>What the gallery lists it as.</summary>
    public string Name { get; }

    /// <summary>One line on what it is for and what it demonstrates.</summary>
    public string Summary { get; }

    /// <summary>Stock width in millimeters.</summary>
    public double WidthMm { get; }

    /// <summary>Stock height in millimeters.</summary>
    public double HeightMm { get; }

    /// <summary>The size as the gallery shows it, and the size <see cref="Create"/>
    /// produces: one number, so the caption cannot describe a different label from the
    /// one the button makes.</summary>
    public string SizeText => string.Create(
        CultureInfo.InvariantCulture, $"{WidthMm:0.#} x {HeightMm:0.#} mm");

    /// <summary>
    /// Builds the label at a print density, in dots per millimeter.
    ///
    /// A new document every call, never a shared one handed out twice: the designer edits
    /// what it is given, so a cached instance would let one session's edits turn up in the
    /// next person's "new" label.
    /// </summary>
    public LabelDocument Create(int dpmm)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dpmm, 1);

        var document = new LabelDocument { WidthMm = WidthMm, HeightMm = HeightMm, Dpmm = dpmm };
        _layout(new StarterSheet(document));
        return document;
    }
}
