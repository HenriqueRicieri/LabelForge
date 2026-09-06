namespace LabelForge.Core.Rendering;

/// <summary>
/// One job running, one waiting, and the waiting one is always the newest. A request
/// arriving while something runs replaces whatever was waiting; the replaced request never
/// runs and its caller is told so.
///
/// This exists because cancelling a render does not stop it. The engine's Draw is a
/// synchronous CPU loop with no cancellation point in it, so a scheme that fired a request
/// every N milliseconds and cancelled the last one started a new full render while the
/// previous was still going: on a four-core machine three or four of them ran at once
/// during a drag, each slower for the company. Nothing here can stop a render either. What
/// it does is never start a second one, and never keep more than one request waiting, so
/// the work queued can never outgrow what the machine can do.
///
/// The caller decides how long to wait BEFORE asking (typing pauses first, a gesture does
/// not); this decides only what happens once it has asked. No UI types and no timers, so
/// the rules are unit-tested rather than watched.
/// </summary>
/// <typeparam name="TRequest">Whatever the work needs. Treated as opaque.</typeparam>
/// <typeparam name="TResult">What the work produces.</typeparam>
public sealed class RenderQueue<TRequest, TResult>
    where TResult : class
{
    private readonly Func<TRequest, CancellationToken, TResult> _work;
    private readonly Lock _gate = new();

    private bool _running;
    private bool _hasWaiting;
    private TRequest? _waitingRequest;
    private CancellationToken _waitingToken;
    private TaskCompletionSource<TResult?>? _waitingCompletion;

    /// <param name="work">The rendering itself. Called on a thread pool thread, never
    /// concurrently with itself, and handed the token its request came with.</param>
    public RenderQueue(Func<TRequest, CancellationToken, TResult> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _work = work;
    }

    /// <summary>
    /// Asks for the work to run with this request, and waits for the answer.
    ///
    /// The result is null when this request never ran: a newer one arrived while it was
    /// waiting, or its token was already cancelled. That is not a failure and there is
    /// nothing to show for it, because a newer request is on its way. A request that DID
    /// run returns what the work produced even if something newer has since queued up
    /// behind it, so the picture on screen is never older than one render. Null is
    /// reserved for that distinction, so the work itself must never return it.
    /// </summary>
    public Task<TResult?> RequestAsync(TRequest request, CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
        {
            return Task.FromResult<TResult?>(null);
        }

        TaskCompletionSource<TResult?>? superseded;
        var completion = new TaskCompletionSource<TResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            if (_running)
            {
                // Park it, and tell whoever was waiting that their turn will not come.
                // Completing them outside the lock keeps a continuation from running
                // arbitrary code while this holds it.
                superseded = _waitingCompletion;
                _hasWaiting = true;
                _waitingRequest = request;
                _waitingToken = token;
                _waitingCompletion = completion;
            }
            else
            {
                superseded = null;
                _running = true;
                Start(request, token, completion);
            }
        }

        superseded?.TrySetResult(null);
        return completion.Task;
    }

    private void Start(TRequest request, CancellationToken token, TaskCompletionSource<TResult?> completion)
    {
        _ = Task.Run(
            () =>
            {
                try
                {
                    completion.TrySetResult(_work(request, token));
                }
                catch (OperationCanceledException)
                {
                    // The work stopped because its request was superseded. Same answer as
                    // never having run: there is nothing to show and something newer is
                    // coming.
                    completion.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    // Whatever happened, including a throw, the queue moves on. Anything
                    // else and one bad render wedges the preview for the session.
                    RunNext();
                }
            },
            CancellationToken.None);
    }

    private void RunNext()
    {
        while (true)
        {
            TRequest request;
            CancellationToken token;
            TaskCompletionSource<TResult?> completion;

            lock (_gate)
            {
                if (!_hasWaiting)
                {
                    _running = false;
                    return;
                }

                request = _waitingRequest!;
                token = _waitingToken;
                completion = _waitingCompletion!;
                _hasWaiting = false;
                _waitingRequest = default;
                _waitingToken = default;
                _waitingCompletion = null;

                if (!token.IsCancellationRequested)
                {
                    Start(request, token, completion);
                    return;
                }
            }

            // The one waiting was cancelled before it started. Drop it and look again,
            // rather than starting work whose answer nobody wants.
            completion.TrySetResult(null);
        }
    }
}
