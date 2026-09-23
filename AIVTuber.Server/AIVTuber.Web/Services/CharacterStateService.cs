using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

public sealed class CharacterStateService
{
    private readonly object _lock = new();
    private readonly Dictionary<long, byte[]> _audioByVersion = [];
    private readonly Dictionary<long, bool> _idleByMessage = [];
    private long _lastMessageId;

    private CharacterState _state =
        new(
            Text: "",
            Emotion: "neutral",
            Intensity: 0.5f,
            Version: 0,
            HasAudio: false,
            MessageId: 0,
            SegmentIndex: 0,
            SegmentCount: 0,
            SegmentText: "",
            IsIdle: false
        );

    public CharacterState Get()
    {
        lock (_lock)
        {
            return _state;
        }
    }

    public long BeginResponse(
        string text,
        string emotion = "neutral",
        float intensity = 0.5f,
        int segmentCount = 1,
        bool isIdle = false)
    {
        lock (_lock)
        {
            long messageId = ++_lastMessageId;
            CharacterState next = new(
                Text: text,
                Emotion: emotion,
                Intensity: intensity,
                Version: _state.Version + 1,
                HasAudio: false,
                MessageId: messageId,
                SegmentIndex: 0,
                SegmentCount: Math.Max(segmentCount, 0),
                SegmentText: "",
                IsIdle: isIdle
            );

            _idleByMessage[messageId] = isIdle;
            _state = next;
            TrimAudioHistory(next.Version);
            return messageId;
        }
    }

    public bool PublishAudioSegment(
        long messageId,
        string text,
        string emotion,
        float intensity,
        int segmentIndex,
        int segmentCount,
        string segmentText,
        byte[] audio,
        out long publishedVersion)
    {
        publishedVersion = 0;
        if (audio.Length == 0)
        {
            return false;
        }

        lock (_lock)
        {
            // A newer response superseded this TTS job.
            if (messageId != _lastMessageId)
            {
                return false;
            }

            CharacterState next = new(
                Text: text,
                Emotion: emotion,
                Intensity: intensity,
                Version: _state.Version + 1,
                HasAudio: true,
                MessageId: messageId,
                SegmentIndex: segmentIndex,
                SegmentCount: segmentCount,
                SegmentText: segmentText,
                IsIdle: _idleByMessage.GetValueOrDefault(messageId)
            );

            publishedVersion = next.Version;
            _audioByVersion[next.Version] = audio;
            _state = next;
            TrimAudioHistory(next.Version);
            return true;
        }
    }

    public byte[]? GetAudio(long version)
    {
        lock (_lock)
        {
            return _audioByVersion.GetValueOrDefault(version);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _audioByVersion.Clear();
            _idleByMessage.Clear();
            _state = new CharacterState(
                Text: "",
                Emotion: "neutral",
                Intensity: 0.5f,
                Version: _state.Version + 1,
                HasAudio: false,
                MessageId: ++_lastMessageId,
                SegmentIndex: 0,
                SegmentCount: 0,
                SegmentText: "",
                IsIdle: false
            );
        }
    }

    private void TrimAudioHistory(long currentVersion)
    {
        // Keep enough completed segments for Unity's polling/downloading queue.
        long oldestVersion = currentVersion - 16;
        foreach (long version in _audioByVersion.Keys.Where(v => v < oldestVersion).ToArray())
        {
            _audioByVersion.Remove(version);
        }

        long oldestMessageId = _lastMessageId - 16;
        foreach (long messageId in _idleByMessage.Keys.Where(id => id < oldestMessageId).ToArray())
        {
            _idleByMessage.Remove(messageId);
        }
    }
}
