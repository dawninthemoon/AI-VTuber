namespace AIVTuber.Web.Models;

public sealed record PlaybackStatusRequest(bool Speaking);

public sealed record PlaybackStatus(
    bool Speaking,
    DateTimeOffset UpdatedAt
);
