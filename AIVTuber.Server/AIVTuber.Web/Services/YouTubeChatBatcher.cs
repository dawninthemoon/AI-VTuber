using System.Threading.Channels;
using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

// Groups messages by arrival time, including messages buffered while speech is playing.
internal sealed class YouTubeChatBatcher(
    ChannelReader<YouTubeChatMessage> reader,
    TimeSpan quietPeriod,
    TimeSpan maxPeriod,
    int maxMessages)
{
    private YouTubeChatMessage? _pending;
    private List<YouTubeChatMessage>? _inFlight;

    public YouTubeChatMessage? Pending => _pending;
    public IReadOnlyList<YouTubeChatMessage>? InFlight => _inFlight;

    public async Task<IReadOnlyList<YouTubeChatMessage>?> ReadAsync(CancellationToken cancellationToken)
    {
        YouTubeChatMessage first;
        if (_pending is { } pending)
        {
            first = pending;
            _pending = null;
        }
        else
        {
            try { first = await reader.ReadAsync(cancellationToken); }
            catch (ChannelClosedException) { return null; }
        }

        var batch = new List<YouTubeChatMessage>(maxMessages) { first };
        _inFlight = batch;
        DateTimeOffset startedAt = ReceivedAt(first);
        DateTimeOffset lastAt = startedAt;
        while (batch.Count < maxMessages)
        {
            DateTimeOffset deadline = Min(startedAt + maxPeriod, lastAt + quietPeriod);
            YouTubeChatMessage next;
            if (reader.TryRead(out var queued))
            {
                next = queued;
            }
            else
            {
                TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(remaining);
                try { next = await reader.ReadAsync(timeout.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { break; }
                catch (ChannelClosedException) { break; }
            }

            DateTimeOffset nextAt = ReceivedAt(next);
            if (nextAt > deadline)
            {
                _pending = next;
                break;
            }
            batch.Add(next);
            lastAt = nextAt > lastAt ? nextAt : lastAt;
        }
        _inFlight = null;
        return batch;
    }

    private static DateTimeOffset ReceivedAt(YouTubeChatMessage message) =>
        message.ReceivedAt == default ? DateTimeOffset.UtcNow : message.ReceivedAt;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
