using System.Threading.Tasks;
using Avalonia.Controls;
using LabelForge.App.ViewModels;

namespace LabelForge.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Set once the unsaved-changes question has been answered, so the Close
    /// that follows goes through instead of asking again.</summary>
    private bool _closeConfirmed;

    private bool _askingToClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Asks about unsaved changes before the window goes. Closing is cancelled while the
    /// question is open, then repeated once it is answered, because the answer (and a
    /// Save As picker behind it) cannot be awaited inside the event.
    ///
    /// Windows ending the session is let through without asking: there is nobody to
    /// answer, and the designer keeps its recovery snapshot for the next start instead.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeConfirmed
            || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown
            || DataContext is not MainViewModel vm || !vm.Designer.HasUnsavedChanges)
        {
            return;
        }

        e.Cancel = true;
        _ = ConfirmCloseAsync(vm);
    }

    private async Task ConfirmCloseAsync(MainViewModel vm)
    {
        if (_askingToClose)
        {
            return;
        }

        _askingToClose = true;
        try
        {
            // The question is about the label, so it is asked in front of it.
            vm.SelectedTab = MainViewModel.DesignerTab;
            if (await DesignerPane.ConfirmDiscardAsync(this))
            {
                // Only closing records the answer. Don't Save before Open or New changes
                // nothing until the label is replaced, and a dismissed picker leaves it in
                // place, still worth keeping if Windows ends the session later.
                vm.Designer.AcceptDiscard();
                _closeConfirmed = true;
                Close();
            }
        }
        finally
        {
            _askingToClose = false;
        }
    }
}
