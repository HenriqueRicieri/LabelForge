using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace LabelForge.App.Controls;

public sealed partial class DesignerCanvas
{
    private readonly Dictionary<(double? Centimeters, bool Dark), TextLayout> _rulerLabels = [];
    private static readonly Dictionary<StandardCursorType, Cursor> Cursors = [];

    private TextLayout RulerLabel(double? millimeters, IBrush brush)
    {
        var key = (millimeters is { } mm ? Math.Round(mm / 10, 2) : (double?)null, IsDark);
        if (_rulerLabels.TryGetValue(key, out var label)) return label;
        if (_rulerLabels.Count >= 128) ClearRulerLabels();
        label = new TextLayout(key.Item1?.ToString("0.##", CultureInfo.InvariantCulture) ?? "cm",
            Typeface.Default, 9, brush);
        _rulerLabels.Add(key, label);
        return label;
    }

    private void ClearRulerLabels()
    {
        foreach (var label in _rulerLabels.Values) label.Dispose();
        _rulerLabels.Clear();
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
