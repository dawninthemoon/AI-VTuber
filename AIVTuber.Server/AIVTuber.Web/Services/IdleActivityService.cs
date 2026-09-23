namespace AIVTuber.Web.Services;

public sealed class IdleActivityService
{
    private readonly object _gate = new();
    private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
    private int _busy;
    private CancellationTokenSource? _idle;

    public void Begin()
    {
        lock (_gate)
        {
            _busy++;
            _lastActivity = DateTimeOffset.UtcNow;
            _idle?.Cancel();
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
            _lastActivity = DateTimeOffset.UtcNow;
        }
    }

    public bool Publish(Action publish)
    {
        lock (_gate)
        {
            if (_busy > 0 || _idle == null || _idle.IsCancellationRequested)
            {
                return false;
            }
            publish();
            return true;
        }
    }
}
