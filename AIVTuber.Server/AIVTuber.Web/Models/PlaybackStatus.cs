namespace AIVTuber.Web.Models;

public sealed record PlaybackStatusRequest(bool Speaking, long CompletedVersion = 0);

public sealed record PlaybackStatus(
    bool Speaking,
    DateTimeOffset UpdatedAt,
    long CompletedVersion = 0
);
