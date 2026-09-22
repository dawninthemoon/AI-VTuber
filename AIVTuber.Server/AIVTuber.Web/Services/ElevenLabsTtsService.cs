using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AIVTuber.Web.Services;

public sealed class ElevenLabsTtsService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _voiceId;

    public ElevenLabsTtsService(
        HttpClient httpClient)
    {
        _httpClient = httpClient;

        _apiKey =
            Environment.GetEnvironmentVariable(
                "ELEVENLABS_API_KEY"
            )
            ?? throw new InvalidOperationException(
                "ELEVENLABS_API_KEY 환경변수가 없습니다."
            );

        _voiceId =
            Environment.GetEnvironmentVariable(
                "ELEVENLABS_VOICE_ID"
            )
            ?? throw new InvalidOperationException(
                "ELEVENLABS_VOICE_ID 환경변수가 없습니다."
            );
    }

    public async Task<byte[]> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"https://api.elevenlabs.io/v1/text-to-speech/{_voiceId}" +
            "?output_format=mp3_44100_128";

        var body = new
        {
            text = text,

            model_id = "eleven_flash_v2_5",

            voice_settings = new
            {
                stability = 0.45,
                similarity_boost = 0.75,
                style = 0.3,
                use_speaker_boost = true
            }
        };

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                url
            );

        request.Headers.Add(
            "xi-api-key",
            _apiKey
        );

        request.Content =
            JsonContent.Create(body);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken
            );

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadAsByteArrayAsync(
                cancellationToken
            );
    }
}