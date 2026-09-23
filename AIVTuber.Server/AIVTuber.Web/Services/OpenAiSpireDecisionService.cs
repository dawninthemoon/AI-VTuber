using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AIVTuber.Web.Models;
using Microsoft.Extensions.Options;

namespace AIVTuber.Web.Services;

public sealed class OpenAiSpireDecisionService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiSpireDecisionService> _logger;
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OpenAiSpireDecisionService(HttpClient httpClient, IOptions<OpenAiOptions> options, ILogger<OpenAiSpireDecisionService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SpireModelDecision?> DecideAsync(SpireTurnContext context, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || context.LegalActions.Count == 0)
        {
            return null;
        }

        string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("OpenAI is enabled but  is not set.");
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            TimeSpan interval = TimeSpan.FromSeconds(Math.Max(_options.MinimumDecisionIntervalSeconds, 0));
            if (DateTimeOffset.UtcNow - _lastRequestAt < interval)
            {
                return null;
            }

            _lastRequestAt = DateTimeOffset.UtcNow;
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(CreateRequestBody(context));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(_options.RequestTimeoutSeconds, 1)));
            using HttpResponseMessage response = await _httpClient.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            string body = await response.Content.ReadAsStringAsync(timeout.Token);
            return ParseDecision(body);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "OpenAI Spire decision failed; using validated fallback.");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private object CreateRequestBody(SpireTurnContext context)
    {
        string[] actionIds = context.LegalActions.Select(action => action.Id).ToArray();
        return new
        {
            model = _options.Model,
            store = false,
            input = context.Prompt,
            max_output_tokens = 160,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "spire_turn_decision",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            action_id = new { type = "string", @enum = actionIds },
                            speech = new { type = "string" },
                            emotion = new { type = "string", @enum = new[] { "neutral", "happy", "angry", "sad", "surprised" } },
                            intensity = new { type = "number" }
                        },
                        required = new[] { "action_id", "speech", "emotion", "intensity" }
                    }
                }
            }
        };
    }

    private static SpireModelDecision? ParseDecision(string responseBody)
    {
        using JsonDocument document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("output", out JsonElement output) || output.ValueKind != JsonValueKind.Array) return null;
        foreach (JsonElement item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (JsonElement part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out JsonElement type) && type.GetString() == "output_text" && part.TryGetProperty("text", out JsonElement text))
                {
                    return JsonSerializer.Deserialize<SpireModelDecision>(text.GetString() ?? "", new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
        }
        return null;
    }
}
