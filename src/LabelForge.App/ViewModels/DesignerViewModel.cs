using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelForge.Core.Editing;
using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;

namespace LabelForge.App.ViewModels;

/// <summary>
/// The visual designer. The LabelDocument is the source of truth: the canvas and the
/// properties panel edit it, the ZPL generator turns it into code, and the offline
/// renderer turns that code into the canvas underlay (WYSIWYG rule). Rendering is
/// debounced, background, latest-wins, mirroring the viewer pipeline.
/// Undo/redo is snapshot-based: every committed edit records the serialized document
/// (the same JSON as the .lfl file format); bursts of small edits coalesce into one step.
/// </summary>
public partial class DesignerViewModel : ViewModelBase
{
    private const int CoalesceWindowMs = 500;

    private readonly IZplRenderer _renderer = new BinaryKitsRenderer();
    private readonly TemplateSubstitutor _substitutor = new();
    private readonly Core.Media.UserMediaStore _userMediaStore;
    private readonly Core.Fields.FieldCatalogStore _fieldCatalogStore;
    private readonly RecoveryStore _recovery;
    private string? _lastSnapshot;

    /// <summary>What the last drawn underlay was rendered from: the preview ZPL and the
    /// size it was drawn at. Re-rendering the same thing costs the whole engine and
    /// produces a bitmap identical to the one already on screen.</summary>
    private string? _renderedFrom;
    private readonly SnapshotHistory _history = new();
    private LabelDocument? _variablesDocument;
    private CancellationTokenSource? _renderCts;
    private bool _statusHeld;
    private bool _restoring;
    private long _lastRecordTicks;
    private string? _lastCoalesceKey;
    private string? _clipboardElement;

    public IReadOnlyList<DensityOption> Densities => DensityOption.Standard;

    [ObservableProperty]
    public partial LabelDocument Document { get; set; }

    [ObservableProperty]
    public partial Bitmap? Underlay { get; set; }

    /// <summary>Pasteboard margin baked into the underlay bitmap, in dots. 0 when the
    /// underlay is the plain label render; positive when something sits off the label
    /// and the render was expanded so that content stays visible.</summary>
    [ObservableProperty]
    public partial int UnderlayMarginDots { get; set; }

    /// <summary>
    /// Bumped once per completed render pass, whether or not the renderer ran.
    ///
    /// The canvas draws several things the renderer never sees, the die-cut corners, the
    /// quiet zones and the grid among them, and it used to learn about those through the
    /// new underlay every edit produced. Reusing an identical bitmap took that signal
    /// away, so this replaces it with one that says what it means.
    /// </summary>
    [ObservableProperty]
    public partial int CanvasRevision { get; set; }

    [ObservableProperty]
    public partial string GeneratedZpl { get; set; } = string.Empty;

    /// <summary>Shared selection, mutated by the canvas and by commands here.</summary>
    public SelectionSet Selection { get; } = new();

    /// <summary>The primary selected element (last selected).</summary>
    public Element? SelectedElement => Selection.Primary;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand))]
    [NotifyCanExecuteChangedFor(nameof(BringToFrontCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendToBackCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlignLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlignCenterHorizontalCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlignRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlignTopCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlignMiddleCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlignBottomCommand))]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DistributeHorizontalCommand))]
    [NotifyCanExecuteChangedFor(nameof(DistributeVerticalCommand))]
    public partial int SelectionCount { get; set; }

    [ObservableProperty]
    public partial bool IsSingleSelection { get; set; }

    [ObservableProperty]
    public partial bool HasMultiSelection { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PasteCommand))]
    public partial bool CanPaste { get; set; }

    /// <summary>Per-type property editor for the selection; DataTemplates pick the view.</summary>
    [ObservableProperty]
    public partial ElementPropertiesViewModel? SelectionProperties { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewMediaSizeText))]
    public partial decimal WidthMm { get; set; } = 100m;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewMediaSizeText))]
    public partial decimal HeightMm { get; set; } = 60m;

    /// <summary>What the media picker searches: the user's own presets first, then the
    /// official Zebra catalog. Replaced wholesale when presets change, rather than
    /// mutated, because the catalog is 797 entries and the picker only needs the list.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Core.Media.StockMedia> MediaCatalog { get; set; } = [];

    /// <summary>The user's saved media definitions, each with its own remove command.</summary>
    public ObservableCollection<UserMediaEntryViewModel> UserMedia { get; } = [];

    public bool HasUserMedia => UserMedia.Count > 0;

    [ObservableProperty]
    public partial string NewMediaName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewMediaMaterial { get; set; } = string.Empty;

    /// <summary>Die-cut corner radius recorded with a preset. Descriptive for now, like
    /// the radius the Zebra catalog already carries; it starts affecting the drawn
    /// outline when the document gains a corner radius (backlog A2).</summary>
    [ObservableProperty]
    public partial decimal NewMediaRadiusMm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewMediaSizeText))]
    public partial bool NewMediaContinuous { get; set; }

    /// <summary>The size saving would record right now, shown so it is confirmed before
    /// it is stored rather than after.</summary>
    public string NewMediaSizeText => Core.Media.StockMedia.FormatSize(
        (double)WidthMm, (double)HeightMm, NewMediaContinuous);

    /// <summary>
    /// Every element on the label, front to back, so one can be found by reading rather
    /// than by hunting for it on the canvas.
    ///
    /// Worth having because real labels are dense: the sample corpus runs from 38 to 60
    /// elements, and picking the one you want out of a stack of overlapping fields with
    /// the mouse is guesswork. Ordered front to back, matching what the canvas draws last
    /// and what a click would therefore hit first.
    /// </summary>
    public ObservableCollection<ElementOutlineViewModel> Outline { get; } = [];

    public bool HasOutline => Outline.Count > 0;

    public string OutlineHeader => Outline.Count == 1
        ? "Elements (1)"
        : $"Elements ({Outline.Count})";

    /// <summary>The row for the current selection, so picking in the list and picking on
    /// the canvas are the same act seen from two places.</summary>
    [ObservableProperty]
    public partial ElementOutlineViewModel? SelectedOutlineRow { get; set; }

    private bool _syncingOutline;

    partial void OnSelectedOutlineRowChanged(ElementOutlineViewModel? value)
    {
        if (_syncingOutline || value is null)
        {
            return;
        }

        Selection.Set(value.Element);
    }

    /// <summary>Template variables found on the label, with editable preview samples.
    /// Rebuilt only when the variable set or the document instance changes, so typing
    /// in a sample box never loses focus to a refresh.</summary>
    public ObservableCollection<VariableSampleViewModel> Variables { get; } = [];

    public bool HasVariables => Variables.Count > 0;

    /// <summary>Recently opened or saved .lfl paths, newest first.</summary>
    public ObservableCollection<string> RecentFiles { get; } = new(Services.RecentFilesStore.Load());

    public bool HasRecentFiles => RecentFiles.Count > 0;

    /// <summary>Field catalogs installed on this machine, newest import last. Rebuilt
    /// wholesale when one is imported or removed, the same way the media list is.</summary>
    public ObservableCollection<FieldCatalogEntryViewModel> FieldCatalogs { get; } = [];

    public bool HasFieldCatalogs => FieldCatalogs.Count > 0;

    /// <summary>
    /// The catalog this label is designed against, or null for none.
    ///
    /// Picking one is how the user says what kind of label this is: the catalog carries
    /// their own name for it, so there is no separate list of label types to keep in
    /// step with anything.
    /// </summary>
    public Core.Fields.FieldCatalog? SelectedFieldCatalog
    {
        get => _catalogs.FirstOrDefault(
            c => string.Equals(c.Name, Document.FieldCatalog, StringComparison.OrdinalIgnoreCase));
        set
        {
            string next = value?.Name ?? string.Empty;
            if (string.Equals(Document.FieldCatalog, next, StringComparison.Ordinal))
            {
                return;
            }

            Document.FieldCatalog = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FieldSuggestions));
            if (!_restoring)
            {
                RecordUndo("doc-catalog");
                ScheduleRender();
            }
        }
    }

    private IReadOnlyList<Core.Fields.FieldCatalog> _catalogs = [];

    /// <summary>
    /// Ready-to-paste markers from the bound catalog, for the completion boxes.
    ///
    /// Full markers rather than bare names, because that is what goes in the field and
    /// typing the delimiters by hand is half the mistakes. A label with no catalog gets
    /// an empty list and the boxes behave like plain text boxes.
    /// </summary>
    public IReadOnlyList<string> FieldSuggestions =>
        SelectedFieldCatalog is { } catalog
            ?
            [
                .. catalog.Fields.Select(f => Document.Markers.Marker(f.Name)),
                .. catalog.Functions.Select(f => f.Marker(Document.Markers)),
            ]
            : [];

    /// <summary>
    /// Imports a field list or a script of callable helpers, naming the catalog after
    /// the file unless the user has typed a name.
    ///
    /// One entry point for both, because which one a file is can be seen rather than
    /// asked: a script yields function signatures and a field list does not. The two
    /// halves merge into the catalog rather than replacing it, so importing a script
    /// beside an existing field list adds to it, and re-importing either replaces only
    /// its own half.
    /// </summary>
    public void ImportFieldCatalog(string text, string suggestedName)
    {
        string name = NewCatalogName.Trim();
        if (name.Length == 0)
        {
            name = suggestedName.Trim();
        }

        Core.Fields.FieldCatalog? existing = _catalogs.FirstOrDefault(
            c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        Core.Fields.FieldCatalogImport read =
            Core.Fields.FieldCatalogImport.Read(text, Document.Markers);

        // Each half replaces only itself, so importing a script beside an existing field
        // list adds to it rather than wiping it.
        IReadOnlyList<Core.Fields.FieldDefinition> fields =
            read.Fields.Count > 0 ? read.Fields : existing?.Fields ?? [];
        IReadOnlyList<Core.Fields.FieldFunction> functions =
            read.Functions.Count > 0 ? read.Functions : existing?.Functions ?? [];

        Core.Fields.FieldCatalogResult result = _fieldCatalogStore.Add(
            new Core.Fields.FieldCatalog(name, fields) { Functions = functions });
        ApplyFieldCatalogs(result.Catalogs);

        if (result.Error is not null)
        {
            Notify($"Could not save the catalog: {result.Error}");
            return;
        }

        NewCatalogName = string.Empty;

        // Binding the label to what was just imported is the point of importing it.
        _restoring = true;
        SelectedFieldCatalog = _catalogs.FirstOrDefault(
            c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        _restoring = false;
        RecordUndo("doc-catalog");
        ScheduleRender();
        Notify($"Imported {read.Describe()} as \"{name}\"");
    }

    /// <summary>Name to save the next import under; empty means "use the file's name".</summary>
    [ObservableProperty]
    public partial string NewCatalogName { get; set; } = string.Empty;

    private void RemoveFieldCatalog(Core.Fields.FieldCatalog catalog)
    {
        Core.Fields.FieldCatalogResult result = _fieldCatalogStore.Remove(catalog.Name);
        ApplyFieldCatalogs(result.Catalogs);
        StatusText = result.Error is null
            ? $"Removed the catalog \"{catalog.Name}\""
            : $"Could not update the catalogs file: {result.Error}";
        ScheduleRender();
    }

    private void ApplyFieldCatalogs(IReadOnlyList<Core.Fields.FieldCatalog> catalogs)
    {
        _catalogs = catalogs;
        FieldCatalogs.Clear();
        foreach (Core.Fields.FieldCatalog catalog in catalogs)
        {
            FieldCatalogs.Add(new FieldCatalogEntryViewModel(catalog, RemoveFieldCatalog));
        }

        OnPropertyChanged(nameof(HasFieldCatalogs));
        OnPropertyChanged(nameof(CatalogChoices));
        OnPropertyChanged(nameof(SelectedFieldCatalog));
        OnPropertyChanged(nameof(FieldSuggestions));
    }

    /// <summary>What the picker offers: the installed catalogs, plus a null entry so a
    /// label can be unbound again without removing anything.</summary>
    public IReadOnlyList<Core.Fields.FieldCatalog?> CatalogChoices =>
        [null, .. _catalogs];

    /// <summary>
    /// Continuous stock: a roll with no gaps or die cuts. The label stops having a fixed
    /// height and becomes exactly as long as its content, so the height box turns into a
    /// readout, the corner radius stops meaning anything, and the ZPL gains ^MNN.
    /// </summary>
    public bool IsContinuous
    {
        get => Document.IsContinuous;
        set
        {
            if (Document.IsContinuous == value)
            {
                return;
            }

            Document.IsContinuous = value;

            // Both directions need the box re-read at once: entering hands it over to
            // the measurement, leaving puts the stored die-cut height back rather than
            // stranding whatever the content last measured.
            SyncLabelLength();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasFixedLength));
            OnPropertyChanged(nameof(LengthHint));
            if (!_restoring)
            {
                // Hand-editing the stock kind means the picked media no longer
                // describes the label, exactly as editing its size does. Saving this
                // size as a preset should record the same kind of stock, or the preset
                // would carry a height that means nothing.
                SelectedMedia = null;
                NewMediaContinuous = value;
                RecordUndo("doc-continuous");
                ScheduleRender();
            }
        }
    }

    /// <summary>True on die-cut stock, where the height and the corner radius are the
    /// user's to set. Drives the enabled state of both.</summary>
    public bool HasFixedLength => !Document.IsContinuous;

    public string LengthHint => Document.IsContinuous
        ? "Measured from the content"
        : string.Empty;

    /// <summary>Markers this label uses that its field catalog does not list. Shown
    /// under the catalog picker rather than in the toolbar summary, because that is where
    /// the reader can do something about it.</summary>
    [ObservableProperty]
    public partial string UnknownFieldWarning { get; set; } = string.Empty;

    /// <summary>Warn when a symbol's quiet zone is not clear. A design aid: it only ever
    /// produces warnings, and never changes a byte of the generated ZPL.</summary>
    public bool CheckQuietZones
    {
        get => Document.CheckQuietZones;
        set
        {
            if (Document.CheckQuietZones == value)
            {
                return;
            }

            Document.CheckQuietZones = value;
            OnPropertyChanged();
            if (!_restoring)
            {
                RecordUndo("doc-quiet-zones");
                ScheduleRender();
            }
        }
    }

    /// <summary>Design grid pitch in millimeters; 0 is off. Draws and snaps, because
    /// wanting one without the other is not a real state.</summary>
    public double GridPitchMm
    {
        get => Document.GridPitchMm;
        set
        {
            if (Math.Abs(Document.GridPitchMm - value) < 0.0001)
            {
                return;
            }

            Document.GridPitchMm = value;
            OnPropertyChanged();
            if (!_restoring)
            {
                RecordUndo("doc-grid");
                ScheduleRender();
                Notify(value <= 0
                    ? "Grid off"
                    : FormattableString.Invariant($"Grid at {value:0.##} mm; drags snap to it"));
            }
        }
    }

    /// <summary>Blank stock left after the last ink on continuous media, so the next
    /// label has something to start after.</summary>
    public decimal ContinuousMarginMm
    {
        get => (decimal)Document.ContinuousMarginMm;
        set
        {
            double next = Math.Clamp((double)value, 0, 50);
            if (Math.Abs(next - Document.ContinuousMarginMm) < 0.0001)
            {
                return;
            }

            Document.ContinuousMarginMm = next;
            OnPropertyChanged();
            if (!_restoring)
            {
                RecordUndo("doc-gap");
                ScheduleRender();
            }
        }
    }

    /// <summary>Die-cut corner radius in mm. Describes the physical stock: it shapes the
    /// canvas and the PDF, never the ZPL.</summary>
    public decimal CornerRadiusMm
    {
        get => (decimal)Document.CornerRadiusMm;
        set
        {
            double next = Math.Clamp((double)value, 0, 50);
            if (Math.Abs(next - Document.CornerRadiusMm) < 0.0001)
            {
                return;
            }

            Document.CornerRadiusMm = next;
            OnPropertyChanged();
            if (!_restoring)
            {
                RecordUndo("doc-radius");
                ScheduleRender();
            }
        }
    }

    /// <summary>Copies to print (^PQ); job settings live on the document.</summary>
    public decimal PrintCopies
    {
        get => Document.Print.Copies;
        set => EditPrintSetting((int)value, 1, 9999, Document.Print.Copies,
            v => Document.Print.Copies = v, "print-copies");
    }

    /// <summary>Darkness adjustment (^MD), -30..30; 0 keeps the printer default.</summary>
    public decimal PrintDarkness
    {
        get => Document.Print.DarknessDelta;
        set => EditPrintSetting((int)value, -30, 30, Document.Print.DarknessDelta,
            v => Document.Print.DarknessDelta = v, "print-darkness");
    }

    /// <summary>Print speed (^PR) in inches per second; 0 keeps the printer default
    /// (the generator clamps emitted values to the ^PR 2..14 range).</summary>
    public decimal PrintSpeed
    {
        get => Document.Print.SpeedIps;
        set => EditPrintSetting((int)value, 0, 14, Document.Print.SpeedIps,
            v => Document.Print.SpeedIps = v, "print-speed");
    }

    private void EditPrintSetting(int value, int min, int max, int current, Action<int> apply,
        string undoKey, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        int next = Math.Clamp(value, min, max);
        if (next == current)
        {
            return;
        }

        apply(next);
        OnPropertyChanged(property);
        if (!_restoring)
        {
            RecordUndo(undoKey);
            ScheduleRender();
        }
    }

    /// <summary>Media picked from the catalog; applying it sets the label size.
    /// Cleared when the size is edited by hand so the field never lies.</summary>
    [ObservableProperty]
    public partial Core.Media.StockMedia? SelectedMedia { get; set; }

    [ObservableProperty]
    public partial DensityOption? SelectedDensity { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Path of the open .lfl file; null until first save.</summary>
    [ObservableProperty]
    public partial string? CurrentFilePath { get; set; }

    [ObservableProperty]
    public partial string PrinterHost { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal PrinterPort { get; set; } = Core.Printing.RawNetworkPrinter.DefaultPort;

    public IReadOnlyList<Core.Printers.PrinterProfile> Printers => Core.Printers.PrinterCatalog.All;

    /// <summary>Printer queues installed in Windows (USB path); empty elsewhere.</summary>
    public IReadOnlyList<string> WindowsPrinters { get; } = LoadWindowsPrinters();

    [ObservableProperty]
    public partial string? SelectedWindowsPrinter { get; set; }

    [ObservableProperty]
    public partial Core.Printers.PrinterProfile? SelectedPrinter { get; set; }

    /// <summary>Head-width/density warnings for the selected printer; empty when fine.</summary>
    [ObservableProperty]
    public partial string PrinterWarning { get; set; } = string.Empty;

    /// <summary>Document-wide barcode validation summary; empty when every barcode is
    /// encodable. Refreshed on each render so it tracks edits without being selected.</summary>
    [ObservableProperty]
    public partial string ValidationWarning { get; set; } = string.Empty;

    /// <summary>Elements off the label or crossing its edge, summarized for display;
    /// empty when everything sits inside. Refreshed on each render.</summary>
    [ObservableProperty]
    public partial string PlacementWarning { get; set; } = string.Empty;

    /// <summary>Why a printer-side counter or clock the document asked for is not being
    /// used. Empty when everything the label requested is expressible in ZPL, which is
    /// the normal case; a fallback still prints correctly, just more slowly.</summary>
    [ObservableProperty]
    public partial string VariableWarning { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool CanUndo { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    public partial bool CanRedo { get; set; }

    /// <param name="userMediaStore">Where the user's own media presets live; defaults
    /// to the per-user file. Injected so a harness can exercise presets without
    /// touching what the person using the app has saved.</param>
    public DesignerViewModel(
        Core.Media.UserMediaStore? userMediaStore = null,
        Core.Fields.FieldCatalogStore? fieldCatalogStore = null,
        RecoveryStore? recoveryStore = null)
    {
        _userMediaStore = userMediaStore ?? new Core.Media.UserMediaStore();
        _fieldCatalogStore = fieldCatalogStore ?? new Core.Fields.FieldCatalogStore();
        _recovery = recoveryStore ?? new RecoveryStore();
        Selection.Changed += (_, _) => OnSelectionChanged();

        ApplyUserMedia(_userMediaStore.Load());
        ApplyFieldCatalogs(_fieldCatalogStore.Load());

        // Property setters record undo states; construction must not, or the
        // history would start with a spurious extra document before the baseline.
        _restoring = true;
        Document = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        SelectedDensity = Densities[0];
        SelectedPrinter = Core.Printers.PrinterProfile.Any;
        _restoring = false;

        RecordUndo();
        ScheduleRender();
        OfferRecovery();
    }

    /// <summary>
    /// Work left behind by a session that did not shut down, offered rather than restored.
    ///
    /// Never opened automatically. A snapshot is what someone was in the middle of, and
    /// deciding for them would replace whatever they meant to open this time; the offer
    /// costs one click and the alternative costs the wrong document.
    /// </summary>
    [ObservableProperty]
    public partial string RecoveryOffer { get; set; } = string.Empty;

    private Core.Io.RecoverySnapshot? _pendingRecovery;

    public bool HasRecoveryOffer => _pendingRecovery is not null;

    private void OfferRecovery()
    {
        _pendingRecovery = _recovery.FindAbandoned().FirstOrDefault();
        if (_pendingRecovery is null)
        {
            return;
        }

        string origin = _pendingRecovery.OriginalPath is { } path
            ? Path.GetFileName(path)
            : "a label that was never saved";
        RecoveryOffer =
            $"LabelForge closed unexpectedly with unsaved changes to {origin} "
            + $"({_pendingRecovery.SavedAtUtc.ToLocalTime():g}).";
        OnPropertyChanged(nameof(HasRecoveryOffer));
    }

    /// <summary>Opens the recovered document. It deliberately arrives with no file path
    /// when it never had one, so saving asks where it goes instead of inventing a
    /// location, and with its original path when it had one.</summary>
    [RelayCommand]
    private void RecoverDocument()
    {
        if (_pendingRecovery is not { } snapshot)
        {
            return;
        }

        try
        {
            LoadDocument(LabelDocumentJson.Deserialize(snapshot.Lfl), snapshot.OriginalPath);
            Notify("Recovered the unsaved changes. Save the label to keep them.");
        }
        catch (Exception ex)
        {
            Notify($"The recovered file could not be read: {ex.Message}");
        }

        DismissRecovery();
    }

    /// <summary>Throws the snapshot away. Both answers are decisions, and only an
    /// undecided one is worth keeping for next time.</summary>
    [RelayCommand]
    private void DismissRecovery()
    {
        if (_pendingRecovery is { } snapshot)
        {
            _recovery.Discard(snapshot.SnapshotPath);
        }

        _pendingRecovery = null;
        RecoveryOffer = string.Empty;
        OnPropertyChanged(nameof(HasRecoveryOffer));
    }

    /// <summary>
    /// Takes a snapshot if the document has moved since the last one.
    ///
    /// Driven by the render pass rather than a clock, because that is what already fires
    /// on every edit and is already debounced; a timer would be a second schedule saying
    /// the same thing. Comparing against the last snapshot keeps an idle session from
    /// rewriting the same bytes.
    /// </summary>
    private void SnapshotForRecovery()
    {
        try
        {
            string lfl = SerializeDocument();
            if (string.Equals(lfl, _lastSnapshot, StringComparison.Ordinal))
            {
                return;
            }

            _lastSnapshot = lfl;
            _recovery.Save(lfl, CurrentFilePath);
        }
        catch (Exception)
        {
            // The safety net failing must never take the editor with it.
        }
    }

    /// <summary>Called when the document reaches a file of its own, and on shutdown. The
    /// snapshot's existence is what says work was lost, so it has to go the moment that
    /// stops being true.</summary>
    public void ClearRecovery()
    {
        _lastSnapshot = null;
        _recovery.Clear();
    }

    /// <summary>Ends the session cleanly, which is what stops the next start offering to
    /// recover work that was never lost.</summary>
    public void ShutDown() => _recovery.Dispose();

    /// <summary>Called continuously while the canvas drags or resizes: the model is
    /// already updated, so re-render and refresh the panel, but record no undo.
    /// Uses a much shorter debounce than typing so content tracks the pointer.</summary>
    public void NotifyDocumentPreview()
    {
        SelectionProperties?.Refresh();
        ScheduleRender(delayMs: 40);
    }

    private void OnSelectionChanged()
    {
        HasSelection = Selection.Count > 0;
        SelectionCount = Selection.Count;
        IsSingleSelection = Selection.Count == 1;
        HasMultiSelection = Selection.Count > 1;
        SelectionProperties = Selection.Count == 1
            ? CreatePropertiesEditor(Selection.Primary)
            : null;

        // Selecting on the canvas highlights the row, guarded so the row's own setter
        // does not bounce the selection straight back.
        _syncingOutline = true;
        SelectedOutlineRow = Outline.FirstOrDefault(r => ReferenceEquals(r.Element, Selection.Primary));
        _syncingOutline = false;
    }

    /// <summary>Serializes the current document in the .lfl format.</summary>
    public string SerializeDocument() => LabelDocumentJson.Serialize(Document);

    /// <summary>Renders the current document to PNG bytes (for export).</summary>
    public Task<byte[]> RenderPngAsync()
    {
        LabelDocument document = Document;
        return Task.Run(() => _renderer
            .Render(new ZplGenerator().Generate(document), document.WidthMm, document.HeightMm, document.Dpmm)
            .Png);
    }

    /// <summary>Renders the current document to a PDF page at its physical size.</summary>
    public Task<byte[]> RenderPdfAsync()
    {
        LabelDocument document = Document;
        return Task.Run(() =>
        {
            byte[] png = _renderer
                .Render(new ZplGenerator().Generate(document), document.WidthMm, document.HeightMm, document.Dpmm)
                .Png;
            return Core.Export.PdfExporter.FromPng(
                png, document.WidthMm, document.HeightMm, document.EffectiveCornerRadiusMm);
        });
    }

    /// <summary>Replaces the document (new file or opened .lfl) and resets history.</summary>
    public void LoadDocument(LabelDocument document, string? path)
    {
        _restoring = true;
        try
        {
            Document = document;
            WidthMm = (decimal)document.WidthMm;
            HeightMm = (decimal)document.HeightMm;
            SelectedMedia = null;
            SelectedDensity = Densities.FirstOrDefault(d => d.Dpmm == document.Dpmm) ?? Densities[0];
            Selection.Clear();
        }
        finally
        {
            _restoring = false;
        }

        CurrentFilePath = path;
        NotifyPrintSettingsChanged();
        RefreshVariables();
        RefreshOutline();
        UpdatePrinterWarning();
        _history.Clear();
        _lastRecordTicks = 0;
        _lastCoalesceKey = null;
        RecordUndo();
        ScheduleRender();
    }

    /// <summary>Re-reads the document-backed fields after the document is replaced
    /// (open, new, undo), since they have no observable property of their own.</summary>
    private void NotifyPrintSettingsChanged()
    {
        OnPropertyChanged(nameof(PrintCopies));
        OnPropertyChanged(nameof(PrintDarkness));
        OnPropertyChanged(nameof(PrintSpeed));
        OnPropertyChanged(nameof(CornerRadiusMm));
        OnPropertyChanged(nameof(IsContinuous));
        OnPropertyChanged(nameof(HasFixedLength));
        OnPropertyChanged(nameof(LengthHint));
        OnPropertyChanged(nameof(ContinuousMarginMm));
        OnPropertyChanged(nameof(CheckQuietZones));
        OnPropertyChanged(nameof(GridPitchMm));
        OnPropertyChanged(nameof(SelectedFieldCatalog));
        OnPropertyChanged(nameof(FieldSuggestions));
    }

    [RelayCommand]
    private void NewDocument() =>
        LoadDocument(new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 }, path: null);

    /// <summary>A demo label showing each element type; reachable from File.</summary>
    [RelayCommand]
    private void LoadSample()
    {
        var doc = new LabelDocument { WidthMm = 100, HeightMm = 60, Dpmm = 8 };
        SeedSampleLabel(doc);
        LoadDocument(doc, path: null);
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        string host = PrinterHost.Trim();
        if (host.Length == 0)
        {
            StatusText = "Enter the printer address first";
            return;
        }

        try
        {
            StatusText = $"Sending to {host}...";
            PrintJobResult job = BuildPrintJob();

            // The connection phase is bounded inside SendAsync; a timeout surfaces as a
            // TimeoutException whose message already names the unreachable endpoint.
            await Core.Printing.RawNetworkPrinter.SendAsync(host, (int)PrinterPort, job.Zpl);
            StatusText = $"Sent to {host}:{(int)PrinterPort}{DescribeRun(job)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Print failed: {ex.Message}";
        }
    }

    private static IReadOnlyList<string> LoadWindowsPrinters()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            return Core.Printing.WindowsRawPrinter.GetInstalledPrinters();
        }
        catch
        {
            return [];
        }
    }

    [RelayCommand]
    private async Task PrintWindowsAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            StatusText = "Windows printing is only available on Windows";
            return;
        }

        if (SelectedWindowsPrinter is not { Length: > 0 } name)
        {
            StatusText = "Pick a Windows printer first";
            return;
        }

        try
        {
            StatusText = $"Spooling to {name}...";
            PrintJobResult job = BuildPrintJob();
            await SendToWindowsPrinterAsync(name, job.Zpl);
            StatusText = $"Sent to {name}{DescribeRun(job)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Print failed: {ex.Message}";
        }
    }

    /// <summary>Trailing summary of what a run produced, so "sent" also says how many
    /// labels went out and whether the printer or this PC numbered them.</summary>
    /// <summary>
    /// The exact bytes a print run sends, stamped now.
    ///
    /// Both printers and the print-job export call this and nothing else builds a run, so
    /// what gets exported for a support ticket or a regression fixture cannot drift from
    /// what the printer would receive. That is the whole value of the export: a file that
    /// merely resembles the job is worse than no file, because it is trusted.
    ///
    /// It is not the same thing as the ZPL pane, and deliberately so. That shows one
    /// label; a run whose counter this machine expands is one block per copy, and the
    /// timestamp is taken when the job is built rather than when the canvas last drew.
    /// </summary>
    public PrintJobResult BuildPrintJob() => PrintJob.Build(Document, DateTime.Now);

    /// <summary>What a built run contains, for the status line after an export.</summary>
    public string DescribeJob(PrintJobResult job)
    {
        ArgumentNullException.ThrowIfNull(job);
        string labels = job.Labels == 1 ? "1 label" : $"{job.Labels} labels";
        string blocks = CountBlocks(job.Zpl) == 1 ? "one block" : $"{CountBlocks(job.Zpl)} blocks";
        string numbering = job.CountedByPrinter ? ", serialized by the printer" : string.Empty;
        return $"{labels} in {blocks}{numbering}";
    }

    private static int CountBlocks(string zpl)
    {
        int count = 0;
        int at = zpl.IndexOf("^XA", StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = zpl.IndexOf("^XA", at + 3, StringComparison.Ordinal);
        }

        return count;
    }

    private string DescribeRun(PrintJobResult job)
    {
        if (job.Labels <= 1)
        {
            return string.Empty;
        }

        string capped = job.Labels < Document.Print.Copies
            ? $", capped from {Document.Print.Copies}"
            : string.Empty;
        string numbering = job.CountedByPrinter ? ", serialized by the printer" : string.Empty;
        return $" ({job.Labels} labels{numbering}{capped})";
    }

    /// <summary>Guarded so the platform-specific spooler call has a Windows-only context;
    /// callers reach it only after an <see cref="OperatingSystem.IsWindows"/> check.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Task SendToWindowsPrinterAsync(string name, string zpl) =>
        Task.Run(() => Core.Printing.WindowsRawPrinter.Send(name, zpl));

    /// <summary>Called by the view when the canvas edits the model (drag, resize, nudge).</summary>
    public void NotifyDocumentEdited()
    {
        SelectionProperties?.Refresh();
        // Key on the set of moved elements so a continuous drag or a run of nudges of
        // the same selection coalesces, but editing a different selection does not.
        string key = "canvas:" + string.Join(",", Selection.Items.Select(e => e.Id));
        RecordUndo(key);
        ScheduleRender();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_history.Undo() is { } state)
        {
            RestoreSnapshot(state);
        }

        UpdateUndoState();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_history.Redo() is { } state)
        {
            RestoreSnapshot(state);
        }

        UpdateUndoState();
    }

    /// <summary>True while an insert is armed; the next canvas click places it.</summary>
    [ObservableProperty]
    public partial bool IsPlacing { get; set; }

    /// <summary>Name of the armed insert tool ("Text", "Box", ...), null when none.
    /// Drives the highlighted state of the tool buttons in the sidebar.</summary>
    [ObservableProperty]
    public partial string? ArmedTool { get; set; }

    partial void OnArmedToolChanged(string? value)
    {
        OnPropertyChanged(nameof(IsTextArmed));
        OnPropertyChanged(nameof(IsBoxArmed));
        OnPropertyChanged(nameof(IsLineArmed));
        OnPropertyChanged(nameof(IsEllipseArmed));
        OnPropertyChanged(nameof(IsDiagonalArmed));
        OnPropertyChanged(nameof(IsBarcodeArmed));
        OnPropertyChanged(nameof(IsQrArmed));
        OnPropertyChanged(nameof(IsDataMatrixArmed));
        OnPropertyChanged(nameof(IsPdf417Armed));
        OnPropertyChanged(nameof(IsImageArmed));
    }

    public bool IsTextArmed => ArmedTool == "Text";

    public bool IsBoxArmed => ArmedTool == "Box";

    public bool IsLineArmed => ArmedTool == "Line";

    public bool IsEllipseArmed => ArmedTool == "Ellipse";

    public bool IsDiagonalArmed => ArmedTool == "Diagonal";

    public bool IsBarcodeArmed => ArmedTool == "Barcode";

    public bool IsQrArmed => ArmedTool == "QR";

    public bool IsDataMatrixArmed => ArmedTool == "DataMatrix";

    public bool IsPdf417Armed => ArmedTool == "Pdf417";

    public bool IsImageArmed => ArmedTool == "Image";

    private Func<Element>? _pendingFactory;

    [RelayCommand]
    private void AddText() => ArmInsert("Text",
        () => new TextElement { Text = "New text", FontHeightDots = 40 });

    [RelayCommand]
    private void AddBox() => ArmInsert("Box",
        () => new BoxElement { WidthDots = 240, HeightDots = 140, ThicknessDots = 3 });

    [RelayCommand]
    private void AddLine() => ArmInsert("Line",
        () => new LineElement { LengthDots = 240, ThicknessDots = 3 });

    [RelayCommand]
    private void AddEllipse() => ArmInsert("Ellipse",
        () => new EllipseElement { WidthDots = 200, HeightDots = 140, ThicknessDots = 3 });

    /// <summary>Thickness starts at 3 rather than at ZPL's default of 1, because the
    /// offline renderer draws a one-dot diagonal as nothing at all and a new element that
    /// appears to have failed is a bad first impression of a working command.</summary>
    [RelayCommand]
    private void AddDiagonal() => ArmInsert("Diagonal",
        () => new DiagonalLineElement { WidthDots = 200, HeightDots = 140, ThicknessDots = 3 });

    [RelayCommand]
    private void AddBarcode() => ArmInsert("Barcode",
        () => new BarcodeElement { Data = "123456", HeightDots = 100, ModuleWidthDots = 2 });

    [RelayCommand]
    private void AddQr() => ArmInsert("QR",
        () => new QrCodeElement { Data = "https://example.com", Magnification = 5 });

    [RelayCommand]
    private void AddDataMatrix() => ArmInsert("DataMatrix",
        () => new DataMatrixElement { Data = "LF-000123", ModuleSizeDots = 4 });

    [RelayCommand]
    private void AddPdf417() => ArmInsert("Pdf417",
        () => new Pdf417Element { Data = "LF-000123", DataColumns = 5 });

    /// <summary>Arms image placement with an already-picked file (the file dialog
    /// runs in the view). The image scales to fit a 240-dot box, keeping aspect.</summary>
    public void ArmInsertImage(byte[] imageData, int pixelWidth, int pixelHeight)
    {
        const int fitDots = 240;
        double scale = Math.Min(
            (double)fitDots / Math.Max(pixelWidth, 1),
            (double)fitDots / Math.Max(pixelHeight, 1));
        ArmInsert("Image", () => new ImageElement
        {
            ImageData = imageData,
            SourcePixelWidth = pixelWidth,
            SourcePixelHeight = pixelHeight,
            WidthDots = Math.Max((int)Math.Round(pixelWidth * scale), 8),
            HeightDots = Math.Max((int)Math.Round(pixelHeight * scale), 8),
        });
    }

    /// <summary>
    /// Replaces the document with a ZPL label read back into the model.
    ///
    /// The file is parsed at the density currently selected here, because ZPL states
    /// ^PW and ^LL in dots and never says which printer it was written for; that is a
    /// fact about the format, not a gap to paper over with a guess. The result is not
    /// given a file path: it came from a .zpl, so saving should ask where the .lfl goes
    /// rather than offering to overwrite the source.
    /// </summary>
    public void ImportZplDocument(string zpl, string sourceName)
    {
        _importedZpl = zpl;
        _importedName = sourceName;
        OpenImportedBlock(ZplDocumentImport.FromZpl(zpl, Document.Dpmm), sourceName);
    }

    /// <summary>The ZPL an import came from, kept so the file's other labels stay
    /// reachable. Real files hold several: one in the corpus holds twenty-seven, and
    /// without this an import is a one-way door onto whichever the parser picked.</summary>
    private string? _importedZpl;
    private string? _importedName;

    /// <summary>The other labels in the imported file, for the picker. Empty when the
    /// file held one, which is when there is nothing to choose between.</summary>
    public ObservableCollection<ImportedBlockViewModel> ImportedBlocks { get; } = [];

    public bool HasImportedBlocks => ImportedBlocks.Count > 1;

    /// <summary>
    /// Which label of the imported file is open.
    ///
    /// Setting it re-reads that block and replaces the document, which is what importing
    /// did in the first place. It is deliberately an explicit act on a strip that names
    /// the file, rather than something reachable by accident, because it discards whatever
    /// has been done since.
    /// </summary>
    [ObservableProperty]
    public partial ImportedBlockViewModel? SelectedImportedBlock { get; set; }

    partial void OnSelectedImportedBlockChanged(ImportedBlockViewModel? value)
    {
        if (_switchingBlock || value is null || _importedZpl is null || _importedName is null)
        {
            return;
        }

        OpenImportedBlock(
            ZplDocumentImport.FromZpl(_importedZpl, Document.Dpmm, value.Index), _importedName);
    }

    private bool _switchingBlock;

    private void OpenImportedBlock(ZplDocumentImportResult result, string sourceName)
    {
        LoadDocument(result.Document, path: null);

        // Rebuilt here rather than only on the first import, because the density can
        // change between one block and the next and the counts come from the parse.
        _switchingBlock = true;
        try
        {
            ImportedBlocks.Clear();
            for (int i = 0; i < result.BlockElementCounts.Count; i++)
            {
                ImportedBlocks.Add(new ImportedBlockViewModel(i, result.BlockElementCounts[i]));
            }

            SelectedImportedBlock = ImportedBlocks.FirstOrDefault(b => b.Index == result.SelectedIndex);
        }
        finally
        {
            _switchingBlock = false;
        }

        ImportedFrom = HasImportedBlocks ? sourceName : string.Empty;
        OnPropertyChanged(nameof(HasImportedBlocks));

        var notes = new List<string>();
        if (result.LabelCount > 1)
        {
            notes.Add($"Label {result.SelectedIndex + 1} of {result.LabelCount} in the file.");
        }

        // Ahead of the warnings, because it is the one thing the user most likely has to
        // act on: a size nobody stated is the size we worked out, and only they know the
        // stock. It is kept out of the warning list itself, which is for real losses.
        if (result.MeasuredSize is { } measured)
        {
            notes.Add(measured);
        }

        notes.AddRange(result.Warnings);

        string what = result.Document.Elements.Count == 1
            ? "1 element"
            : $"{result.Document.Elements.Count} elements";
        Notify(result.Document.Elements.Count == 0
            ? $"Nothing in {sourceName} could be turned into elements. "
              + string.Join(" ", result.Warnings)
            : $"Imported {what} from {sourceName}. {string.Join(" ", notes)}".TrimEnd());
    }

    /// <summary>Name of the file the open label came from, while more of it is reachable.</summary>
    [ObservableProperty]
    public partial string ImportedFrom { get; set; } = string.Empty;

    /// <summary>Lets go of the imported file, so the strip stops offering labels from
    /// something the user has moved on from.</summary>
    [RelayCommand]
    private void CloseImportedFile()
    {
        _importedZpl = null;
        _importedName = null;
        _switchingBlock = true;
        ImportedBlocks.Clear();
        SelectedImportedBlock = null;
        _switchingBlock = false;
        ImportedFrom = string.Empty;
        OnPropertyChanged(nameof(HasImportedBlocks));
    }

    /// <summary>
    /// Adds every graphic an existing ZPL label carries to this document as editable
    /// images (the file dialog runs in the view, like the image import).
    ///
    /// Each one lands where the source label drew it rather than under the mouse: a
    /// stamp's position relative to the rest of the layout is usually the reason to
    /// import it at all. On a smaller label some of them will land off the label, which
    /// the pasteboard shows honestly and the status line counts, instead of being
    /// silently shoved inside. The whole import is one undo step.
    /// </summary>
    public void ImportGraphicsFromZpl(string zpl, string sourceName)
    {
        ZplGraphicImportResult result = ZplGraphicImport.FromZpl(zpl);
        if (result.Graphics.Count == 0)
        {
            Notify(result.Warnings.Count > 0
                ? string.Join(" ", result.Warnings)
                : $"No graphics found in {sourceName}");
            return;
        }

        int nextZ = Document.Elements.Count == 0
            ? 0
            : Document.Elements.Max(e => e.ZOrder) + 1;

        var added = new List<Element>();
        foreach (ImportedGraphic graphic in result.Graphics)
        {
            graphic.Element.ZOrder = nextZ++;
            Document.Elements.Add(graphic.Element);
            added.Add(graphic.Element);
        }

        Selection.SetMany(added);
        RecordUndo();
        ScheduleRender();

        var notes = new List<string>(result.Warnings);
        int offLabel = added.Count(
            e => !ElementPlacement.IsPrintable(e, Document));
        if (offLabel > 0)
        {
            notes.Add(offLabel == added.Count
                ? "The source label is larger than this one, so they all landed on the pasteboard."
                : $"{offLabel} landed off the label, on the pasteboard.");
        }

        string what = added.Count == 1 ? "1 graphic" : $"{added.Count} graphics";
        Notify(notes.Count == 0
            ? $"Imported {what} from {sourceName}"
            : $"Imported {what} from {sourceName}. {string.Join(" ", notes)}");
    }

    /// <summary>Arms the insert: the mouse becomes a placement tool until a click or Esc.</summary>
    private void ArmInsert(string tool, Func<Element> factory)
    {
        _pendingFactory = factory;
        IsPlacing = true;
        ArmedTool = tool;
        StatusText = "Click the canvas to place the new element (Esc cancels)";
    }

    /// <summary>Called by the canvas with the clicked position in dots.</summary>
    public void PlaceAt(int x, int y)
    {
        if (_pendingFactory is null)
        {
            IsPlacing = false;
            return;
        }

        Element element = _pendingFactory();
        element.X = Math.Clamp(x, 0, Math.Max(Document.WidthDots - 1, 0));
        element.Y = Math.Clamp(y, 0, Math.Max(Document.HeightDots - 1, 0));
        element.ZOrder = Document.Elements.Count == 0
            ? 0
            : Document.Elements.Max(e => e.ZOrder) + 1;
        Document.Elements.Add(element);
        Selection.Set(element);

        _pendingFactory = null;
        IsPlacing = false;
        ArmedTool = null;
        StatusText = string.Empty;
        RecordUndo();
        ScheduleRender();
    }

    public void CancelInsert()
    {
        _pendingFactory = null;
        IsPlacing = false;
        ArmedTool = null;
        StatusText = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AlignLeft() => ApplyAlign(AlignEdge.Left);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AlignCenterHorizontal() => ApplyAlign(AlignEdge.CenterHorizontal);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AlignRight() => ApplyAlign(AlignEdge.Right);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AlignTop() => ApplyAlign(AlignEdge.Top);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AlignMiddle() => ApplyAlign(AlignEdge.Middle);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AlignBottom() => ApplyAlign(AlignEdge.Bottom);

    /// <summary>Distribution needs at least three elements; with fewer there is no
    /// middle gap to equalize.</summary>
    public bool CanDistribute => SelectionCount >= 3;

    [RelayCommand(CanExecute = nameof(CanDistribute))]
    private void DistributeHorizontal() => ApplyDistribute(horizontal: true);

    [RelayCommand(CanExecute = nameof(CanDistribute))]
    private void DistributeVertical() => ApplyDistribute(horizontal: false);

    private void ApplyAlign(AlignEdge edge)
    {
        if (Aligner.Align(Selection.Items.ToList(), edge, Document.WidthDots, Document.HeightDots))
        {
            SelectionProperties?.Refresh();
            RecordUndo();
            ScheduleRender();
        }
    }

    private void ApplyDistribute(bool horizontal)
    {
        if (Aligner.Distribute(Selection.Items.ToList(), horizontal))
        {
            SelectionProperties?.Refresh();
            RecordUndo();
            ScheduleRender();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy()
    {
        if (Selection.Count > 0)
        {
            // Preserve draw order so a pasted group stacks like the original.
            _clipboardElement = LabelDocumentJson.SerializeElements(
                Selection.Items.OrderBy(e => e.ZOrder));
            CanPaste = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste()
    {
        if (_clipboardElement is null)
        {
            return;
        }

        List<Element> elements = LabelDocumentJson.DeserializeElements(_clipboardElement);
        if (PlaceClones(elements))
        {
            // Re-serialize the placed (offset, clamped) copies so repeated pastes
            // cascade from where the last one landed instead of stacking.
            _clipboardElement = LabelDocumentJson.SerializeElements(elements);
        }
    }

    /// <summary>
    /// Pastes with the copied group's top-left at a point, rather than cascading from
    /// where the last paste landed.
    ///
    /// The cascade is right for repeated pastes from the keyboard, where the user has said
    /// nothing about position. A right-click has said exactly where, so honouring it is
    /// the whole difference between the two.
    /// </summary>
    public void PasteAt(int x, int y)
    {
        if (_clipboardElement is null)
        {
            return;
        }

        List<Element> clones = LabelDocumentJson.DeserializeElements(_clipboardElement);
        if (clones.Count == 0)
        {
            return;
        }

        // One delta for the whole group, so the copies keep their relative layout.
        int dx = x - clones.Min(e => e.X);
        int dy = y - clones.Min(e => e.Y);
        int nextZ = Document.Elements.Count == 0
            ? 0
            : Document.Elements.Max(e => e.ZOrder) + 1;

        foreach (Element element in clones)
        {
            element.Id = Guid.NewGuid();
            element.X = Math.Clamp(element.X + dx, 0, Math.Max(Document.WidthDots - 1, 0));
            element.Y = Math.Clamp(element.Y + dy, 0, Math.Max(Document.HeightDots - 1, 0));
            element.ZOrder = nextZ++;
            Document.Elements.Add(element);
        }

        Selection.SetMany(clones);
        RecordUndo();
        ScheduleRender();
    }

    /// <summary>Selects everything that can be seen. Hidden elements are left out: a
    /// selection is something you are about to act on, and acting on what you cannot see
    /// is how work gets lost.</summary>
    [RelayCommand]
    private void SelectAll() =>
        Selection.SetMany(Document.Elements.Where(e => e.IsVisible).OrderBy(e => e.ZOrder));

    /// <summary>Arms an insert and places it in one go, for the context menu: a click has
    /// already said where, so asking for a second one would be asking twice.</summary>
    public void InsertAt(System.Windows.Input.ICommand armCommand, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(armCommand);
        armCommand.Execute(null);
        PlaceAt(x, y);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        if (Selection.Count == 0)
        {
            return;
        }

        // Duplicate is independent of the clipboard: clone the selection directly so it
        // never overwrites what the user has copied.
        List<Element> clones = LabelDocumentJson.DeserializeElements(
            LabelDocumentJson.SerializeElements(Selection.Items.OrderBy(e => e.ZOrder)));
        PlaceClones(clones);
    }

    /// <summary>Adds cloned elements to the document with fresh ids, a +20 dot cascade
    /// offset, and z-order on top; selects them and records one undo step. One delta is
    /// applied to the whole group so relative offsets are preserved, and when the group
    /// reaches a label edge the cascade wraps back near the top-left instead of
    /// clamping, so repeated pastes never pile up on a single spot. Returns false when
    /// there was nothing to add.</summary>
    private bool PlaceClones(List<Element> clones)
    {
        if (clones.Count == 0)
        {
            return false;
        }

        int nextZ = Document.Elements.Count == 0
            ? 0
            : Document.Elements.Max(e => e.ZOrder) + 1;
        int dx = CascadeDelta(clones.Min(e => e.X), clones.Max(e => e.X), Math.Max(Document.WidthDots - 1, 0));
        int dy = CascadeDelta(clones.Min(e => e.Y), clones.Max(e => e.Y), Math.Max(Document.HeightDots - 1, 0));

        foreach (Element element in clones)
        {
            element.Id = Guid.NewGuid();
            element.X += dx;
            element.Y += dy;
            element.ZOrder = nextZ++;
            Document.Elements.Add(element);
        }

        Selection.SetMany(clones);
        RecordUndo();
        ScheduleRender();
        return true;
    }

    /// <summary>The cascade offset along one axis for a group whose origins span
    /// [min, max]: +20 while that fits within the label, otherwise wrap the group back
    /// so its first origin restarts at 20. A group whose origins span the whole axis
    /// stays put; it cannot cascade without leaving the label.</summary>
    private static int CascadeDelta(int min, int max, int limit)
    {
        const int step = 20;
        if (max + step <= limit)
        {
            return step;
        }

        int wrap = step - min;
        return max + wrap <= limit ? wrap : -min;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void BringToFront()
    {
        if (Selection.Count == 0)
        {
            return;
        }

        int nextZ = Document.Elements.Max(e => e.ZOrder) + 1;
        foreach (Element element in Selection.Items.OrderBy(e => e.ZOrder))
        {
            element.ZOrder = nextZ++;
        }

        RecordUndo();
        ScheduleRender();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SendToBack()
    {
        if (Selection.Count == 0)
        {
            return;
        }

        int nextZ = Document.Elements.Min(e => e.ZOrder) - Selection.Count;
        foreach (Element element in Selection.Items.OrderBy(e => e.ZOrder))
        {
            element.ZOrder = nextZ++;
        }

        RecordUndo();
        ScheduleRender();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Selection.Count == 0)
        {
            return;
        }

        foreach (Element element in Selection.Items.ToList())
        {
            Document.Elements.Remove(element);
        }

        Selection.Clear();
        RecordUndo();
        ScheduleRender();
    }

    private ElementPropertiesViewModel? CreatePropertiesEditor(Element? value)
    {
        ElementPropertiesViewModel? editor = BuildPropertiesEditor(value);
        if (editor is not null)
        {
            editor.SuggestionSource = () => FieldSuggestions;
        }

        return editor;
    }

    private ElementPropertiesViewModel? BuildPropertiesEditor(Element? value) => value switch
    {
        TextElement text => new TextPropertiesViewModel(text, Document, OnPanelEdited),
        BarcodeElement barcode => new BarcodePropertiesViewModel(barcode, Document, OnPanelEdited),
        QrCodeElement qr => new QrPropertiesViewModel(qr, Document, OnPanelEdited),
        DataMatrixElement dm => new DataMatrixPropertiesViewModel(dm, Document, OnPanelEdited),
        Pdf417Element pdf => new Pdf417PropertiesViewModel(pdf, Document, OnPanelEdited),
        ImageElement image => new ImagePropertiesViewModel(image, Document, OnPanelEdited),
        LineElement line => new LinePropertiesViewModel(line, Document, OnPanelEdited),
        BoxElement box => new BoxPropertiesViewModel(box, Document, OnPanelEdited),
        EllipseElement ellipse => new EllipsePropertiesViewModel(ellipse, Document, OnPanelEdited),
        DiagonalLineElement diagonal =>
            new DiagonalPropertiesViewModel(diagonal, Document, OnPanelEdited),
        _ => null,
    };

    private void OnPanelEdited(string property)
    {
        // Key on the edited element and property so typing in one field coalesces, but
        // moving to another field or another element starts a fresh undo step.
        RecordUndo($"panel:{Selection.Primary?.Id}:{property}");
        ScheduleRender();
    }

    partial void OnSelectedPrinterChanged(Core.Printers.PrinterProfile? value)
    {
        if (!_restoring && value is { IsAny: false })
        {
            // Adopting a printer pushes its density onto the label.
            SelectedDensity = Densities.FirstOrDefault(d => d.Dpmm == value.Dpmm) ?? SelectedDensity;
        }

        UpdatePrinterWarning();
    }

    private void UpdatePrinterWarning() =>
        PrinterWarning = SelectedPrinter is { IsAny: false } printer
            ? string.Join("; ", printer.Validate(Document))
            : string.Empty;

    partial void OnWidthMmChanged(decimal value)
    {
        if (_restoring)
        {
            return;
        }

        SelectedMedia = null;
        Document.WidthMm = (double)value;
        UpdatePrinterWarning();
        RecordUndo("doc-width");
        ScheduleRender();
    }

    partial void OnHeightMmChanged(decimal value)
    {
        if (_restoring)
        {
            return;
        }

        SelectedMedia = null;
        Document.HeightMm = (double)value;
        RecordUndo("doc-height");
        ScheduleRender();
    }

    /// <summary>Applies a catalog media to the label: both dimensions in one undo
    /// step. The width/height setters are bypassed via the restore guard so the
    /// apply does not clear the selection or record two extra steps.</summary>
    partial void OnSelectedMediaChanged(Core.Media.StockMedia? value)
    {
        if (_restoring || value is null)
        {
            return;
        }

        _restoring = true;
        WidthMm = (decimal)value.WidthMm;
        HeightMm = (decimal)value.HeightMm;
        CornerRadiusMm = (decimal)value.RadiusMm;

        // The roll's own kind comes with it, so a continuous stock stops pretending to
        // have a die-cut height the moment it is picked.
        IsContinuous = value.Continuous;
        _restoring = false;

        Document.WidthMm = value.WidthMm;
        Document.HeightMm = value.HeightMm;

        // The stock's own die-cut shape comes with it, so the canvas shows the label
        // that will actually come off the roll.
        Document.CornerRadiusMm = value.RadiusMm;
        UpdatePrinterWarning();
        RecordUndo();
        ScheduleRender();
        string kind = value.IsUserDefined ? "my media" : "media";
        StatusText = value.Continuous
            ? $"Applied {kind} {value.PartNumber}: continuous {value.WidthMm:0.#} mm roll, the length now follows the content"
            : $"Applied {kind} {value.PartNumber} ({value.SizeText})";
    }

    /// <summary>Saves the label's current size as one of the user's own media. Presets
    /// are per machine, not part of the document, so this records no undo step.</summary>
    [RelayCommand]
    private void SaveUserMedia()
    {
        string name = NewMediaName.Trim();
        if (name.Length == 0)
        {
            StatusText = "Name the media before saving it";
            return;
        }

        var media = Core.Media.StockMedia.UserDefined(
            name,
            (double)WidthMm,
            (double)HeightMm,
            NewMediaMaterial,
            (double)NewMediaRadiusMm,
            NewMediaContinuous);

        Core.Media.UserMediaResult result = _userMediaStore.Add(media);
        ApplyUserMedia(result.Entries);
        NewMediaName = string.Empty;
        NewMediaMaterial = string.Empty;

        // A failed write still leaves a usable preset in this session; say which it is
        // rather than reporting a success the next start would contradict.
        StatusText = result.Error is null
            ? $"Saved my media {name} ({media.SizeText})"
            : $"Saved for this session only, the presets file could not be written: {result.Error}";
    }

    private void RemoveUserMedia(Core.Media.StockMedia media)
    {
        Core.Media.UserMediaResult result = _userMediaStore.Remove(media.PartNumber);
        if (SelectedMedia == media)
        {
            SelectedMedia = null;
        }

        ApplyUserMedia(result.Entries);
        StatusText = result.Error is null
            ? $"Removed my media {media.PartNumber}"
            : $"Could not update the presets file: {result.Error}";
    }

    private void ApplyUserMedia(IReadOnlyList<Core.Media.StockMedia> presets)
    {
        UserMedia.Clear();
        foreach (Core.Media.StockMedia media in presets)
        {
            UserMedia.Add(new UserMediaEntryViewModel(media, RemoveUserMedia));
        }

        // The user's own first: they are the few entries that were deliberately
        // defined, and the catalog behind them is 797 entries deep.
        MediaCatalog = [.. presets, .. Core.Media.StockCatalog.All];
        OnPropertyChanged(nameof(HasUserMedia));
    }

    partial void OnSelectedDensityChanged(DensityOption? value)
    {
        if (_restoring || value is null)
        {
            return;
        }

        Document.Dpmm = value.Dpmm;
        UpdatePrinterWarning();
        RecordUndo("density");
        ScheduleRender();
    }

    /// <summary>
    /// Serializes the document into the history. A non-null <paramref name="coalesceKey"/>
    /// identifies the logical action being edited (a selection being dragged, one
    /// property being typed, the label width being adjusted). Consecutive edits that
    /// share the same key within the coalesce window replace the current snapshot
    /// instead of pushing a new one, so typing a word or holding an arrow key undoes as
    /// a single step; an edit to a different target starts a fresh step. A null key
    /// never coalesces (discrete actions like add, paste, delete, z-order).
    /// </summary>
    private void RecordUndo(string? coalesceKey = null)
    {
        string snapshot = LabelDocumentJson.Serialize(Document);
        long now = Environment.TickCount64;

        bool coalesce = coalesceKey is not null
            && coalesceKey == _lastCoalesceKey
            && now - _lastRecordTicks < CoalesceWindowMs;

        if (coalesce)
        {
            _history.ReplaceCurrent(snapshot);
        }
        else
        {
            _history.Record(snapshot);
        }

        _lastRecordTicks = now;
        _lastCoalesceKey = coalesceKey;
        UpdateUndoState();
    }

    private void RestoreSnapshot(string snapshot)
    {
        var selectedIds = Selection.Items.Select(e => e.Id).ToHashSet();

        _restoring = true;
        try
        {
            LabelDocument document = LabelDocumentJson.Deserialize(snapshot);
            Document = document;
            WidthMm = (decimal)document.WidthMm;
            HeightMm = (decimal)document.HeightMm;
            SelectedDensity = Densities.FirstOrDefault(d => d.Dpmm == document.Dpmm) ?? Densities[0];
            Selection.SetMany(document.Elements.Where(e => selectedIds.Contains(e.Id)));
        }
        finally
        {
            _restoring = false;
        }

        // Restoring must not count as a new edit; a subsequent edit starts fresh.
        _lastRecordTicks = 0;
        _lastCoalesceKey = null;
        NotifyPrintSettingsChanged();
        RefreshVariables();
        RefreshOutline();
        UpdatePrinterWarning();
        ScheduleRender();
    }

    private void UpdateUndoState()
    {
        CanUndo = _history.CanUndo;
        CanRedo = _history.CanRedo;
    }

    /// <summary>Syncs the Variables panel with the markers on the label. Skips the
    /// rebuild when nothing changed so an open sample TextBox keeps its focus.</summary>
    /// <summary>
    /// Rebuilds the outline when the label's structure has actually changed.
    ///
    /// Compared by a signature rather than rebuilt every pass: a list that is thrown away
    /// and remade on each render loses the row the user is pointing at, and the render
    /// runs on every keystroke.
    /// </summary>
    private void RefreshOutline()
    {
        Element[] elements = Document.Elements.OrderByDescending(e => e.ZOrder).ToArray();
        string signature = string.Join(
            "|", elements.Select(e => $"{e.Id}:{OutlineLabel(e)}:{e.IsVisible}:{e.IsLocked}"));
        if (string.Equals(signature, _outlineSignature, StringComparison.Ordinal))
        {
            return;
        }

        _outlineSignature = signature;
        _syncingOutline = true;
        try
        {
            Outline.Clear();
            foreach (Element element in elements)
            {
                Outline.Add(new ElementOutlineViewModel(element, OutlineLabel(element), OnOutlineEdited));
            }

            SelectedOutlineRow = Outline.FirstOrDefault(r => ReferenceEquals(r.Element, Selection.Primary));
        }
        finally
        {
            _syncingOutline = false;
        }

        OnPropertyChanged(nameof(HasOutline));
        OnPropertyChanged(nameof(OutlineHeader));
    }

    private string? _outlineSignature;

    /// <summary>Names a row: the user's own name when there is one, otherwise the type
    /// and a glimpse of the content, which is what tells two of the same type apart.</summary>
    private static string OutlineLabel(Element element)
    {
        if (!string.IsNullOrWhiteSpace(element.Name))
        {
            return element.Name;
        }

        string content = element switch
        {
            TextElement text => text.Text,
            BarcodeElement barcode => barcode.Data,
            QrCodeElement qr => qr.Data,
            DataMatrixElement dm => dm.Data,
            Pdf417Element pdf => pdf.Data,
            _ => string.Empty,
        };

        content = content.ReplaceLineEndings(" ").Trim();
        if (content.Length > 28)
        {
            content = content[..28] + "...";
        }

        return content.Length > 0 ? $"{DisplayName(element)}: {content}" : DisplayName(element);
    }

    private void OnOutlineEdited(Element element, string key)
    {
        RecordUndo($"{key}:{element.Id}");
        ScheduleRender();
    }

    private void RefreshVariables()
    {
        IReadOnlyList<string> names = TemplateVariables.Discover(Document);
        bool unchanged = ReferenceEquals(_variablesDocument, Document)
            && names.Count == Variables.Count
            && !names.Where((name, i) => Variables[i].Name != name).Any();
        if (unchanged)
        {
            return;
        }

        _variablesDocument = Document;
        Variables.Clear();
        foreach (string name in names)
        {
            Variables.Add(new VariableSampleViewModel(name, Document, OnVariableEdited));
        }

        OnPropertyChanged(nameof(HasVariables));
    }

    /// <summary>An edit from the Variables panel. The row supplies the coalescing key so
    /// typing a sample and nudging a counter never merge into one undo step.</summary>
    private void OnVariableEdited(string undoKey)
    {
        RecordUndo(undoKey);
        ScheduleRender();
    }

    /// <summary>Moves (or adds) a path to the top of the recent files list.</summary>
    public void RegisterRecentFile(string path)
    {
        SyncRecentFiles(Services.RecentFilesStore.Add(path));
    }

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            OpenLabelFile(path);
        }
    }

    /// <summary>
    /// Opens a .lfl by path. The one implementation: the file picker, the recent files
    /// menu and a path handed to the app at startup all end here, because three ways in
    /// are three ways to disagree about what "opened" means to the recent list and to the
    /// status line.
    /// </summary>
    /// <returns>True when the label opened. A caller that chose the path itself, rather
    /// than being handed one, wants to know.</returns>
    public bool OpenLabelFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            LoadDocument(LabelDocumentJson.Deserialize(File.ReadAllText(path)), path);
            StatusText = $"Opened {Path.GetFileName(path)}";
            RegisterRecentFile(path);
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open: {ex.Message}";

            // A path that no longer opens has no business in a menu of paths to open.
            // Harmless for one that was never in the list.
            SyncRecentFiles(Services.RecentFilesStore.Remove(path));
            return false;
        }
    }

    private void SyncRecentFiles(IReadOnlyList<string> entries)
    {
        RecentFiles.Clear();
        foreach (string entry in entries)
        {
            RecentFiles.Add(entry);
        }

        OnPropertyChanged(nameof(HasRecentFiles));
    }

    /// <summary>Every visible barcode whose data cannot be encoded, each described for
    /// display ("Barcode 'Name': reason"). Empty when all barcodes are fine.</summary>
    private List<string> CollectBarcodeProblems()
    {
        var problems = new List<string>();
        foreach (BarcodeElement barcode in Document.Elements.OfType<BarcodeElement>().Where(b => b.IsVisible))
        {
            if (Core.Zpl.BarcodeValidator.Validate(
                    barcode.Symbology, barcode.Data, Document.Markers) is { } warning)
            {
                string name = string.IsNullOrEmpty(barcode.Name) ? barcode.Symbology.ToString() : barcode.Name;
                problems.Add($"Barcode '{name}': {warning}");
            }
        }

        return problems;
    }

    /// <summary>
    /// Symbols whose quiet zone is not clear: the blank margin a scanner needs to find
    /// the symbol at all. Reported separately from encoding problems because it is a
    /// different kind of failure. Un-encodable data produces no barcode; a crowded quiet
    /// zone produces one that looks perfect and does not scan.
    /// </summary>
    private List<string> CollectQuietZoneProblems()
    {
        var problems = new List<string>();

        // One line per symbol, not per intruder: three neighbours crowding one barcode is
        // one thing to fix, and listing it three times would bury the other symbols.
        foreach (var group in QuietZoneChecker.Check(Document).GroupBy(f => f.Code))
        {
            string name = DisplayName(group.Key);
            string[] intruders = group
                .Where(f => f.Intruder is not null)
                .Select(f => DisplayName(f.Intruder!))
                .Distinct()
                .ToArray();

            if (intruders.Length > 0)
            {
                problems.Add(
                    $"Quiet zone of '{name}' is crowded by {string.Join(", ", intruders.Select(i => $"'{i}'"))}: "
                    + "a scanner needs that margin blank to find the symbol.");
            }

            if (group.Any(f => f.Intruder is null))
            {
                problems.Add(
                    $"Quiet zone of '{name}' runs off the label: move it in so the blank "
                    + "margin fits on the stock.");
            }
        }

        return problems;
    }

    /// <summary>
    /// GS1 payloads whose structure will not read back as written.
    ///
    /// Kept apart from the encoding problems because it is a different failure. Data that
    /// cannot be encoded produces no barcode; a payload missing a separator produces one
    /// that scans perfectly and returns the wrong value, which is why it has to be said
    /// out loud rather than left to whoever reads the label later.
    /// </summary>
    private List<string> CollectGs1Problems()
    {
        var problems = new List<string>();
        foreach (BarcodeElement barcode in Document.Elements
                     .OfType<BarcodeElement>()
                     .Where(b => b.IsVisible &&
                                 b.Symbology == BarcodeSymbology.Code128 &&
                                 Core.Zpl.Gs1Payload.IsGs1(b.Data)))
        {
            foreach (string problem in Core.Zpl.Gs1Payload.Read(barcode.Data).Problems)
            {
                problems.Add($"GS1 barcode '{DisplayName(barcode)}': {problem}");
            }
        }

        return problems;
    }

    /// <summary>
    /// Markers this label uses that its field catalog does not list.
    ///
    /// The failure being caught is a quiet one: a marker the filling system does not
    /// recognise is not rejected anywhere, it is simply left alone, so the label prints
    /// the marker text itself. Nothing errors and the roll comes out wrong.
    /// </summary>
    private List<string> CollectUnknownFieldProblems()
    {
        IReadOnlyList<Core.Fields.UnknownField> unknown =
            Core.Fields.UnknownFieldCheck.Check(Document, SelectedFieldCatalog);
        if (unknown.Count == 0)
        {
            return [];
        }

        string catalog = $"'{SelectedFieldCatalog?.Name}'";
        return
        [
            unknown.Count == 1
                ? $"{Describe(unknown[0])} is not in {catalog}, so it will print as written instead of being filled in."
                : $"{unknown.Count} markers are not in {catalog}, so they will print as written instead of being filled in: "
                  + string.Join(", ", unknown.Select(Describe)),
        ];

        static string Describe(Core.Fields.UnknownField field) =>
            field.Suggestion is null
                ? $"'{field.Name}'"
                : $"'{field.Name}' (did you mean {field.Suggestion}?)";
    }

    /// <summary>Refreshes the document-wide validation summary shown near the canvas, so
    /// a barcode that will not encode or will not scan is visible even when it is not
    /// selected.</summary>
    private void UpdateValidationWarning(IReadOnlyList<string> problems) =>
        ValidationWarning = problems.Count switch
        {
            0 => string.Empty,
            1 => problems[0],
            _ => $"{problems.Count} things need attention: {string.Join("; ", problems)}",
        };

    /// <summary>
    /// Puts a message on the status line and protects it from the render's size readout.
    ///
    /// The readout is the resting state of that line, and it lands whenever the render
    /// that a command scheduled finishes, a moment after the command set its message.
    /// Without this the answer to "what did the import find?" would flash and vanish.
    /// The next edit schedules a render, which clears the hold, so the line goes back to
    /// reporting the label size on its own.
    /// </summary>
    private void Notify(string message)
    {
        StatusText = message;
        _statusHeld = true;
    }

    /// <summary>Re-reads the label's length into the setup bar. On continuous stock the
    /// height is not something the user typed but something the content decides, so the
    /// box has to follow it; every edit schedules a render, which is the one place that
    /// sees them all. The guard keeps this from recording an undo step of its own.</summary>
    private void SyncLabelLength()
    {
        var current = (decimal)Document.HeightMm;
        if (HeightMm == current)
        {
            return;
        }

        bool restoring = _restoring;
        _restoring = true;
        HeightMm = current;
        _restoring = restoring;
    }

    private async void ScheduleRender(int delayMs = 150)
    {
        if (Document.IsContinuous)
        {
            SyncLabelLength();
        }

        _renderCts?.Cancel();
        _statusHeld = false;
        var cts = new CancellationTokenSource();
        _renderCts = cts;

        try
        {
            await Task.Delay(delayMs, cts.Token);

            LabelDocument document = Document;
            double widthMm = document.WidthMm;
            double heightMm = document.HeightMm;
            int dpmm = document.Dpmm;

            // One timestamp for the whole pass so the ZPL pane and the canvas agree on
            // what a date variable currently reads.
            DateTime now = DateTime.Now;

            // Captured before the work starts so the background task compares against a
            // value it owns, and only the UI thread ever writes the field.
            string? renderedFrom = _renderedFrom;

            (string zpl, RenderResult? result, int marginDots, string placementWarning,
                    string variableWarning, string renderKey) =
                await Task.Run(
                    () =>
                    {
                        // A generator instance carries the state of one pass, and a
                        // superseded render can still be in flight, so never share one.
                        var generator = new ZplGenerator();
                        string generated = generator.Generate(
                            document, new GenerationContext { Now = now });
                        GenerationInfo run = generator.LastRun;

                        var bounds = new ElementBoundsCalculator();
                        var offLabel = document.Elements
                            .Where(e => e.IsVisible)
                            .Select(e => (Element: e, Status: ElementPlacement.Classify(
                                e, bounds.GetBounds(e), document)))
                            .Where(t => t.Status != PlacementStatus.Inside)
                            .ToList();

                        // Only pay for the expanded pasteboard render when something
                        // actually sits off the label.
                        int margin = offLabel.Count > 0
                            ? Units.MmToDots(ElementPlacement.PasteboardMarginMm, dpmm)
                            : 0;
                        double marginMm = Units.DotsToMm(margin, dpmm);

                        // Always the preview variant, even at margin 0: it keeps job
                        // settings and printer-side clock codes out of the render, which
                        // the offline engine would report as unknown commands or draw
                        // literally instead of as a date.
                        string previewZpl = new ZplGenerator().GeneratePreview(document, margin);

                        // The preview resolves every marker to something renderable: the
                        // user's sample, the counter's first value, or the current date.
                        // Exported and printed ZPL keeps external markers literal.
                        previewZpl = _substitutor.Substitute(
                            previewZpl, inner => VariableValues.ForPreview(document, inner, now));
                        // Everything the drawn bitmap depends on. A clock variable makes
                        // this differ whenever its formatted value does, which is exactly
                        // when the picture would change, so it needs no special case.
                        string key = FormattableString.Invariant(
                            $"{widthMm}x{heightMm}@{dpmm}+{margin}|{previewZpl}");

                        // Nothing that reaches the renderer has changed, so the bitmap on
                        // screen is already the answer. Naming and locking an element,
                        // and undoing back to where you were, all land here.
                        if (string.Equals(key, renderedFrom, StringComparison.Ordinal))
                        {
                            return (generated, (RenderResult?)null, margin,
                                DescribePlacement(offLabel), string.Join(" ", run.Warnings), key);
                        }

                        RenderResult rendered = _renderer.Render(
                            previewZpl, widthMm + 2 * marginMm, heightMm + 2 * marginMm, dpmm);
                        return (generated, (RenderResult?)rendered, margin,
                            DescribePlacement(offLabel), string.Join(" ", run.Warnings), key);
                    },
                    cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            GeneratedZpl = zpl;
            CanvasRevision++;
            SnapshotForRecovery();
            UnderlayMarginDots = marginDots;
            PlacementWarning = placementWarning;
            VariableWarning = variableWarning;
            RefreshVariables();
            RefreshOutline();

            if (result is not null)
            {
                Bitmap? previous = Underlay;
                if (result.Png.Length > 0)
                {
                    using var stream = new MemoryStream(result.Png);
                    Underlay = new Bitmap(stream);
                }
                else
                {
                    Underlay = null;
                }

                previous?.Dispose();
                _renderedFrom = renderKey;
            }

            // Document-wide validation summary, shown near the canvas regardless of
            // what is selected.
            // Kept apart for the diagnosis below: a crowded quiet zone never explains an
            // empty render, so it must not be offered as the reason for one.
            List<string> barcodeProblems = CollectBarcodeProblems();
            UpdateValidationWarning(
                [.. barcodeProblems, .. CollectGs1Problems(), .. CollectQuietZoneProblems()]);
            UnknownFieldWarning = string.Join(" ", CollectUnknownFieldProblems());

            // On a failed or empty render, lead with a specific diagnosis when a
            // barcode cannot be encoded, but keep the engine's own message too: the
            // failure may have a different cause than the barcode we flagged.
            var diagnosis = new List<string>(2);
            if (result is not null)
            {
                if ((result.Errors.Count > 0 || result.Png.Length == 0) && barcodeProblems.Count > 0)
                {
                    diagnosis.Add(barcodeProblems[0]);
                }

                if (result.Errors.Count > 0)
                {
                    diagnosis.Add(string.Join("; ", result.Errors.Take(2)));
                }
            }

            // The rendered bitmap may be pasteboard-expanded; always report the label's
            // own size. A render problem outranks anything a command had to say; the
            // idle size readout does not (see Notify).
            if (diagnosis.Count > 0)
            {
                StatusText = string.Join(" | ", diagnosis);
                _statusHeld = false;
            }
            else if (!_statusHeld)
            {
                StatusText = $"{document.WidthDots} x {document.HeightDots} dots";
            }
        }
        catch (OperationCanceledException)
        {
            // A newer edit superseded this render; drop it.
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    /// <summary>One line summarizing elements off the label ("will not print"), elements
    /// crossing its right/bottom edge ("will be clipped"), and elements deliberately kept
    /// out of the print; empty when none. The deliberate ones are phrased as a statement
    /// rather than a warning, because the user asked for them.</summary>
    private static string DescribePlacement(
        IReadOnlyList<(Element Element, PlacementStatus Status)> offLabel)
    {
        var outside = offLabel.Where(t => t.Status == PlacementStatus.NotPrintable).ToList();
        var clipped = offLabel.Where(t => t.Status == PlacementStatus.Clipped).ToList();
        var suppressed = offLabel.Where(t => t.Status == PlacementStatus.Suppressed).ToList();

        var parts = new List<string>(3);
        if (outside.Count == 1)
        {
            parts.Add($"'{DisplayName(outside[0].Element)}' is outside the label and will not print");
        }
        else if (outside.Count > 1)
        {
            parts.Add($"{outside.Count} elements are outside the label and will not print");
        }

        if (clipped.Count == 1)
        {
            parts.Add($"'{DisplayName(clipped[0].Element)}' extends past the label edge and will be clipped");
        }
        else if (clipped.Count > 1)
        {
            parts.Add($"{clipped.Count} elements extend past the label edge and will be clipped");
        }

        if (suppressed.Count == 1)
        {
            parts.Add($"'{DisplayName(suppressed[0].Element)}' is set not to print");
        }
        else if (suppressed.Count > 1)
        {
            parts.Add($"{suppressed.Count} elements are set not to print");
        }

        return string.Join("; ", parts);
    }

    /// <summary>What an unnamed element is called, in the outline and in the placement
    /// warnings alike. A white shape says so: it paints the stock clear, so it draws
    /// nothing the eye can find, and the outline is where someone goes looking for it.</summary>
    private static string DisplayName(Element element) =>
        !string.IsNullOrEmpty(element.Name) ? element.Name : element switch
        {
            TextElement => "Text",
            BarcodeElement => "Barcode",
            QrCodeElement => "QR code",
            DataMatrixElement => "Data Matrix",
            Pdf417Element => "PDF417",
            ImageElement => "Image",
            LineElement { IsWhite: true } => "White line",
            LineElement => "Line",
            BoxElement { IsWhite: true } => "White box",
            BoxElement => "Box",
            EllipseElement { IsWhite: true } => "White ellipse",
            EllipseElement e when e.WidthDots == e.HeightDots => "Circle",
            EllipseElement => "Ellipse",
            DiagonalLineElement { IsWhite: true } => "White diagonal",
            DiagonalLineElement => "Diagonal line",
            _ => "Element",
        };

    private static void SeedSampleLabel(LabelDocument doc)
    {
        doc.Elements.Add(new BoxElement
        {
            Name = "Border", X = 15, Y = 15, WidthDots = 770, HeightDots = 450, ThicknessDots = 3, ZOrder = 0,
        });
        doc.Elements.Add(new TextElement
        {
            Name = "Title", X = 50, Y = 50, Text = "LabelForge", FontHeightDots = 60, ZOrder = 1,
        });
        doc.Elements.Add(new BarcodeElement
        {
            Name = "Barcode", X = 50, Y = 170, Data = "LF-000123", HeightDots = 140, ModuleWidthDots = 3, ZOrder = 2,
        });
        doc.Elements.Add(new QrCodeElement
        {
            Name = "QR", X = 600, Y = 170, Data = "https://labelforge.app", Magnification = 6, ZOrder = 3,
        });
    }
}
