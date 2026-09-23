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
    IOptions<IdleOptions> options,
    ILogger<IdleBehaviorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }
        TimeSpan delay = NextDelay();
        long version = state.Get().Version;
        DateTimeOffset changedAt = DateTimeOffset.UtcNow;
        var recentLines = new Queue<string>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var current = state.Get();
            if (current.Version != version)
            {
                version = current.Version;
                changedAt = DateTimeOffset.UtcNow;
            }
            if (!CanSpeak() || DateTimeOffset.UtcNow - changedAt < delay)
            {
                continue;
            }
            var idle = activity.TryBegin(delay);
            if (idle == null)
            {
                continue;
            }
            try
            {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, idle.Token);
                cancellation.CancelAfter(TimeSpan.FromSeconds(30));
                var snapshot = spire.GetLastState();
                string topic = Random.Shared.Next(3) switch
                {
                    0 => "캐릭터의 취향에 맞는 가벼운 혼잣말",
                    1 => "부담을 주지 않는 다정한 일상 잡담",
                    _ => "캐릭터다운 짧은 상상이나 소소한 계획"
                };
                // Only use observations that were actually received recently.
                string game = snapshot != null && DateTimeOffset.UtcNow - snapshot.ReceivedAt < TimeSpan.FromSeconds(30)
                    ? System.Text.Json.JsonSerializer.Serialize(snapshot) : "없음";
                var response = await chat.GenerateIdleAsync(topic, game, recentLines.ToArray(), cancellation.Token);
                if (response == null || recentLines.Contains(response.Text))
                {
                    continue;
                }
                byte[] audio = await tts.GenerateAsync(response.Text, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (audio.Length == 0 || !CanSpeak() || state.Get().Version != version)
                {
                    continue;
                }
                if (activity.Publish(() =>
                {
                    long id = state.BeginResponse(response.Text, response.Emotion, response.Intensity);
                    state.PublishAudioSegment(id, response.Text, response.Emotion, response.Intensity,
                        0, 1, response.Text, audio, out _);
                }))
                {
                    recentLines.Enqueue(response.Text);
                    while (recentLines.Count > 8) recentLines.Dequeue();
                    logger.LogInformation("Idle speech: {Text}", response.Text);
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Chat or game speech takes priority; discard this idle response.
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "Idle speech failed; retrying after the next idle interval.");
            }
            finally
            {
                activity.Finish();
                delay = NextDelay();
                changedAt = DateTimeOffset.UtcNow;
            }
        }
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
