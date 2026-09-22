namespace AIVTuber.Web.Models;

public sealed class OllamaOptions
{
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "qwen3:8b";
    public bool Think { get; init; } = true;
    public double Temperature { get; init; } = 0.6;
    public int NumPredict { get; init; } = 300;

    // system + 최근 대화 N개만 모델에 전달
    public int MaxHistoryMessages { get; init; } = 24;
}
