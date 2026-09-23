using LabelForge.Core.Rendering;

internal sealed class DelayedComparisonRenderer(int delayMs = 450) : IZplRenderer
{
    public int? LastLabelIndex { get; private set; }
    private readonly BinaryKitsRenderer _inner = new();

    public RenderResult Render(
        string zpl, double widthMm, double heightMm, int dpmm,
        int labelIndex = 0, RenderOutput output = RenderOutput.Png)
    {
        LastLabelIndex = labelIndex;
        if (delayMs > 0)
        {
            Thread.Sleep(delayMs);
        }

        return _inner.Render(zpl, widthMm, heightMm, dpmm, labelIndex, output);
    }
}
