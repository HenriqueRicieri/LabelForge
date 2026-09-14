using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace LabelForge.App.Controls;

public sealed partial class DesignerCanvas
{
    public static readonly StyledProperty<bool> ShowCanvasPerformanceProperty =
        AvaloniaProperty.Register<DesignerCanvas, bool>(nameof(ShowCanvasPerformance));

    public bool ShowCanvasPerformance
    {
        get => GetValue(ShowCanvasPerformanceProperty);
        set => SetValue(ShowCanvasPerformanceProperty, value);
    }

    public double LastPaintMilliseconds { get; private set; }
    public long LastPaintAllocatedBytes { get; private set; }
    public long MeasuredPaintCount { get; private set; }
    private long _paintWindowStart;
    private int _paintWindowCount;
    private FormattedText? _performanceText;

    public override void Render(DrawingContext context)
    {
        if (!ShowCanvasPerformance)
        {
            _paintWindowStart = 0;
            _performanceText = null;
            RenderCanvas(context);
            return;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        RenderCanvas(context);
        LastPaintMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        LastPaintAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        MeasuredPaintCount++;
        _paintWindowCount++;
        double elapsed = _paintWindowStart == 0 ? 0 : Stopwatch.GetElapsedTime(_paintWindowStart).TotalSeconds;
        if (_performanceText is null || elapsed >= 1)
        {
            string rate = elapsed > 0 ? $"{_paintWindowCount / elapsed:0} redraws/s" : "Measuring";
            _performanceText = new FormattedText(
                string.Create(CultureInfo.InvariantCulture, $"{LastPaintMilliseconds:0.00} ms paint | {rate}"),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White);
            _paintWindowStart = start;
            _paintWindowCount = 0;
        }
        var box = new Rect(RulerSize + 8, RulerSize + 8, _performanceText.Width + 12, _performanceText.Height + 8);
        context.FillRectangle(Brushes.Black, box);
        context.DrawText(_performanceText, new Point(box.X + 6, box.Y + 4));
    }
}
