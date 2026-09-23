using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

// Speech must never delay the command that CommunicationMod is waiting for.
public sealed class SpireSpeechService
{
    private readonly CharacterStateService _characterStateService;
    private readonly GPTSoVitsTtsService _ttsService;
    private readonly ILogger<SpireSpeechService> _logger;

    public SpireSpeechService(
        CharacterStateService characterStateService,
        GPTSoVitsTtsService ttsService,
        ILogger<SpireSpeechService> logger)
    {
        _characterStateService = characterStateService;
        _ttsService = ttsService;
        _logger = logger;
    }

    public void Publish(SpireModelDecision decision)
    {
        string speech = decision.Speech.Trim();
        if (string.IsNullOrWhiteSpace(speech))
        {
            return;
        }

        speech = speech[..Math.Min(speech.Length, 180)];
        string emotion = NormalizeEmotion(decision.Emotion);
        float intensity = Math.Clamp(decision.Intensity, 0f, 1f);
        long messageId = _characterStateService.BeginResponse(speech, emotion, intensity);

        _ = GenerateAndPublishAsync(messageId, speech, emotion, intensity);
    }

    private async Task GenerateAndPublishAsync(long messageId, string speech, string emotion, float intensity)
    {
        try
        {
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
