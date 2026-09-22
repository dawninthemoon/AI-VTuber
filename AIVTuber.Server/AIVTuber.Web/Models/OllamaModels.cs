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

    // Restrict the spoken response to the fields Unity consumes.
    [JsonPropertyName("format")]
    public object Format { get; init; } = new
    {
        type = "object",
        properties = new
        {
            text = new { type = "string" },
            emotion = new
            {
                type = "string",
                @enum = new[] { "neutral", "happy", "angry", "sad", "surprised" }
            },
            intensity = new { type = "number", minimum = 0, maximum = 1 }
        },
        required = new[] { "text", "emotion", "intensity" },
        additionalProperties = false
    };

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
