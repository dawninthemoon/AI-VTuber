namespace AIVTuber.Web.Services;

public sealed class IdleActivityService
{
    private readonly object _gate = new();
    private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
    private int _busy;
    private CancellationTokenSource? _idle;
    private bool _idleCommitted;

    public void Begin()
    {
        lock (_gate)
        {
            _busy++;
            _lastActivity = DateTimeOffset.UtcNow;
            // Preparing idle speech may be cancelled. Once published, finish the
            // whole response (including all audio segments) before handing off.
            if (!_idleCommitted) _idle?.Cancel();
        }
    }

    public void End()
    {
        lock (_gate)
        {
            _busy--;
            _lastActivity = DateTimeOffset.UtcNow;
        }
    }

    public CancellationTokenSource? TryBegin(TimeSpan silence)
    {
        lock (_gate)
        {
            if (_busy > 0 || _idle != null || DateTimeOffset.UtcNow - _lastActivity < silence)
            {
                return null;
            }
            return _idle = new CancellationTokenSource();
        }
    }

    public void Finish()
    {
        lock (_gate)
        {
            _idle?.Dispose();
            _idle = null;
            _idleCommitted = false;
            _lastActivity = DateTimeOffset.UtcNow;
        }
    }

    public bool Publish(Action publish)
    {
        lock (_gate)
        {
            if ((!_idleCommitted && _busy > 0) || _idle == null || _idle.IsCancellationRequested)
            {
                return false;
            }
            publish();
            _idleCommitted = true;
            return true;
        }
    }
}
