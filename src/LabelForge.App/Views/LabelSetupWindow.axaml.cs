using Avalonia.Controls;
using Avalonia.Interactivity;
using LabelForge.Core.Media;

namespace LabelForge.App.Views;

public partial class LabelSetupWindow : Window
{
    public LabelSetupWindow()
    {
        InitializeComponent();
        MediaBox.ItemFilter = (query, item) =>
            item is StockMedia media && StockCatalog.IsMatch(media, query);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
