namespace AIVTuber.Web.Models;

public sealed class OllamaOptions
{
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "qwen3:8b";
    public int MaxHistoryMessages { get; init; } = 24;
}
