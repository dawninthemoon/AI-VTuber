namespace AIVTuber.Web.Models;

public sealed record SpireStateSnapshot(
    DateTimeOffset ReceivedAt,
    string? Screen,
    int? Floor,
    int? CurrentHp,
    int? MaxHp,
    int? Energy,
    bool ReadyForCommand
);
