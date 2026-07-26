using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LabelForge.Core.Model;

namespace LabelForge.App.ViewModels;

/// <summary>
/// One row of the element outline.
///
/// Carries its own toggles rather than reaching for an ancestor, the same way the media
/// and catalog rows do: binding through a parent is unreliable inside item templates and
/// there is no reason for a row to know what contains it.
/// </summary>
public sealed partial class ElementOutlineViewModel : ObservableObject
{
    private readonly Action<Element, string> _edited;

    public ElementOutlineViewModel(Element element, string display, Action<Element, string> edited)
    {
        Element = element;
        Display = display;
        _edited = edited;
    }

    public Element Element { get; }

    /// <summary>What the row reads as: the user's own name when there is one, otherwise
    /// the type and a glimpse of its content.</summary>
    public string Display { get; }

    /// <summary>Hidden everywhere, canvas included. Kept on the row because an outline is
    /// exactly where someone goes to find the thing they cannot see.</summary>
    public bool IsVisible
    {
        get => Element.IsVisible;
        set
        {
            if (Element.IsVisible == value)
            {
                return;
            }

            Element.IsVisible = value;
            OnPropertyChanged();
            _edited(Element, "outline-visible");
        }
    }

    /// <summary>Guards finished layout from the mouse. Reachable here as well as in the
    /// properties panel, because locking a run of elements one after another is the point
    /// of having a list of them.</summary>
    public bool IsLocked
    {
        get => Element.IsLocked;
        set
        {
            if (Element.IsLocked == value)
            {
                return;
            }

            Element.IsLocked = value;
            OnPropertyChanged();
            _edited(Element, "outline-locked");
        }
    }
}
