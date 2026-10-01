using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelForge.App.ViewModels;
using LabelForge.App.Views;
using LabelForge.Core.Io;
using LabelForge.Core.Model;

/// <summary>
/// Unsaved work is asked about before anything replaces or closes it, and the window title
/// says which label is open and whether it has changes.
///
/// Runs in a window of its own with its own scratch stores, because the point is to close
/// that window, and closing the harness's main window would end the rest of the pass. The
/// prompt is found the way a user meets it: as a window owned by the editor, opened by the
/// same key or menu item a person would use.
/// </summary>
internal static class UnsavedChangesChecks
{
    private const string TwoLabels =
        "^XA^FO20,20^A0N,30,30^FDfirst^FS^XZ\n^XA^FO20,20^A0N,30,30^FDsecond^FS^XZ\n";

    public static void Run(string scratch, Action<string, object?, object?> check)
    {
        string dir = Path.Combine(scratch, "unsaved-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string recovery = Path.Combine(dir, "recovery");

        MainViewModel NewShell(string session) => new(
            new LabelForge.Core.Media.UserMediaStore(Path.Combine(dir, "media.json")),
            new LabelForge.Core.Fields.FieldCatalogStore(Path.Combine(dir, "fields.json")),
            new RecoveryStore(recovery, session),
            () => new LabelForge.Core.Rendering.BinaryKitsRenderer(),
            new LabelForge.Core.Settings.UserSettingsStore(Path.Combine(dir, "settings.json")),
            recentFilesPath: Path.Combine(dir, "recent.json"));

        var vm = NewShell("unsaved");
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 760 };
        window.Show();
        Pump(400);
        DesignerViewModel d = vm.Designer;

        d.NewDocumentCommand.Execute(null);
        Pump(300);
        check("unsaved: a new label is titled Untitled", window.Title, "Untitled - LabelForge");

        AddText(d, "keep me");
        check("unsaved: an edit marks the title", window.Title, "Untitled* - LabelForge");

        // Ctrl+N is how most people reach New; the menu item runs the same handler.
        Press(window, Avalonia.Input.Key.N, RawInputModifiers.Control, PhysicalKey.N, "n");
        Window? prompt = Prompt(window);
        check("unsaved: Ctrl+N on unsaved work asks first", prompt is not null, true);
        check("unsaved: the question names the label",
            Text(prompt, "QuestionText"), "Save changes to Untitled?");
        check("unsaved: nothing is replaced while it asks", d.Document.Elements.Count, 1);

        if (prompt is not null)
        {
            Press(prompt, Avalonia.Input.Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        }

        check("unsaved: Escape cancels the question", Prompt(window) is null, true);
        check("unsaved: Cancel keeps the work", d.Document.Elements.Count, 1);
        check("unsaved: Cancel keeps the title marked", window.Title, "Untitled* - LabelForge");

        Press(window, Avalonia.Input.Key.N, RawInputModifiers.Control, PhysicalKey.N, "n");
        Click(Prompt(window), "DontSaveButton");
        check("unsaved: Don't Save goes ahead with New", d.Document.Elements.Count, 0);
        check("unsaved: the new label starts unmarked", window.Title, "Untitled - LabelForge");

        // Saving to a known path: Ctrl+S writes it and the title follows the file.
        string path = Path.Combine(dir, "saved-check.lfl");
        AddText(d, "saved text");
        d.CurrentFilePath = path;
        Press(window, Avalonia.Input.Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");

        // The write is asynchronous; a slow machine (CI) can still hold the file open after
        // a fixed pump, so wait for the save to land, which is when the label turns clean.
        PumpUntil(() => !d.IsDirty, 5000);
        check("unsaved: Ctrl+S writes the label",
            File.Exists(path) && File.ReadAllText(path).Contains("saved text"), true);
        check("unsaved: saving clears the mark", window.Title, "saved-check.lfl - LabelForge");

        AddText(d, "undone later");
        check("unsaved: an edit after saving marks it again", window.Title, "saved-check.lfl* - LabelForge");
        d.UndoCommand.Execute(null);
        Pump(300);
        check("unsaved: undoing back to the saved label clears the mark",
            window.Title, "saved-check.lfl - LabelForge");
        check("unsaved: an unmodified label opens New without asking",
            NewWithoutPrompt(window, d), true);

        // The other ways of replacing the label: Open Recent and another label of an
        // imported file. Both have to ask, and Cancel has to leave everything as it was.
        d.OpenLabelFile(path);
        Pump(300);
        AddText(d, "recent guard");
        var recent = window.GetVisualDescendants().OfType<DesignerView>().Single()
            .FindControl<MenuItem>("RecentMenu")!.Items.OfType<MenuItem>().First();
        recent.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Pump(200);
        check("unsaved: Open Recent asks first", Prompt(window) is not null, true);
        Click(Prompt(window), "CancelButton");
        check("unsaved: Cancel keeps the label Open Recent would replace",
            d.Document.Elements.Count, 2);

        d.ImportZplDocument(TwoLabels, "two-labels.zpl");
        Pump(300);
        AddText(d, "picker guard");
        var picker = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => c.Name == "ImportedBlockPicker");
        var first = d.SelectedImportedBlock;
        picker.SelectedIndex = 1;
        Pump(200);
        check("unsaved: picking another imported label asks first", Prompt(window) is not null, true);
        Click(Prompt(window), "CancelButton");
        check("unsaved: Cancel keeps the imported label on the canvas",
            ReferenceEquals(d.SelectedImportedBlock, first) && d.Document.Elements.Count == 2, true);
        check("unsaved: Cancel puts the picker back", ReferenceEquals(picker.SelectedItem, first), true);

        // The title follows the tab on show.
        vm.SelectedTab = MainViewModel.ViewerTab;
        Pump(200);
        check("unsaved: the viewer's sample leaves the title plain", window.Title, "LabelForge");
        string zpl = Path.Combine(dir, "viewer-check.zpl");
        File.WriteAllText(zpl, TwoLabels);
        vm.Viewer.OpenFile(zpl);
        Pump(200);
        check("unsaved: the viewer names its file", window.Title, "viewer-check.zpl - LabelForge");

        // Closing from the viewer tab still asks, in front of the label it is about.
        window.Close();
        Pump(300);
        check("unsaved: closing with unsaved work asks first", Prompt(window) is not null, true);
        check("unsaved: the question is asked on the Designer tab", vm.SelectedTab, MainViewModel.DesignerTab);
        Click(Prompt(window), "CancelButton");
        check("unsaved: Cancel keeps the window open", window.IsVisible, true);

        window.Close();
        Pump(300);
        Click(Prompt(window), "DontSaveButton");
        check("unsaved: Don't Save closes the window", window.IsVisible, false);
        d.ShutDown();
        check("unsaved: Don't Save leaves nothing to recover", Snapshots(recovery), 0);

        // Windows signing out closes without anyone to ask. The work is kept for the next
        // start instead, and arrives there marked as unsaved.
        var signOut = NewShell("signed-out");
        signOut.Designer.NewDocumentCommand.Execute(null);
        AddText(signOut.Designer, "kept for later");
        signOut.Designer.ShutDown();
        var next = NewShell("next-start");
        check("unsaved: work ended without an answer is offered next start",
            next.Designer.HasRecoveryOffer, true);
        next.Designer.RecoverDocumentCommand.Execute(null);
        Pump(200);
        check("unsaved: recovered work starts marked", next.WindowTitle, "Untitled* - LabelForge");
        next.Designer.AcceptDiscard();
        next.Designer.ShutDown();
        check("unsaved: discarding recovered work leaves nothing", Snapshots(recovery), 0);
    }

    private static void AddText(DesignerViewModel d, string text)
    {
        d.Document.Elements.Add(new TextElement { X = 30, Y = 30, Text = text, FontHeightDots = 30 });
        d.NotifyDocumentEdited();
        Pump(400);
    }

    private static bool NewWithoutPrompt(Window window, DesignerViewModel d)
    {
        Press(window, Avalonia.Input.Key.N, RawInputModifiers.Control, PhysicalKey.N, "n");
        bool asked = Prompt(window) is not null;
        return !asked && d.Document.Elements.Count == 0;
    }

    private static Window? Prompt(Window owner) =>
        owner.OwnedWindows.FirstOrDefault(w => w.GetType().Name == "UnsavedChangesWindow" && w.IsVisible);

    private static string? Text(Window? prompt, string name) =>
        prompt?.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == name)?.Text;

    private static void Click(Window? prompt, string button)
    {
        prompt?.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Name == button)
            ?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(300);
    }

    private static int Snapshots(string directory) => Directory.Exists(directory)
        ? Directory.GetFiles(directory, "*.recovery.json").Length
        : 0;

    private static void Press(Window target, Key key, RawInputModifiers modifiers, PhysicalKey physical, string? symbol)
    {
        target.KeyPress(key, modifiers, physical, symbol);
        Pump(300);
    }

    private static void PumpUntil(Func<bool> done, int timeoutMilliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (!done() && timer.ElapsedMilliseconds < timeoutMilliseconds)
        {
            Pump(20);
        }
    }

    private static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(20);
        }
    }
}
