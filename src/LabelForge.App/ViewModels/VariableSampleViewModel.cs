using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using LabelForge.Core.Model;
using LabelForge.Core.Templating;

namespace LabelForge.App.ViewModels;

/// <summary>A variable kind as offered in the panel: the model value plus a label that
/// says what it means to the person filling the label in.</summary>
public sealed class VariableKindOption(VariableKind kind, string label)
{
    public VariableKind Kind { get; } = kind;

    public override string ToString() => label;

    /// <summary>Shared instances so a ComboBox can match by reference.</summary>
    public static IReadOnlyList<VariableKindOption> All { get; } =
    [
        new VariableKindOption(VariableKind.External, "Filled at print"),
        new VariableKindOption(VariableKind.Counter, "Counter"),
        new VariableKindOption(VariableKind.Clock, "Date and time"),
    ];
}

/// <summary>
/// One row of the designer's Variables panel: a template variable discovered on the
/// label, how it is filled, and its preview value. Writes go straight to the document
/// (SampleValues for the preview sample, Variables for counters and clocks), so the
/// .lfl and the undo snapshots carry them like any other document change.
///
/// A variable that is filled downstream keeps no entry in Document.Variables at all,
/// which is what makes a label saved before counters existed load unchanged.
/// </summary>
public sealed class VariableSampleViewModel : ObservableObject
{
    /// <summary>Read-only source of default values for a variable with no definition
    /// yet. Never written: every setter returns early when the definition is absent.</summary>
    private static readonly VariableDefinition Defaults = new();

    private const decimal MaxCounterValue = 999_999_999_999m;

    private readonly LabelDocument _document;
    private readonly Action<string> _edited;

    public VariableSampleViewModel(string name, LabelDocument document, Action<string> edited)
    {
        Name = name;
        _document = document;
        _edited = edited;
    }

    public string Name { get; }

    public string DefaultHint => TemplateSubstitutor.DefaultSampleValue;

    public IReadOnlyList<VariableKindOption> Kinds => VariableKindOption.All;

    public IReadOnlyList<string> ClockFormats => ZplClockFormat.Presets;

    private VariableDefinition? Definition =>
        _document.Variables.TryGetValue(Name, out VariableDefinition? definition)
            ? definition
            : null;

    private VariableDefinition Current => Definition ?? Defaults;

    public VariableKind Kind => Current.Kind;

    public bool IsExternal => Kind == VariableKind.External;

    public bool IsCounter => Kind == VariableKind.Counter;

    public bool IsClock => Kind == VariableKind.Clock;

    public VariableKindOption SelectedKind
    {
        get => VariableKindOption.All.First(option => option.Kind == Kind);
        set
        {
            VariableKind next = value?.Kind ?? VariableKind.External;
            if (next == Kind)
            {
                return;
            }

            if (next == VariableKind.External)
            {
                _document.Variables.Remove(Name);
            }
            else
            {
                VariableDefinition definition = Definition ?? Add();
                definition.Kind = next;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(IsExternal));
            OnPropertyChanged(nameof(IsCounter));
            OnPropertyChanged(nameof(IsClock));
            NotifySettingsChanged();
            _edited($"var-kind:{Name}");
        }
    }

    /// <summary>Preview value for a variable filled downstream. Empty falls back to the
    /// default sample so a barcode still renders.</summary>
    public string Sample
    {
        get => _document.SampleValues.TryGetValue(Name, out string? value) ? value : string.Empty;
        set
        {
            string next = value ?? string.Empty;
            if (Sample == next)
            {
                return;
            }

            if (next.Length == 0)
            {
                _document.SampleValues.Remove(Name);
            }
            else
            {
                _document.SampleValues[Name] = next;
            }

            OnPropertyChanged();
            _edited($"sample:{Name}");
        }
    }

    public decimal CounterStart
    {
        get => Current.CounterStart;
        set => Set(
            (long)Math.Clamp(value, 0m, MaxCounterValue),
            d => d.CounterStart,
            (d, v) => d.CounterStart = v,
            $"var-start:{Name}");
    }

    public decimal CounterStep
    {
        get => Current.CounterStep;
        set => Set(
            (long)Math.Clamp(value, -MaxCounterValue, MaxCounterValue),
            d => d.CounterStep,
            (d, v) => d.CounterStep = v,
            $"var-step:{Name}");
    }

    public decimal CounterPadding
    {
        get => Current.CounterPadding;
        set => Set(
            (int)Math.Clamp(value, 0m, VariableDefinition.MaxCounterDigits),
            d => d.CounterPadding,
            (d, v) => d.CounterPadding = v,
            $"var-pad:{Name}");
    }

    public bool UsePrinterCounter
    {
        get => Current.UsePrinterCounter;
        set => Set(value, d => d.UsePrinterCounter, (d, v) => d.UsePrinterCounter = v,
            $"var-native:{Name}");
    }

    public string ClockFormat
    {
        get => Current.ClockFormat;
        set => Set(value ?? string.Empty, d => d.ClockFormat, (d, v) => d.ClockFormat = v,
            $"var-format:{Name}");
    }

    public bool UsePrinterClock
    {
        get => Current.UsePrinterClock;
        set => Set(value, d => d.UsePrinterClock, (d, v) => d.UsePrinterClock = v,
            $"var-rtc:{Name}");
    }

    /// <summary>The preset matching the current format, or null when the user typed
    /// their own. Selecting one writes it into <see cref="ClockFormat"/>.</summary>
    public string? SelectedClockPreset
    {
        get => ZplClockFormat.Presets.FirstOrDefault(
            f => string.Equals(f, ClockFormat, StringComparison.Ordinal));
        set
        {
            if (value is { Length: > 0 })
            {
                ClockFormat = value;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>What this variable will print, so the choice can be checked without
    /// hunting for the field on the canvas.</summary>
    public string PreviewValue
    {
        get
        {
            VariableDefinition definition = Current;
            if (Kind == VariableKind.Counter)
            {
                return string.Join(", ", Enumerable.Range(0, 3)
                    .Select(definition.FormatCounterAt)) + ", ...";
            }

            if (Kind == VariableKind.Clock)
            {
                return VariableDefinition.TryFormatClock(definition.ClockFormat, DateTime.Now, out string text)
                    ? text
                    : "Unrecognized format; the label would print "
                      + definition.FormatClock(DateTime.Now);
            }

            return string.Empty;
        }
    }

    /// <summary>How the value gets produced, stated up front because the printer-side
    /// options change how a run is sent, not just what it prints.</summary>
    public string KindHint => Kind switch
    {
        VariableKind.Counter when Current.UsePrinterCounter =>
            "The printer numbers the run (^SN) when the marker ends its field; otherwise LabelForge numbers it and sends one job per label.",
        VariableKind.Counter =>
            "LabelForge numbers every copy and sends one job per label.",
        VariableKind.Clock when !Current.UsePrinterClock =>
            "Stamped from this PC's clock when the label is printed or exported.",
        VariableKind.Clock when ZplClockFormat.TryTranslate(Current.ClockFormat, out _) =>
            "Filled by the printer's clock (^FC); printers without the real time clock option need this off.",
        VariableKind.Clock =>
            "This format has no printer equivalent, so this PC's clock fills it.",
        _ => string.Empty,
    };

    private VariableDefinition Add()
    {
        var definition = new VariableDefinition();
        _document.Variables[Name] = definition;
        return definition;
    }

    /// <summary>Applies a setting, but only to a variable that already has a definition:
    /// a control bound while its section is hidden must never conjure one.</summary>
    private void Set<T>(
        T value,
        Func<VariableDefinition, T> read,
        Action<VariableDefinition, T> write,
        string undoKey,
        [CallerMemberName] string? property = null)
    {
        if (Definition is not { } definition ||
            EqualityComparer<T>.Default.Equals(read(definition), value))
        {
            return;
        }

        write(definition, value);
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(PreviewValue));
        OnPropertyChanged(nameof(KindHint));
        OnPropertyChanged(nameof(SelectedClockPreset));
        _edited(undoKey);
    }

    private void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(CounterStart));
        OnPropertyChanged(nameof(CounterStep));
        OnPropertyChanged(nameof(CounterPadding));
        OnPropertyChanged(nameof(UsePrinterCounter));
        OnPropertyChanged(nameof(ClockFormat));
        OnPropertyChanged(nameof(UsePrinterClock));
        OnPropertyChanged(nameof(SelectedClockPreset));
        OnPropertyChanged(nameof(PreviewValue));
        OnPropertyChanged(nameof(KindHint));
    }
}
