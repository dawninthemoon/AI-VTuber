namespace AIVTuber.Web.Services;

public sealed class SpeechPlaybackWaiter(
    CharacterStateService state,
    PlaybackStatusService playback,
    ILogger<SpeechPlaybackWaiter> logger)
{
    public async Task<bool> WaitAsync(
        long messageId,
        long audioVersion,
        CancellationToken cancellationToken = default)
    {
        if (audioVersion <= 0) return false;
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(90);
        while (state.Get().MessageId == messageId && playback.ViewerConnected())
        {
            if (playback.Get().CompletedVersion >= audioVersion) return true;
            if (DateTimeOffset.UtcNow >= deadline)
            {
                logger.LogWarning("Unity playback acknowledgement timed out for version {Version}.", audioVersion);
                return false;
            }
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }
}
