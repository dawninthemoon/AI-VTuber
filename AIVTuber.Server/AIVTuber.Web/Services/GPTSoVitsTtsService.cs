using System.Net.Http.Json;

namespace AIVTuber.Web.Services;

public sealed class GPTSoVitsTtsService
{
    private readonly HttpClient _httpClient;
    private readonly string _refAudioPath;

    private const string PromptText =
        "부모가 저지르는 큰 실수 중 하나는 자기 아이를 다른 집 아이와 비교하는 것이다.";

    public GPTSoVitsTtsService(
        HttpClient httpClient,
        IWebHostEnvironment environment)
    {
        _httpClient = httpClient;
        _refAudioPath = Path.GetFullPath(
            Path.Combine(
                environment.ContentRootPath,
                "..",
                "..",
                "Resources",
                "kss_sample.wav"));

        _httpClient.BaseAddress =
            new Uri("http://127.0.0.1:9881/");
    }

    public async Task<byte[]> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_refAudioPath))
        {
            throw new FileNotFoundException(
                "GPT-SoVITS 참조 음성이 없습니다. Resources/kss_sample.wav 파일을 추가하세요.",
                _refAudioPath);
        }

        var body = new
        {
            text = text,

            text_lang = "ko",

            ref_audio_path = _refAudioPath,

            prompt_text = PromptText,

            prompt_lang = "ko",

            text_split_method = "cut5",

            batch_size = 1,

            media_type = "wav",

            streaming_mode = false
        };

        using var response =
            await _httpClient.PostAsJsonAsync(
                "tts",
                body,
                cancellationToken
            );

        if (!response.IsSuccessStatusCode)
        {
            string error =
                await response.Content.ReadAsStringAsync(
                    cancellationToken
                );

            throw new HttpRequestException(
                $"GPT-SoVITS TTS failed: {(int)response.StatusCode} {response.StatusCode}\n{error}"
            );
        }

        return await response.Content
            .ReadAsByteArrayAsync(
                cancellationToken
            );
    }
}
