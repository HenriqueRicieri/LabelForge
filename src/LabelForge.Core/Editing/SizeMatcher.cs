using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

/// <summary>What matching sizes did.</summary>
/// <param name="Resized">How many elements changed size.</param>
/// <param name="Quantized">How many of those could not land exactly on the size asked for,
/// because their size comes in steps: a barcode moves in whole module widths, a QR in whole
/// magnifications. The caller says so rather than the canvas quietly disagreeing with the
/// number the user just matched to.</param>
public readonly record struct SizeMatch(int Resized, int Quantized);

/// <summary>
/// Making elements the same size as one of them, which is the sizing half of the alignment
/// commands. It resizes through <see cref="ElementResizer"/> rather than assigning widths
/// itself, so every clamp and every quantization step stays in the one place that knows
/// them, and it works in DRAWN sizes for the same reason the aligner does: what a person
/// means by "the same width" is the ink, not the field's declared box.
/// </summary>
public static class SizeMatcher
{
    /// <summary>
    /// Sizes every element to the reference's drawn box, on the axes asked for. The
    /// reference itself and anything locked are left alone.
    /// </summary>
    public static SizeMatch Match(
        IEnumerable<Element> elements, Element reference, bool width, bool height)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(reference);

        if (!width && !height)
        {
            return default;
        }

        var bounds = new ElementBoundsCalculator();
        DotRect target = bounds.GetBounds(reference);

        int resized = 0;
        int quantized = 0;
        foreach (Element element in elements)
        {
            if (element.IsLocked || ReferenceEquals(element, reference))
            {
                continue;
            }

            DotRect before = bounds.GetBounds(element);
            int wantWidth = width ? target.Width : before.Width;
            int wantHeight = height ? target.Height : before.Height;
            if (wantWidth == before.Width && wantHeight == before.Height)
            {
                continue;
            }

            ElementResizer.Resize(element, wantWidth, wantHeight);

            DotRect after = bounds.GetBounds(element);
            if (after.Width == before.Width && after.Height == before.Height)
            {
                // Asked for something it could not do at all, and it did not move: a
                // barcode already at its narrowest module, for instance.
                quantized++;
                continue;
            }

            resized++;
            if ((width && after.Width != wantWidth) || (height && after.Height != wantHeight))
            {
                quantized++;
            }
        }

        return new SizeMatch(resized, quantized);
    }
}
