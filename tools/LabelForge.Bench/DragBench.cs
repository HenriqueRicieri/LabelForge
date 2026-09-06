using System.Diagnostics;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;

namespace LabelForge.Bench;

/// <summary>
/// What a drag actually delivers. The tables measure one render; this measures a stream of
/// them, which is the thing the user complained about.
///
/// Both schemes are driven identically: a request every few milliseconds for two seconds,
/// with an element nudged each time so no two frames render the same ZPL and the render
/// cache cannot answer for free. What is counted is frames that reached the screen, which
/// is the only number a person can see.
///
/// The old scheme is reproduced here rather than described, because "it got faster" is
/// worth nothing without the thing it got faster than. It is what the designer did before
/// H3: wait out a debounce, cancel the previous request, start a render whatever else is
/// running, and throw the result away if something newer arrived meanwhile.
///
/// Two pointer intervals, because the old scheme failed in two different ways. Below its
/// debounce every request cancelled the one before it and no render ever started, so the
/// canvas simply stopped following until the hand paused. Above it, renders started faster
/// than they finished and several ran at once, each slower for the company. Only the
/// scheduling differs between the columns; both draw through the same pixel path, so what
/// is measured here is H3 and not H2.
/// </summary>
internal static class DragBench
{
    private const int DurationMs = 2000;
    private const int OldDebounceMs = 40;

    /// <summary>A hand that never stops moving, and one that moves in steps just slower
    /// than the old debounce. The first is where the old scheme starved, the second where
    /// it piled up.</summary>
    public static readonly int[] PointerIntervalsMs = [8, 50];

    public static List<string[]> Run(IReadOnlyList<Scenario> scenarios, int pointerIntervalMs)
    {
        var rows = new List<string[]>();
        foreach ((string name, LabelDocument document) in scenarios)
        {
            Console.Error.WriteLine($"dragging on {name} every {pointerIntervalMs} ms ...");
            Element? mover = document.Elements.FirstOrDefault(e => e.IsVisible)
                ?? document.Elements.FirstOrDefault();
            if (mover is null)
            {
                continue;
            }

            (int oldShown, int oldStarted, int oldPeak) = Old(document, mover, pointerIntervalMs);
            (int newShown, int newStarted, int newPeak) = Queued(document, mover, pointerIntervalMs);

            rows.Add([
                name,
                Fps(oldShown),
                Fps(newShown),
                $"{oldStarted} / {newStarted}",
                $"{oldPeak} / {newPeak}",
            ]);
        }

        return rows;
    }

    private static string Fps(int frames) =>
        $"{frames} ({frames / (DurationMs / 1000.0):0.0}/s)";

    /// <summary>What the designer did before H3. Every tick cancels the last request and
    /// starts a render regardless of what is already running, and a result whose request
    /// was superseded is dropped.</summary>
    private static (int Shown, int Started, int Peak) Old(
        LabelDocument document, Element mover, int pointerIntervalMs)
    {
        var render = new Render(document);
        int shown = 0;
        int started = 0;
        int inFlight = 0;
        int peak = 0;
        var running = new List<Task>();
        CancellationTokenSource? previous = null;

        var stopwatch = Stopwatch.StartNew();
        int step = 0;
        while (stopwatch.ElapsedMilliseconds < DurationMs)
        {
            previous?.Cancel();
            var cts = new CancellationTokenSource();
            previous = cts;
            mover.X = 40 + (step++ % 60);
            string zpl = render.Zpl();

            running.Add(Task.Run(
                async () =>
                {
                    try
                    {
                        await Task.Delay(OldDebounceMs, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    int here = Interlocked.Increment(ref inFlight);
                    Interlocked.Increment(ref started);
                    InterlockedMax(ref peak, here);
                    render.Draw(zpl);
                    Interlocked.Decrement(ref inFlight);

                    if (!cts.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref shown);
                    }
                },
                CancellationToken.None));

            Thread.Sleep(pointerIntervalMs);
        }

        previous?.Cancel();
        Task.WaitAll([.. running]);
        return (shown, started, peak);
    }

    /// <summary>What it does now: one render at a time, the newest request waiting, and a
    /// render that finished is shown whatever has queued up behind it.</summary>
    private static (int Shown, int Started, int Peak) Queued(
        LabelDocument document, Element mover, int pointerIntervalMs)
    {
        var render = new Render(document);
        int shown = 0;
        int started = 0;
        int inFlight = 0;
        int peak = 0;

        var queue = new RenderQueue<string, string>((zpl, _) =>
        {
            int here = Interlocked.Increment(ref inFlight);
            Interlocked.Increment(ref started);
            InterlockedMax(ref peak, here);
            render.Draw(zpl);
            Interlocked.Decrement(ref inFlight);
            return zpl;
        });

        var running = new List<Task>();
        var stopwatch = Stopwatch.StartNew();
        int step = 0;
        while (stopwatch.ElapsedMilliseconds < DurationMs)
        {
            mover.X = 40 + (step++ % 60);
            running.Add(queue.RequestAsync(render.Zpl()).ContinueWith(
                t =>
                {
                    if (t.Result is not null)
                    {
                        Interlocked.Increment(ref shown);
                    }
                },
                TaskScheduler.Default));

            Thread.Sleep(pointerIntervalMs);
        }

        Task.WaitAll([.. running]);
        return (shown, started, peak);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen = Volatile.Read(ref target);
        while (value > seen)
        {
            int was = Interlocked.CompareExchange(ref target, value, seen);
            if (was == seen)
            {
                return;
            }

            seen = was;
        }
    }

    /// <summary>The render pass a drag frame costs, with nothing else in it: preview ZPL
    /// out of the live document, then pixels, which is what the designer asks for.</summary>
    private sealed class Render(LabelDocument document)
    {
        private readonly IZplRenderer _renderer = new BinaryKitsRenderer();
        private readonly TemplateSubstitutor _substitutor = new();
        private readonly DateTime _now = DateTime.Now;

        public string Zpl() =>
            _substitutor.Substitute(
                new ZplGenerator().GeneratePreview(document, 0),
                inner => VariableValues.ForPreview(document, inner, _now));

        public void Draw(string zpl) => _renderer.Render(
            zpl, document.WidthMm, document.HeightMm, document.Dpmm, 0, RenderOutput.Pixels);
    }
}
