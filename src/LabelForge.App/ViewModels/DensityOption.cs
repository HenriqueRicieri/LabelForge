using System.Collections.Generic;

namespace LabelForge.App.ViewModels;

/// <summary>A print-density choice shown in the UI (dpi label, dpmm value used by the engine).</summary>
public sealed class DensityOption(int dpi, int dpmm)
{
    public int Dpi { get; } = dpi;

    public int Dpmm { get; } = dpmm;

    /// <summary>The standard Zebra densities offered everywhere in the app.</summary>
    public static IReadOnlyList<DensityOption> Standard { get; } =
    [
        new DensityOption(203, 8),
        new DensityOption(300, 12),
        new DensityOption(600, 24),
    ];

    public override string ToString() => $"{Dpi} dpi";
}
