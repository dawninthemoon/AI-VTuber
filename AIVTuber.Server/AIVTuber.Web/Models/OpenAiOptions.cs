namespace AIVTuber.Web.Models;

public sealed class OpenAiOptions
{
    public bool Enabled { get; init; }
    public string Model { get; init; } = "gpt-6-luna";
    public int MinimumDecisionIntervalSeconds { get; init; } = 3;
    public int RequestTimeoutSeconds { get; init; } = 12;
}
