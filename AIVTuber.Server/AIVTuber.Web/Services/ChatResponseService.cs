using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

/// <summary>
/// Runs the shared response pipeline used by both the browser and live chat.
/// Calls are serialized because ChatService owns one ordered conversation history.
/// </summary>
public sealed class ChatResponseService
{
    private readonly ChatService _chatService;
    private readonly ChatClassifier _classifier;
    private readonly WikipediaSearchService _searchService;
    private readonly CharacterStateService _stateService;
    private readonly GPTSoVitsTtsService _ttsService;
    private readonly ILogger<ChatResponseService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IdleActivityService _activity;

    public ChatResponseService(
        ChatService chatService,
        ChatClassifier classifier,
        WikipediaSearchService searchService,
        CharacterStateService stateService,
        GPTSoVitsTtsService ttsService,
        ILogger<ChatResponseService> logger,
        IdleActivityService activity)
    {
        _chatService = chatService;
        _classifier = classifier;
        _searchService = searchService;
        _stateService = stateService;
        _ttsService = ttsService;
        _logger = logger;
        _activity = activity;
    }

    public async Task<ChatResponse> RespondAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("메시지가 비어 있습니다.", nameof(message));
        }

        _activity.Begin();
        bool acquired = false;
        try
        {
            await _lock.WaitAsync(cancellationToken);
            acquired = true;
            GenerationProfile profile = _classifier.Classify(message);
            SearchEvidence? evidence = null;

            async Task<AICharacterResponse> GenerateResponseAsync()
            {
                if (profile.NeedsSearch)
                {
                    try
                    {
                        evidence = await _searchService.SearchAsync(message, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "사실 검색 실패");
                        evidence = new SearchEvidence([]);
                    }
                }

                return await _chatService.SendAsync(message, evidence, cancellationToken);
            }

            Task<AICharacterResponse> responseTask = GenerateResponseAsync();

            if (profile.NeedsSearch)
            {
                await Task.WhenAny(
                    responseTask,
                    Task.Delay(TimeSpan.FromSeconds(1.2), cancellationToken));

                if (!responseTask.IsCompleted)
                {
                    string waitingLine = evidence == null ? "잠시만, 찾아볼게." : "생각 좀 해볼게.";
                    byte[]? waitingAudio = null;

                    try
                    {
                        waitingAudio = await _ttsService.GenerateAsync(waitingLine, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "검색 안내 음성 생성 실패");
                    }

                    if (!responseTask.IsCompleted && waitingAudio is { Length: > 0 })
                    {
                        long waitingMessageId = _stateService.BeginResponse(
                            waitingLine, "neutral", 0.4f, segmentCount: 1);
                        _stateService.PublishAudioSegment(
                            waitingMessageId, waitingLine, "neutral", 0.4f,
                            segmentIndex: 0, segmentCount: 1, segmentText: waitingLine,
                            audio: waitingAudio, publishedVersion: out _);
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    }
                }
            }

            AICharacterResponse response = await responseTask;
            IReadOnlyList<string> segments = TtsTextSegmenter.Split(response.Text);
            long messageId = _stateService.BeginResponse(
                response.Text, response.Emotion, response.Intensity, segments.Count);

            long audioVersion = 0;
            for (int index = 0; index < segments.Count; index++)
            {
                string segment = segments[index];
                try
                {
                    byte[] audio = await _ttsService.GenerateAsync(segment, cancellationToken);
                    bool published = _stateService.PublishAudioSegment(
                        messageId, response.Text, response.Emotion, response.Intensity,
                        index, segments.Count, segment, audio, out long publishedVersion);

                    if (published)
                    {
                        audioVersion = publishedVersion;
                    }

                    _logger.LogInformation(
                        "TTS 조각 생성 성공: {Current}/{Total}, {Bytes} bytes, Published: {Published}, Text: {Text}",
                        index + 1, segments.Count, audio.Length, published, segment);

                    if (!published)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "GPT-SoVITS TTS 조각 생성 실패: {Current}/{Total}, Text: {Text}",
                        index + 1, segments.Count, segment);
                    break;
                }
            }

            return new ChatResponse(response.Text, evidence?.Hits, audioVersion);
        }
        finally
        {
            if (acquired) _lock.Release();
            _activity.End();
        }
    }
}
