using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LabelForge.Core.Model;
using LabelForge.Core.Templating;

namespace LabelForge.App.ViewModels;

/// <summary>
/// One row of the designer's Variables panel: a template variable discovered on the
/// label and its editable preview sample. Writes go straight to the document's
/// SampleValues (the .lfl and undo snapshots carry them); an empty sample falls back
/// to the default preview value.
/// </summary>
public sealed class VariableSampleViewModel : ObservableObject
{
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
            _edited(Name);
        }
    }
}
