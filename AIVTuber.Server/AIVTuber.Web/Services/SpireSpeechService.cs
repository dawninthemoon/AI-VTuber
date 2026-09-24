using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

// Speech must never delay the command that CommunicationMod is waiting for.
public sealed class SpireSpeechService
{
    private readonly CharacterStateService _characterStateService;
    private readonly GPTSoVitsTtsService _ttsService;
    private readonly ILogger<SpireSpeechService> _logger;
    private readonly IdleActivityService _activity;
    private readonly SpeechTurnCoordinator _speechTurns;
    private readonly SpeechPlaybackWaiter _playbackWaiter;
    private long _latestSpeech;

    public SpireSpeechService(
        CharacterStateService characterStateService,
        GPTSoVitsTtsService ttsService,
        ILogger<SpireSpeechService> logger,
        IdleActivityService activity,
        SpeechTurnCoordinator speechTurns,
        SpeechPlaybackWaiter playbackWaiter)
    {
        _characterStateService = characterStateService;
        _ttsService = ttsService;
        _logger = logger;
        _activity = activity;
        _speechTurns = speechTurns;
        _playbackWaiter = playbackWaiter;
    }

    public void Publish(SpireModelDecision decision)
    {
        string speech = decision.Speech.Trim();
        if (string.IsNullOrWhiteSpace(speech))
        {
            return;
        }

        _activity.Begin();
        speech = speech[..Math.Min(speech.Length, 180)];
        string emotion = NormalizeEmotion(decision.Emotion);
        float intensity = Math.Clamp(decision.Intensity, 0f, 1f);
        long speechNumber = Interlocked.Increment(ref _latestSpeech);
        _ = GenerateAndPublishAsync(speech, emotion, intensity, speechNumber);
    }

    private async Task GenerateAndPublishAsync(string speech, string emotion, float intensity, long speechNumber)
    {
        bool acquired = false;
        long messageId = 0;
        long audioVersion = 0;
        try
        {
            await _speechTurns.WaitAsync(SpeechSource.Game);
            acquired = true;
            using var activeTurn = CancellationTokenSource.CreateLinkedTokenSource(
                _speechTurns.ActiveCancellationToken);
            if (speechNumber != Interlocked.Read(ref _latestSpeech)) return;
            byte[] audio = await _ttsService.GenerateAsync(speech, activeTurn.Token);
            activeTurn.Token.ThrowIfCancellationRequested();
            if (speechNumber != Interlocked.Read(ref _latestSpeech)) return;
            messageId = _characterStateService.BeginResponse(speech, emotion, intensity);
            _characterStateService.PublishAudioSegment(
                messageId,
                speech,
                emotion,
                intensity,
                segmentIndex: 0,
                segmentCount: 1,
                segmentText: speech,
                audio,
                publishedVersion: out audioVersion);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Game speech was interrupted.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Spire speech TTS generation failed.");
        }
        finally
        {
            if (acquired)
            {
                if (audioVersion > 0)
                    _ = ReleaseAfterPlaybackAsync(messageId, audioVersion);
                else
                    _speechTurns.Release();
            }
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
            _logger.LogWarning(error, "Could not confirm game speech playback for version {Version}.", audioVersion);
        }
        finally
        {
            _speechTurns.Release();
        }
    }

    private static string NormalizeEmotion(string? emotion)
    {
        return emotion?.Trim().ToLowerInvariant() switch
        {
            "happy" => "happy",
            "angry" => "angry",
            "sad" => "sad",
            "surprised" => "surprised",
            _ => "neutral"
        };
    }
}
