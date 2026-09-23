namespace AIVTuber.Web.Services;

// Separate, bounded observation history. Evicting history never removes queued work.
public sealed class YouTubeChatStatus
{
    private readonly object _gate = new();
    private readonly LinkedList<YouTubeChatEntry> _recent = new();
    private readonly Dictionary<string, LinkedListNode<YouTubeChatEntry>> _byId = new();
    private readonly Dictionary<string, long> _events = new();
    private string _connection = "disabled";
    private string? _reason;
    private DateTimeOffset? _retryAt;
    private long _received, _streamAttempts, _videoLookups;

    public void Connection(string state, string? reason = null, TimeSpan? retryAfter = null)
    {
        lock (_gate)
        {
            _connection = state;
            _reason = reason;
            _retryAt = retryAfter is { } delay ? DateTimeOffset.UtcNow + delay : null;
        }
    }

    public void Request(bool stream)
    {
        lock (_gate)
        {
            if (stream) _streamAttempts++; else _videoLookups++;
        }
    }

    public void Received(string id, string author, string text)
    {
        lock (_gate)
        {
            _received++;
            if (_byId.ContainsKey(id)) return;
            var node = _recent.AddLast(new YouTubeChatEntry(id, author, text, DateTimeOffset.UtcNow, "received", null));
            _byId.Add(id, node);
            if (_recent.Count > 500)
            {
                _byId.Remove(_recent.First!.Value.Id);
                _recent.RemoveFirst();
            }
        }
    }

    public void Message(string id, string state, string? reason = null)
    {
        lock (_gate)
        {
            _events[state] = _events.GetValueOrDefault(state) + 1;
            if (_byId.TryGetValue(id, out var node))
                node.Value = node.Value with { Status = state, Reason = reason };
        }
    }

    public object Snapshot()
    {
        lock (_gate)
        {
            return new
            {
                connection = _connection, reason = _reason, retryAt = _retryAt,
                received = _received, streamAttempts = _streamAttempts, videoLookups = _videoLookups,
                events = new Dictionary<string, long>(_events),
                recent = _recent.Reverse().ToArray()
            };
        }
    }
}

public sealed record YouTubeChatEntry(string Id, string Author, string Text,
    DateTimeOffset ReceivedAt, string Status, string? Reason);
