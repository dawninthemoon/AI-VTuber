using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

public sealed class CharacterStateService
{
    private readonly object _lock = new();
    private readonly Dictionary<long, byte[]> _audioByVersion = [];
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
            SegmentCount: 0
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
        int segmentCount = 1)
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
                SegmentCount: Math.Max(segmentCount, 0)
            );

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
        byte[] audio)
    {
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
                SegmentCount: segmentCount
            );

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
            _state = new CharacterState(
                Text: "",
                Emotion: "neutral",
                Intensity: 0.5f,
                Version: _state.Version + 1,
                HasAudio: false,
                MessageId: ++_lastMessageId,
                SegmentIndex: 0,
                SegmentCount: 0
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
    }
}
