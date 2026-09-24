namespace AIVTuber.Web.Models;

public sealed record PlaybackStatusRequest(
    bool Speaking,
    long CompletedVersion = 0,
    long PlayingVersion = 0,
    float PlayedSeconds = 0,
    float ClipSeconds = 0);

public sealed record PlaybackStatus(
    bool Speaking,
    DateTimeOffset UpdatedAt,
    long CompletedVersion = 0,
    long PlayingVersion = 0,
    float PlayedSeconds = 0,
    float ClipSeconds = 0
);
