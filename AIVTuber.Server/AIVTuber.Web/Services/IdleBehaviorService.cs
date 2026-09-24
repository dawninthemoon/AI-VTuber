using AIVTuber.Web.Models;
using Microsoft.Extensions.Options;

namespace AIVTuber.Web.Services;

public sealed class IdleBehaviorService(
    IdleActivityService activity,
    PlaybackStatusService playback,
    CharacterStateService state,
    ChatService chat,
    GPTSoVitsTtsService tts,
    SpireTurnService spire,
    SpeechTurnCoordinator speechTurns,
    IOptions<IdleOptions> options,
    ILogger<IdleBehaviorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        var memory = new IdleMonologueMemory();
        bool continuing = false;
        TimeSpan initialDelay = NextDelay();
        long version = state.Get().Version;
        DateTimeOffset changedAt = DateTimeOffset.UtcNow;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var current = state.Get();
            if (current.Version != version)
            {
                version = current.Version;
                changedAt = DateTimeOffset.UtcNow;
                if (!current.IsIdle) continuing = false;
            }
            TimeSpan delay = continuing
                ? TimeSpan.FromSeconds(Math.Clamp(options.Value.ContinuationPauseSeconds, 1, 30))
                : initialDelay;
            if (!CanSpeak() || DateTimeOffset.UtcNow - changedAt < delay) continue;
            var idle = activity.TryBegin(delay);
            if (idle == null) continue;

            bool acquired = false;
            bool finished = false;
            bool playbackUnavailable = false;
            long messageId = 0, audioVersion = 0;
            try
            {
                // Only unpublished preparation can be cancelled by an incoming chat.
                using var preparing = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, idle.Token);
                preparing.CancelAfter(TimeSpan.FromSeconds(60));
                await speechTurns.WaitAsync(SpeechSource.Idle, preparing.Token);
                acquired = true;
                CancellationToken speechInterrupt = speechTurns.ActiveCancellationToken;
                using var activePreparation = CancellationTokenSource.CreateLinkedTokenSource(
                    preparing.Token, speechInterrupt);
                if (!CanSpeak() || state.Get().Version != version) continue;
                var snapshot = spire.GetLastState();
                string game = snapshot != null && DateTimeOffset.UtcNow - snapshot.ReceivedAt < TimeSpan.FromSeconds(30)
                    ? System.Text.Json.JsonSerializer.Serialize(snapshot) : "없음";
                AICharacterResponse? response = null;
                // One retry for repetition; do not loop indefinitely spending API calls.
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    response = await chat.GenerateIdleAsync(memory.NextDirection(), game, memory.Recent, activePreparation.Token);
                    if (response != null && !memory.IsRepetitive(response.Text)) break;
                    response = null;
                }
                if (response == null) continue;
                var segments = TtsTextSegmenter.Split(response.Text);
                if (segments.Count == 0) continue;
                byte[] firstAudio = await tts.GenerateAsync(segments[0], activePreparation.Token);
                activePreparation.Token.ThrowIfCancellationRequested();
                if (firstAudio.Length == 0 || !CanSpeak() || state.Get().Version != version) continue;

                // This atomic commit races safely with activity.Begin(). From here on,
                // incoming chats wait on speechTurns while ALL segments finish.
                if (!activity.Publish(() =>
                {
                    messageId = state.BeginResponse(response.Text, response.Emotion,
                        response.Intensity, segments.Count, isIdle: true);
                    state.PublishAudioSegment(messageId, response.Text, response.Emotion, response.Intensity,
                        0, segments.Count, segments[0], firstAudio, out audioVersion);
                })) continue;

                memory.Remember(response.Text);
                logger.LogInformation("Idle paragraph started ({Segments} segments): {Text}", segments.Count, response.Text);
                for (int index = 1; index < segments.Count; index++)
                {
                    // Prepare the next audio while the current segment is playing.
                    // Do not use the pre-publication cancellation token here.
                    using var generation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, speechInterrupt);
                    generation.CancelAfter(TimeSpan.FromSeconds(60));
                    byte[] audio = await tts.GenerateAsync(segments[index], generation.Token);
                    if (!await WaitForPlaybackAsync(messageId, audioVersion, speechInterrupt))
                    {
                        playbackUnavailable = true;
                        break;
                    }
                    if (!state.PublishAudioSegment(messageId, response.Text, response.Emotion, response.Intensity,
                        index, segments.Count, segments[index], audio, out audioVersion)) break;
                    if (index == segments.Count - 1) finished = true;
                }
                if (segments.Count == 1) finished = true;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Idle preparation cancelled or generation timed out.");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "Idle speech failed; waiting for published audio before handoff.");
            }
            finally
            {
                try
                {
                    // Includes failures halfway through TTS: drain audio already published.
                    if (audioVersion > 0 && !playbackUnavailable && !stoppingToken.IsCancellationRequested)
                        finished = await WaitForPlaybackAsync(messageId, audioVersion, stoppingToken) && finished;
                }
                finally
                {
                    activity.Finish();
                    if (acquired) speechTurns.Release();
                    continuing = finished;
                    initialDelay = NextDelay();
                    changedAt = DateTimeOffset.UtcNow;
                    version = state.Get().Version;
                }
            }
        }
    }

    private async Task<bool> WaitForPlaybackAsync(long messageId, long audioVersion, CancellationToken cancellationToken)
    {
        if (audioVersion <= 0) return false;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(90);
        while (state.Get().MessageId == messageId && playback.ViewerConnected())
        {
            if (playback.Get().CompletedVersion >= audioVersion) return true;
            if (DateTimeOffset.UtcNow >= deadline)
            {
                logger.LogWarning("Idle playback acknowledgement timed out for version {Version}; releasing the speech turn.", audioVersion);
                return false;
            }
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }

    private bool CanSpeak()
    {
        var status = playback.Get();
        var current = state.Get();
        return !status.Speaking && playback.ViewerConnected()
            && (!current.HasAudio || status.CompletedVersion >= current.Version);
    }

    private TimeSpan NextDelay()
    {
        int minimum = Math.Clamp(options.Value.MinimumSilenceSeconds, 10, 3600);
        int maximum = Math.Clamp(options.Value.MaximumSilenceSeconds, minimum, 3600);
        return TimeSpan.FromSeconds(Random.Shared.Next(minimum, maximum + 1));
    }
}
