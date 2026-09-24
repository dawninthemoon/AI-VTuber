namespace AIVTuber.Web.Services;

public enum SpeechSource
{
    Idle = 0,
    Game = 1,
    LiveChat = 2,
    DirectChat = 3
}

// A published turn keeps ownership until Unity finishes its last audio segment.
// Higher-priority work moves ahead of waiting turns, but never cuts off a
// published turn. Explicit reset is the only current playback interruption.
public sealed class SpeechTurnCoordinator
{
    private readonly object _gate = new();
    private readonly List<Waiter> _waiting = [];
    private bool _occupied;
    private long _nextOrder;
    private CancellationTokenSource? _activeCancellation;

    public CancellationToken ActiveCancellationToken
    {
        get { lock (_gate) return _activeCancellation?.Token ?? CancellationToken.None; }
    }

    public void InterruptCurrent()
    {
        lock (_gate) _activeCancellation?.Cancel();
    }

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        WaitAsync(SpeechSource.DirectChat, cancellationToken);

    public async Task WaitAsync(SpeechSource source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Waiter? waiter = null;
        lock (_gate)
        {
            if (!_occupied)
            {
                _occupied = true;
                _activeCancellation = new CancellationTokenSource();
            }
            else
            {
                waiter = new Waiter(source, _nextOrder++);
                _waiting.Add(waiter);
            }
        }

        if (waiter == null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Release();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return;
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(
            () => Cancel(waiter, cancellationToken));
        await waiter.Ready.Task.ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            Release();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public void Release()
    {
        CancellationTokenSource? completed;
        lock (_gate)
        {
            if (!_occupied) throw new InvalidOperationException("No speech turn is active.");
            completed = _activeCancellation;
            if (_waiting.Count == 0)
            {
                _occupied = false;
                _activeCancellation = null;
            }
            else
            {
                Waiter next = _waiting[0];
                foreach (Waiter candidate in _waiting)
                {
                    if (candidate.Source > next.Source ||
                        (candidate.Source == next.Source && candidate.Order < next.Order))
                        next = candidate;
                }
                _waiting.Remove(next);
                _activeCancellation = new CancellationTokenSource();
                next.Ready.TrySetResult();
            }
        }
        completed?.Dispose();
    }

    private void Cancel(Waiter waiter, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_waiting.Remove(waiter)) waiter.Ready.TrySetCanceled(cancellationToken);
        }
    }

    private sealed class Waiter(SpeechSource source, long order)
    {
        public SpeechSource Source { get; } = source;
        public long Order { get; } = order;
        public TaskCompletionSource Ready { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
