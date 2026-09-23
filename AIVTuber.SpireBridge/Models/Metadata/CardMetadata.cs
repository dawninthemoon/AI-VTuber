namespace AIVTuber.SpireBridge.Models.Metadata;

public enum CardType
{
    Attack,
    Skill,
    Power,
    Status,
    Curse
}

public enum CardTarget
{
    Enemy,
    AllEnemies,
    Self,
    None
}

public sealed record CardMetadata(
    string Id,
    string Name,
    int Cost,
    CardType Type,
    CardTarget Target,
    int Damage,
    int Block,
    int MagicNumber,
    string Description)
{
    public static CardMetadata Unknown(string? cardId) => new(
        cardId ?? "Unknown",
        cardId ?? "Unknown",
        -1,
        CardType.Skill,
        CardTarget.None,
        0,
        0,
        0,
        string.Empty);
}
