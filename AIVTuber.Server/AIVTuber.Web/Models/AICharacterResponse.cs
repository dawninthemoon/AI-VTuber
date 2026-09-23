using System.Text.Json.Serialization;

namespace AIVTuber.Web.Models;

public sealed class AICharacterResponse
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("emotion")]
    public string Emotion { get; set; } = "neutral";

    [JsonPropertyName("intensity")]
    public float Intensity { get; set; } = 0.5f;
}
