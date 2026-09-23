using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AIVTuber.Web.Models;
using Microsoft.Extensions.Options;

namespace AIVTuber.Web.Services;

public sealed class OpenAiChatService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;

    public OpenAiChatService(HttpClient httpClient, IOptions<OpenAiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (!_options.ChatEnabled)
        {
            throw new InvalidOperationException("OpenAI chat is disabled in configuration.");
        }

        string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY is not set.");
        }
        apiKey = apiKey.Trim();
        if (apiKey.Any(character => character > 127 || char.IsWhiteSpace(character)))
        {
            throw new InvalidOperationException(
                "OPENAI_API_KEY contains non-ASCII characters or whitespace. Enter the actual API key again.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            model = _options.Model,
            store = false,
            input = messages.Select(message => new
            {
                role = message.Role == "system" ? "developer" : message.Role,
                content = message.Content
            }),
            reasoning = new { effort = "none" },
            temperature = profile.Temperature,
            max_output_tokens = profile.NumPredict,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "character_response",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
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
                        required = new[] { "text", "emotion", "intensity" }
                    }
                }
            }
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(_options.RequestTimeoutSeconds, 1)));
        using HttpResponseMessage response = await _httpClient.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(timeout.Token),
            cancellationToken: timeout.Token);

        if (document.RootElement.TryGetProperty("output_text", out JsonElement outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString()?.Trim() ?? "";
        }

        if (document.RootElement.TryGetProperty("output", out JsonElement output) &&
            output.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out JsonElement content) ||
                    content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out JsonElement type) &&
                        type.GetString() == "output_text" &&
                        part.TryGetProperty("text", out JsonElement text))
                    {
                        return text.GetString()?.Trim() ?? "";
                    }
                }
            }
        }

        return "";
    }
}
