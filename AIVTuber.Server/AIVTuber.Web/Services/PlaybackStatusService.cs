using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

public sealed class PlaybackStatusService
{
    private readonly object _lock = new();
    private PlaybackStatus _status = new(false, DateTimeOffset.UtcNow);

    public PlaybackStatus Get()
    {
        lock (_lock)
        {
            return _status;
        }
    }

    public void Set(bool speaking)
    {
        lock (_lock)
        {
            _status = new PlaybackStatus(speaking, DateTimeOffset.UtcNow);
        }
    }
}
