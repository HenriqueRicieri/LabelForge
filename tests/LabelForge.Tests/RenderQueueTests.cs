using LabelForge.Core.Rendering;

namespace LabelForge.Tests;

/// <summary>
/// The rules the preview's scheduling depends on. Nothing here sleeps: the fake work is
/// held open on a TaskCompletionSource the test releases by hand, so what is asserted is
/// the ordering and not how fast this machine happens to be.
/// </summary>
public sealed class RenderQueueTests
{
    /// <summary>A job that does not finish until the test says so, and counts how many
    /// were ever in flight at once.</summary>
    private sealed class Work
    {
        private readonly List<TaskCompletionSource> _gates = [];
        private readonly Lock _lock = new();

        public int Started { get; private set; }

        public int HighWaterMark { get; private set; }

        private int _inFlight;

        public List<string> Ran { get; } = [];

        public string Answer(string request, CancellationToken token)
        {
            TaskCompletionSource gate;
            lock (_lock)
            {
                Started++;
                _inFlight++;
                HighWaterMark = Math.Max(HighWaterMark, _inFlight);
                Ran.Add(request);
                gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _gates.Add(gate);
            }

            gate.Task.GetAwaiter().GetResult();

            lock (_lock)
            {
                _inFlight--;
            }

            token.ThrowIfCancellationRequested();
            return $"drew {request}";
        }

        /// <summary>Lets the nth started job finish. Waits for it to have started, since
        /// the queue starts it on a thread pool thread.</summary>
        public void Release(int index)
        {
            TaskCompletionSource gate = WaitForGate(index);
            gate.SetResult();
        }

        public TaskCompletionSource WaitForGate(int index)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    if (_gates.Count > index)
                    {
                        return _gates[index];
                    }
                }

                Thread.Sleep(1);
            }

            throw new TimeoutException($"job {index} never started; {Started} did.");
        }
    }

    [Fact]
    public async Task TheFirstRequestRunsStraightAway()
    {
        var work = new Work();
        var queue = new RenderQueue<string, string>(work.Answer);

        Task<string?> first = queue.RequestAsync("a");
        work.Release(0);

        Assert.Equal("drew a", await first);
        Assert.Equal(1, work.Started);
    }

    [Fact]
    public async Task ANewerRequestReplacesTheWaitingOneAndOnlyTheNewestRuns()
    {
        var work = new Work();
        var queue = new RenderQueue<string, string>(work.Answer);

        Task<string?> running = queue.RequestAsync("first");
        work.WaitForGate(0);

        Task<string?> superseded = queue.RequestAsync("second");
        Task<string?> newest = queue.RequestAsync("third");

        // The one that never got its turn is told so rather than left hanging, and it is
        // told so immediately: the caller has a newer render coming and nothing to show.
        Assert.Null(await superseded);

        work.Release(0);
        Assert.Equal("drew first", await running);

        work.Release(1);
        Assert.Equal("drew third", await newest);

        Assert.Equal(2, work.Started);
        Assert.Equal(["first", "third"], work.Ran);
    }

    [Fact]
    public async Task TheWaitingRequestRunsTheMomentTheRunningOneFinishes()
    {
        var work = new Work();
        var queue = new RenderQueue<string, string>(work.Answer);

        Task<string?> running = queue.RequestAsync("first");
        work.WaitForGate(0);
        Task<string?> waiting = queue.RequestAsync("second");

        Assert.Equal(1, work.Started);

        work.Release(0);
        await running;

        // Nothing asked for it again; finishing the first is what starts the second.
        work.Release(1);
        Assert.Equal("drew second", await waiting);
    }

    [Fact]
    public async Task NeverTwoAtOnce()
    {
        var work = new Work();
        var queue = new RenderQueue<string, string>(work.Answer);

        var requests = new List<Task<string?>>();
        Task<string?> running = queue.RequestAsync("0");
        work.WaitForGate(0);

        for (int i = 1; i <= 20; i++)
        {
            requests.Add(queue.RequestAsync(i.ToString()));
        }

        work.Release(0);
        await running;
        work.Release(1);
        await Task.WhenAll(requests);

        // Twenty requests behind one running job produce exactly one more render, and it
        // is the last one asked for. Nineteen renders never happened.
        Assert.Equal(2, work.Started);
        Assert.Equal(1, work.HighWaterMark);
        Assert.Equal(["0", "20"], work.Ran);
        Assert.Equal("drew 20", await requests[^1]);
        Assert.All(requests.Take(19), r => Assert.Null(r.Result));
    }

    [Fact]
    public async Task AnAlreadyCancelledRequestNeverRuns()
    {
        var work = new Work();
        var queue = new RenderQueue<string, string>(work.Answer);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Null(await queue.RequestAsync("a", cancelled.Token));
        Assert.Equal(0, work.Started);
    }

    /// <summary>A document swap cancels what was queued for the old one. The request that
    /// was waiting is dropped rather than drawn, and the queue does not stall on it.</summary>
    [Fact]
    public async Task AWaitingRequestCancelledBeforeItsTurnIsDroppedAndTheQueueCarriesOn()
    {
        var work = new Work();
        var queue = new RenderQueue<string, string>(work.Answer);
        using var stale = new CancellationTokenSource();

        Task<string?> running = queue.RequestAsync("first");
        work.WaitForGate(0);
        Task<string?> doomed = queue.RequestAsync("stale", stale.Token);

        stale.Cancel();
        work.Release(0);

        Assert.Equal("drew first", await running);
        Assert.Null(await doomed);
        Assert.Equal(1, work.Started);

        // And the queue is idle rather than stuck believing something is still running.
        Task<string?> next = queue.RequestAsync("after");
        work.Release(1);
        Assert.Equal("drew after", await next);
    }

    [Fact]
    public async Task AThrowingRenderFaultsItsCallerAndDoesNotWedgeTheQueue()
    {
        int started = 0;
        var queue = new RenderQueue<string, string>((request, _) =>
        {
            started++;
            return request == "bad"
                ? throw new InvalidOperationException("the engine gave up")
                : $"drew {request}";
        });

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.RequestAsync("bad"));
        Assert.Equal("the engine gave up", thrown.Message);

        Assert.Equal("drew good", await queue.RequestAsync("good"));
        Assert.Equal(2, started);
    }
}
