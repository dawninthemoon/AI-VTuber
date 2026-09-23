namespace AIVTuber.Web.Services;

public sealed class SpeechTurnCoordinator
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        _semaphore.WaitAsync(cancellationToken);

    public void Release() => _semaphore.Release();
}
