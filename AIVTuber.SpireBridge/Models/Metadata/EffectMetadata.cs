namespace AIVTuber.SpireBridge.Models.Metadata;

public sealed record RelicMetadata(string Id, string Name, string Description, int MagicNumber)
{
    public static RelicMetadata Unknown(string? id) => new(id ?? "Unknown", id ?? "Unknown", string.Empty, 0);
}

public sealed record PotionMetadata(string Id, string Name, string Description, int MagicNumber)
{
    public static PotionMetadata Unknown(string? id) => new(id ?? "Unknown", id ?? "Unknown", string.Empty, 0);
}

public sealed record PowerMetadata(string Id, string Name, string Description, int MagicNumber)
{
    public static PowerMetadata Unknown(string? id) => new(id ?? "Unknown", id ?? "Unknown", string.Empty, 0);
}
