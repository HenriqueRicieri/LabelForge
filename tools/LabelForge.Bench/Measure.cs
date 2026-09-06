using System.Diagnostics;

namespace LabelForge.Bench;

/// <summary>
/// Timing with the shape every row in the table shares: warm up twice so the JIT and any
/// first-call caches are paid for outside the measurement, then take the median of a
/// fixed number of runs. The median rather than the mean because a background thread on
/// a desktop machine produces occasional outliers that no user would ever feel.
/// </summary>
internal static class Measure
{
    public static int Runs { get; set; } = 7;

    /// <summary>Median milliseconds for one unit of work.</summary>
    public static double Time(Action action)
    {
        action();
        action();

        var times = new List<double>(Runs);
        for (int i = 0; i < Runs; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        return times[Runs / 2];
    }

    /// <summary>Milliseconds as the tables print them: two decimals under 10 ms, whole
    /// milliseconds above, because nobody reads the hundredths of a 355 ms render.</summary>
    public static string Ms(double milliseconds) =>
        milliseconds >= 10
            ? $"{milliseconds:0} ms"
            : $"{milliseconds:0.00} ms";
}
