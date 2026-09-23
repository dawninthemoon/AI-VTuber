namespace AIVTuber.Web.Services;

internal sealed class YouTubeReconnectPolicy
{
    private int _failures;

    public TimeSpan AfterClose(TimeSpan duration, bool successful, bool progressed, bool rateLimited = false)
    {
        if (successful && progressed)
        {
            _failures = 0;
            // Resume promptly, without spinning on subsecond streams with changing cursors.
            return TimeSpan.FromSeconds(Math.Max(1, 3 - duration.TotalSeconds));
        }
        _failures = Math.Min(_failures + 1, 7);
        double seconds = Math.Min(5 * Math.Pow(2, _failures - 1), 300);
        return TimeSpan.FromSeconds(rateLimited ? Math.Max(30, seconds) : seconds);
    }

    public static TimeSpan UntilQuotaReset(DateTimeOffset now)
    {
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var nextMidnight = TimeZoneInfo.ConvertTime(now, pacific).Date.AddDays(1);
        var resetUtc = TimeZoneInfo.ConvertTimeToUtc(nextMidnight, pacific);
        return new DateTimeOffset(resetUtc) - now + TimeSpan.FromMinutes(1);
    }
}
