using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;

internal static class RecoveryContrastChecks
{
    public static void Run(MainWindow window, MainViewModel main, Action<string, bool> check)
    {
        var view = window.GetVisualDescendants().OfType<DesignerView>().Single();
        var banner = view.FindControl<Border>("RecoveryBanner")!;
        string offer = main.Designer.RecoveryOffer;
        main.Designer.RecoveryOffer = "Unsaved changes to a synthetic QA label.";
        banner.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        window.MouseMove(new Point(5, 5));
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var bannerBackground = ((ISolidColorBrush)banner.Background!).Color;
        var message = banner.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == main.Designer.RecoveryOffer);
        double messageContrast = Contrast(((ISolidColorBrush)message.Foreground!).Color, bannerBackground);
        check($"{window.ActualThemeVariant}: recovery message contrast {messageContrast:F2} >= 4.5",
            messageContrast >= 4.5);
        var buttons = banner.GetVisualDescendants().OfType<Button>().ToArray();
        foreach (var button in buttons)
        {
            var foreground = ((ISolidColorBrush)button.Foreground!).Color;
            var background = Composite(((ISolidColorBrush)button.Background!).Color, bannerBackground);
            double contrast = Contrast(Composite(foreground, background), background);
            check($"{window.ActualThemeVariant}: recovery {button.Content} contrast {contrast:F2} >= 4.5",
                contrast >= 4.5);
        }
        main.Designer.RecoveryOffer = offer;
        banner.IsVisible = false;
    }

    private static Color Composite(Color front, Color back)
    {
        double alpha = front.A / 255d;
        byte Blend(byte f, byte b) => (byte)Math.Round(f * alpha + b * (1 - alpha));
        return Color.FromRgb(Blend(front.R, back.R), Blend(front.G, back.G), Blend(front.B, back.B));
    }

    private static double Contrast(Color first, Color second)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) =>
            0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
