using System.Net.Http.Json;
using AIVTuber.Web.Models;
using Microsoft.Extensions.Options;

namespace AIVTuber.Web.Services;

public sealed class OllamaService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;

    public OllamaService(
        HttpClient httpClient,
        IOptions<OllamaOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        _httpClient.BaseAddress = new Uri(
            _options.BaseUrl.TrimEnd('/') + "/");
    }

    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationProfile profile,
        CancellationToken cancellationToken = default)
    {
        var request = new OllamaChatRequest
        {
            Model = _options.Model,
            Messages = messages,
            Think = profile.Think,
            Stream = false,
            Options = new OllamaGenerationOptions
            {
                Temperature = profile.Temperature,
                NumPredict = profile.NumPredict
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "api/chat",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<OllamaChatResponse>(
                cancellationToken: cancellationToken);

        return result?.Message.Content?.Trim() ?? "";
    }
}
