using AIVTuber.Web.Models;
using System.Text;

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
    private readonly SemaphoreSlim _ttsPreparationSlots = new(2, 2);
    private readonly IdleActivityService _activity;
    private readonly SpeechTurnCoordinator _speechTurns;
    private readonly SpeechPlaybackWaiter _playbackWaiter;

    public ChatResponseService(
        ChatService chatService,
        ChatClassifier classifier,
        WikipediaSearchService searchService,
        CharacterStateService stateService,
        GPTSoVitsTtsService ttsService,
        ILogger<ChatResponseService> logger,
        IdleActivityService activity,
        SpeechTurnCoordinator speechTurns,
        SpeechPlaybackWaiter playbackWaiter)
    {
        _chatService = chatService;
        _classifier = classifier;
        _searchService = searchService;
        _stateService = stateService;
        _ttsService = ttsService;
        _logger = logger;
        _activity = activity;
        _speechTurns = speechTurns;
        _playbackWaiter = playbackWaiter;
    }

    public async Task<ChatResponse> RespondAsync(
        string message,
        CancellationToken cancellationToken = default,
        SpeechSource source = SpeechSource.DirectChat)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("메시지가 비어 있습니다.", nameof(message));
        }

        _activity.Begin();
        bool acquired = false;
        bool speechTurnAcquired = false;
        long publishedMessageId = 0;
        long audioVersion = 0;
        CancellationTokenSource? speculative = null;
        List<(string Text, Task<byte[]?> Audio)> prepared = [];
        try
        {
            await _speechTurns.WaitAsync(source, cancellationToken);
            speechTurnAcquired = true;
            using var activeTurn = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _speechTurns.ActiveCancellationToken);
            cancellationToken = activeTurn.Token;
            speculative = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            StreamingTtsSegmenter streamSegments = new();
            StringBuilder streamedText = new();

            async Task<byte[]?> PrepareAudioAsync(string text)
            {
                try
                {
                    await _ttsPreparationSlots.WaitAsync(speculative.Token);
                    try { return await _ttsService.GenerateAsync(text, speculative.Token); }
                    finally { _ttsPreparationSlots.Release(); }
                }
                catch (OperationCanceledException) when (speculative.IsCancellationRequested)
                {
                    return null;
                }
                catch (Exception error)
                {
                    _logger.LogWarning(error, "Speculative TTS failed for {Text}.", text);
                    return null;
                }
            }

            void QueuePrepared(string text) => prepared.Add((text, PrepareAudioAsync(text)));

            Task OnTextDeltaAsync(string delta, string emotion, float intensity, CancellationToken _)
            {
                streamedText.Append(delta);
                foreach (string segment in streamSegments.Push(delta)) QueuePrepared(segment);
                return Task.CompletedTask;
            }

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

                return await _chatService.SendAsync(
                    message, evidence, cancellationToken, OnTextDeltaAsync);
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
                            audio: waitingAudio, publishedVersion: out long waitingVersion);
                        publishedMessageId = waitingMessageId;
                        audioVersion = waitingVersion;
                    }
                }
            }

            AICharacterResponse response = await responseTask;
            bool usePrepared = streamedText.Length > 0 &&
                string.Equals(streamedText.ToString().Trim(), response.Text, StringComparison.Ordinal);
            if (usePrepared)
            {
                string tail = streamSegments.Flush();
                if (tail.Length > 0) QueuePrepared(tail);
            }
            else
            {
                speculative.Cancel();
            }
            if (audioVersion > 0)
            {
                await _playbackWaiter.WaitAsync(publishedMessageId, audioVersion, cancellationToken);
            }
            IReadOnlyList<string> segments = usePrepared
                ? prepared.Select(item => item.Text).ToArray()
                : TtsTextSegmenter.Split(response.Text);
            long messageId = _stateService.BeginResponse(
                response.Text, response.Emotion, response.Intensity, segments.Count);
            _chatService.RegisterAudioMessage(messageId);
            publishedMessageId = messageId;
            audioVersion = 0;

            for (int index = 0; index < segments.Count; index++)
            {
                string segment = segments[index];
                try
                {
                    byte[]? audio = usePrepared
                        ? await prepared[index].Audio
                        : await _ttsService.GenerateAsync(segment, cancellationToken);
                    if (audio is not { Length: > 0 } && usePrepared)
                        audio = await _ttsService.GenerateAsync(segment, cancellationToken);
                    if (audio is not { Length: > 0 }) break;
                    // Generate the next clip while the current one plays, then
                    // publish only after Unity has consumed the previous version.
                    if (audioVersion > 0 && publishedMessageId == messageId &&
                        !await _playbackWaiter.WaitAsync(messageId, audioVersion, cancellationToken))
                        break;
                    bool published = _stateService.PublishAudioSegment(
                        messageId, response.Text, response.Emotion, response.Intensity,
                        index, segments.Count, segment, audio, out long publishedVersion);

                    if (published)
                    {
                        publishedMessageId = messageId;
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
            speculative?.Cancel();
            speculative?.Dispose();
            if (speechTurnAcquired)
            {
                if (audioVersion > 0)
                    _ = ReleaseAfterPlaybackAsync(publishedMessageId, audioVersion);
                else
                    _speechTurns.Release();
            }
            if (acquired) _lock.Release();
            _activity.End();
        }
    }

    private async Task ReleaseAfterPlaybackAsync(long messageId, long audioVersion)
    {
        try
        {
            await _playbackWaiter.WaitAsync(messageId, audioVersion);
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "Could not confirm Unity playback for version {Version}.", audioVersion);
        }
        finally
        {
            _speechTurns.Release();
        }
    }
}
