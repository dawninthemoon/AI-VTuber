using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
        CancellationToken cancellationToken = default,
        Func<string, string, float, CancellationToken, Task>? onTextDelta = null)
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
            stream = onTextDelta != null,
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
                            emotion = new
                            {
                                type = "string",
                                @enum = new[] { "neutral", "happy", "angry", "sad", "surprised" }
                            },
                            intensity = new { type = "number", minimum = 0, maximum = 1 },
                            text = new { type = "string" }
                        },
                        required = new[] { "emotion", "intensity", "text" }
                    }
                }
            }
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(_options.RequestTimeoutSeconds, 1)));
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();

        if (onTextDelta != null)
            return await ReadStreamingAsync(response, onTextDelta, timeout.Token);

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

    private static async Task<string> ReadStreamingAsync(
        HttpResponseMessage response,
        Func<string, string, float, CancellationToken, Task> onTextDelta,
        CancellationToken cancellationToken)
    {
        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using StreamReader reader = new(stream, Encoding.UTF8);
        StreamingCharacterResponseParser parser = new();
        StringBuilder eventData = new();
        bool completed = false;

        async Task ProcessEventAsync()
        {
            if (eventData.Length == 0) return;
            string data = eventData.ToString();
            eventData.Clear();
            if (data == "[DONE]") return;

            using JsonDocument document = JsonDocument.Parse(data);
            JsonElement root = document.RootElement;
            string? type = root.GetProperty("type").GetString();
            if (type == "response.output_text.delta")
            {
                string decoded = parser.Feed(root.GetProperty("delta").GetString() ?? "");
                if (decoded.Length > 0)
                    await onTextDelta(decoded, parser.Emotion, parser.Intensity, cancellationToken);
            }
            else if (type == "response.output_text.done" && parser.RawJson.Length == 0 &&
                     root.TryGetProperty("text", out JsonElement finalText))
            {
                string decoded = parser.Feed(finalText.GetString() ?? "");
                if (decoded.Length > 0)
                    await onTextDelta(decoded, parser.Emotion, parser.Intensity, cancellationToken);
            }
            else if (type == "response.completed")
            {
                completed = true;
            }
            else if (type is "response.failed" or "error")
            {
                throw new HttpRequestException($"OpenAI streaming response failed: {data}");
            }
        }

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                await ProcessEventAsync();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (eventData.Length > 0) eventData.Append('\n');
                eventData.Append(line.AsSpan(5).TrimStart());
            }
        }
        await ProcessEventAsync();
        if (!completed)
            throw new IOException("OpenAI streaming response ended before response.completed.");

        return parser.RawJson;
    }
}
