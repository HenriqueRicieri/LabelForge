using Avalonia.Controls;
using LabelForge.App.ViewModels;

namespace LabelForge.App.Views;

public partial class DesignerView : UserControl
{
    public DesignerView()
    {
        InitializeComponent();

        Canvas.DocumentEdited += (_, _) => ViewModel?.NotifyDocumentEdited();
        Canvas.DeleteRequested += (_, _) => ViewModel?.DeleteSelectedCommand.Execute(null);
    }

    private DesignerViewModel? ViewModel => DataContext as DesignerViewModel;
}
