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

    public SpireSpeechService(
        CharacterStateService characterStateService,
        GPTSoVitsTtsService ttsService,
        ILogger<SpireSpeechService> logger,
        IdleActivityService activity,
        SpeechTurnCoordinator speechTurns)
    {
        _characterStateService = characterStateService;
        _ttsService = ttsService;
        _logger = logger;
        _activity = activity;
        _speechTurns = speechTurns;
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
        _ = GenerateAndPublishAsync(speech, emotion, intensity);
    }

    private async Task GenerateAndPublishAsync(string speech, string emotion, float intensity)
    {
        bool acquired = false;
        try
        {
            await _speechTurns.WaitAsync();
            acquired = true;
            long messageId = _characterStateService.BeginResponse(speech, emotion, intensity);
            byte[] audio = await _ttsService.GenerateAsync(speech);
            _characterStateService.PublishAudioSegment(
                messageId,
                speech,
                emotion,
                intensity,
                segmentIndex: 0,
                segmentCount: 1,
                segmentText: speech,
                audio,
                publishedVersion: out _);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Spire speech TTS generation failed.");
        }
        finally
        {
            if (acquired) _speechTurns.Release();
            _activity.End();
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
