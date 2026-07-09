using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using LabelForge.App.ViewModels;

namespace LabelForge.App.Views;

public partial class DesignerView : UserControl
{
    private TopLevel? _topLevel;

    public DesignerView()
    {
        InitializeComponent();

        Canvas.DocumentEdited += (_, _) => ViewModel?.NotifyDocumentEdited();
        Canvas.DeleteRequested += (_, _) => ViewModel?.DeleteSelectedCommand.Execute(null);
    }

    private DesignerViewModel? ViewModel => DataContext as DesignerViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.KeyDown += OnTopLevelKeyDown;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
        {
            _topLevel.KeyDown -= OnTopLevelKeyDown;
            _topLevel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        // The handler sits on the window; ignore shortcuts while another tab is
        // active. Text boxes handle their own Ctrl+Z and mark the event handled
        // before it bubbles up here.
        if (!IsEffectivelyVisible || ViewModel is not { } vm ||
            !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
            case Key.Y:
                if (vm.RedoCommand.CanExecute(null))
                {
                    vm.RedoCommand.Execute(null);
                }

                e.Handled = true;
                break;

            case Key.Z:
                if (vm.UndoCommand.CanExecute(null))
                {
                    vm.UndoCommand.Execute(null);
                }

                e.Handled = true;
                break;
        }
    }
}
