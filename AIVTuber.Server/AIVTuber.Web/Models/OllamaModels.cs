using System.Text.Json.Serialization;

namespace AIVTuber.Web.Models;

public sealed class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("messages")]
    public IReadOnlyList<ChatMessage> Messages { get; init; } = [];

    [JsonPropertyName("think")]
    public bool Think { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    // Ollama에게 JSON만 반환하도록 요청
    [JsonPropertyName("format")]
    public string Format { get; init; } = "json";

    [JsonPropertyName("options")]
    public OllamaGenerationOptions Options { get; init; } = new();
}

public sealed class OllamaGenerationOptions
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = 0.6;

    [JsonPropertyName("num_predict")]
    public int NumPredict { get; init; } = 300;
}

public sealed class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaMessage Message { get; init; } = new();
}

public sealed class OllamaMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "";

    [JsonPropertyName("content")]
    public string Content { get; init; } = "";
}


// 실제 AI 캐릭터가 반환할 데이터
public sealed class AICharacterResponse
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("emotion")]
    public string Emotion { get; set; } = "neutral";

    [JsonPropertyName("intensity")]
    public float Intensity { get; set; } = 0.5f;
}