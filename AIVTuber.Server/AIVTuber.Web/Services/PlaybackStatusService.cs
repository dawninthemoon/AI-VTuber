using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

public sealed class PlaybackStatusService
{
    private readonly object _lock = new();
    private PlaybackStatus _status = new(false, DateTimeOffset.UtcNow);
    private DateTimeOffset _viewerSeenAt = DateTimeOffset.MinValue;

    public void ViewerHeartbeat()
    {
        lock (_lock) { _viewerSeenAt = DateTimeOffset.UtcNow; }
    }

    public bool ViewerConnected()
    {
        lock (_lock) { return DateTimeOffset.UtcNow - _viewerSeenAt < TimeSpan.FromSeconds(10); }
    }

    public PlaybackStatus Get()
    {
        lock (_lock)
        {
            return _status;
        }
    }

    public void Set(bool speaking, long completedVersion = 0)
    {
        lock (_lock)
        {
            _status = new PlaybackStatus(speaking, DateTimeOffset.UtcNow,
                Math.Max(_status.CompletedVersion, completedVersion));
        }
    }
}
