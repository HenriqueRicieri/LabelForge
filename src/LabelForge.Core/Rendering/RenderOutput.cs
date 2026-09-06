namespace LabelForge.Core.Rendering;

/// <summary>
/// What form a caller wants the rendered label in. The picture is the same either way;
/// this is only about what happens to it on the way out.
///
/// PNG is what export, PDF and the tests need, and what an online renderer can give.
/// Pixels is for anything drawing the label on screen: encoding a PNG and decoding it
/// again is 71 to 88 percent of a preview frame (tools/LabelForge.Bench), and it buys
/// the caller nothing when the next thing it does is hand the buffer to a bitmap.
/// </summary>
public enum RenderOutput
{
    /// <summary>Encoded PNG bytes in <see cref="RenderResult.Png"/>. The default, so a
    /// caller that has not thought about it keeps the behaviour it always had.</summary>
    Png,

    /// <summary>Raw BGRA pixels over a white background, in
    /// <see cref="RenderResult.Pixels"/>. What the label looks like printed.</summary>
    Pixels,

    /// <summary>Raw BGRA pixels over a transparent background, so the result composites
    /// over another render. Ink lands on transparency rather than on stock, which is what
    /// a gesture layer needs and what a printed label is not.</summary>
    TransparentPixels,
}
