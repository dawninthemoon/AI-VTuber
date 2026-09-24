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

    public void Set(
        bool speaking,
        long completedVersion = 0,
        long playingVersion = 0,
        float playedSeconds = 0,
        float clipSeconds = 0)
    {
        lock (_lock)
        {
            long activeVersion = speaking ? Math.Max(playingVersion, 0) : 0;
            _status = new PlaybackStatus(speaking, DateTimeOffset.UtcNow,
                Math.Max(_status.CompletedVersion, completedVersion),
                activeVersion,
                activeVersion > 0 ? Math.Max(playedSeconds, 0) : 0,
                activeVersion > 0 ? Math.Max(clipSeconds, 0) : 0);
        }
    }
}
