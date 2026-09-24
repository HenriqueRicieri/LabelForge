using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Input;
using Avalonia.Media;

namespace LabelForge.App.Controls;

public sealed partial class DesignerCanvas
{
    private readonly Dictionary<(double? Centimeters, bool Dark), FormattedText> _rulerLabels = [];
    private static readonly Dictionary<StandardCursorType, Cursor> Cursors = [];

    private FormattedText RulerLabel(double? millimeters, IBrush brush)
    {
        var key = (millimeters is { } mm ? Math.Round(mm / 10, 2) : (double?)null, IsDark);
        if (_rulerLabels.TryGetValue(key, out var label)) return label;
        // Panning can expose indefinitely many labels. Keep only a small working set;
        // positioning and tick spacing still come from the current transform each paint.
        if (_rulerLabels.Count >= 128) _rulerLabels.Clear();
        label = new FormattedText(key.Item1?.ToString("0.##", CultureInfo.InvariantCulture) ?? "cm",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 9, brush);
        _rulerLabels.Add(key, label);
        return label;
    }

    private static Cursor SharedCursor(StandardCursorType type)
    {
        if (!Cursors.TryGetValue(type, out var cursor))
        {
            cursor = new Cursor(type);
            Cursors.Add(type, cursor);
        }
        return cursor;
    }
}
