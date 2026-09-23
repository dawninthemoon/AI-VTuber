namespace AIVTuber.Web.Services;

// Counts RPC attempts, not quota units (YouTube controls quota accounting).
internal sealed class YouTubeReconnectPolicy
{
    private readonly Queue<TimeSpan> _attempts = new();
    private int _shortConnections;

    public bool TryStart(TimeSpan now)
    {
        while (_attempts.TryPeek(out var oldest) && now - oldest >= TimeSpan.FromHours(1))
        {
            _attempts.Dequeue();
        }
        if (_attempts.Count >= 12)
        {
            return false;
        }
        _attempts.Enqueue(now);
        return true;
    }

    public TimeSpan AfterClose(TimeSpan duration, bool successful)
    {
        if (duration >= TimeSpan.FromMinutes(2))
        {
            _shortConnections = 0;
            return TimeSpan.FromSeconds(successful ? 1 : 5);
        }
        // Receiving one batch does not make a rapidly closing stream healthy.
        _shortConnections = Math.Min(_shortConnections + 1, 7);
        return TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, _shortConnections - 1), 300));
    }
}
