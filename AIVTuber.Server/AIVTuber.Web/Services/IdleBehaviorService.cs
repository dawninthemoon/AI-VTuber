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
    private static readonly string[] InterestTopics =
    [
        "게임 이야기. 실제로 플레이 중이라고 꾸미지 말고, 좋아하는 게임 요소나 해보고 싶은 게임에 관해 자연스럽게 혼잣말해.",
        "만화나 애니메이션 이야기. 보지 않은 작품의 구체적인 내용을 지어내지 말고, 좋아하는 장르나 보고 싶은 작품 분위기를 이야기해.",
        "음악 이야기. 락이나 J-POP 취향을 중심으로 듣고 싶은 음악, 밴드 사운드, 노래 분위기에 관해 이야기해.",
        "디저트 이야기. 먹고 싶은 디저트, 좋아하는 맛이나 조합에 관해 가볍게 이야기해."
    ];

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
        int previousTopic = -1;
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
                int topicIndex;
                do
                {
                    topicIndex = Random.Shared.Next(InterestTopics.Length);
                }
                while (topicIndex == previousTopic);
                previousTopic = topicIndex;
                string topic = InterestTopics[topicIndex];
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
                    long id = state.BeginResponse(
                        response.Text,
                        response.Emotion,
                        response.Intensity,
                        isIdle: true);
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
