namespace LabelForge.App.ViewModels;

public partial class DesignerViewModel
{
    private static decimal Cm(decimal millimeters) => decimal.Round(millimeters / 10m, 3);

    public decimal WidthCm
    {
        get => Cm(WidthMm);
        set => WidthMm = value * 10m;
    }

    public decimal HeightCm
    {
        get => Cm(HeightMm);
        set => HeightMm = value * 10m;
    }

    public decimal ContinuousMarginCm
    {
        get => Cm(ContinuousMarginMm);
        set => ContinuousMarginMm = value * 10m;
    }

    public decimal CornerRadiusCm
    {
        get => Cm(CornerRadiusMm);
        set => CornerRadiusMm = value * 10m;
    }

    public decimal AcrossGapCm
    {
        get => Cm(AcrossGapMm);
        set => AcrossGapMm = value * 10m;
    }

    public decimal NewMediaRadiusCm
    {
        get => Cm(NewMediaRadiusMm);
        set => NewMediaRadiusMm = value * 10m;
    }

    public decimal GridPitchCm
    {
        get => Cm((decimal)GridPitchMm);
        set => GridPitchMm = (double)(value * 10m);
    }
}
