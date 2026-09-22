using System.Text.Json;
using AIVTuber.Web.Models;
using Microsoft.Extensions.Options;

namespace AIVTuber.Web.Services;

public sealed class ChatService
{
    private readonly OllamaService _ollamaService;
    private readonly ChatClassifier _classifier;
    private readonly OllamaOptions _options;
    private readonly ILogger<ChatService> _logger;
    private readonly string _systemPrompt;

    private readonly List<ChatMessage> _history = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ChatService(
        OllamaService ollamaService,
        ChatClassifier classifier,
        IOptions<OllamaOptions> options,
        IWebHostEnvironment environment,
        ILogger<ChatService> logger)
    {
        _ollamaService = ollamaService;
        _classifier = classifier;
        _options = options.Value;
        _logger = logger;

        var promptPath = Path.Combine(
            environment.ContentRootPath,
            "Prompts",
            "system.txt");

        _systemPrompt =
            File.ReadAllText(promptPath).Trim();

        Reset();
    }


    public async Task<AICharacterResponse> SendAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            _history.Add(
                new ChatMessage(
                    "user",
                    userMessage
                )
            );

            var profile =
                _classifier.Classify(userMessage);

            _logger.LogInformation(
                "Chat mode: {Mode}, Think: {Think}, NumPredict: {NumPredict}",
                profile.Mode,
                profile.Think,
                profile.NumPredict
            );

            var context = BuildContext();

            // Ollama가 반환한 raw JSON 문자열
            var rawResponse =
                await _ollamaService.ChatAsync(
                    context,
                    profile,
                    cancellationToken
                );

            AICharacterResponse? result = null;

            try
            {
                result =
                    JsonSerializer.Deserialize<AICharacterResponse>(
                        rawResponse,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }
                    );
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "AI JSON 파싱 실패: {Response}",
                    rawResponse
                );
            }


            // JSON 파싱 실패 대비
            if (result == null ||
                string.IsNullOrWhiteSpace(result.Text))
            {
                result = new AICharacterResponse
                {
                    Text = string.IsNullOrWhiteSpace(rawResponse)
                        ? "잠깐, 방금 머리가 멈췄어."
                        : rawResponse,

                    Emotion = "neutral",
                    Intensity = 0.5f
                };
            }


            // 값 보정
            result.Intensity =
                Math.Clamp(
                    result.Intensity,
                    0f,
                    1f
                );

            result.Emotion =
                NormalizeEmotion(
                    result.Emotion
                );


            // 히스토리에는 사람이 보는 실제 대사만 넣음
            _history.Add(
                new ChatMessage(
                    "assistant",
                    result.Text
                )
            );

            TrimHistory();

            return result;
        }
        finally
        {
            _lock.Release();
        }
    }


    private static string NormalizeEmotion(
        string emotion)
    {
        return emotion
            .Trim()
            .ToLowerInvariant()
            switch
            {
                "happy" => "happy",
                "angry" => "angry",
                "sad" => "sad",
                "surprised" => "surprised",
                "neutral" => "neutral",

                _ => "neutral"
            };
    }


    public void Reset()
    {
        _history.Clear();

        _history.Add(
            new ChatMessage(
                "system",
                _systemPrompt
            )
        );
    }


    private IReadOnlyList<ChatMessage>
        BuildContext()
    {
        if (_history.Count <=
            _options.MaxHistoryMessages + 1)
        {
            return _history.ToList();
        }

        var recent =
            _history
                .Skip(
                    Math.Max(
                        1,
                        _history.Count -
                        _options.MaxHistoryMessages
                    )
                )
                .ToList();

        recent.Insert(
            0,
            _history[0]
        );

        return recent;
    }


    private void TrimHistory()
    {
        var maxStored =
            Math.Max(
                _options.MaxHistoryMessages * 2,
                _options.MaxHistoryMessages + 1
            );

        if (_history.Count <= maxStored)
        {
            return;
        }

        var system =
            _history[0];

        var recent =
            _history
                .Skip(
                    _history.Count -
                    _options.MaxHistoryMessages
                )
                .ToList();

        _history.Clear();

        _history.Add(system);

        _history.AddRange(recent);
    }
}