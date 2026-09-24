using System.Globalization;
using System.Text;
using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

public sealed class CharacterStateService
{
    private readonly object _lock = new();
    private readonly Dictionary<long, byte[]> _audioByVersion = [];
    private readonly Dictionary<long, bool> _idleByMessage = [];
    private readonly Dictionary<long, List<SpokenSegment>> _segmentsByMessage = [];
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
            _segmentsByMessage[messageId] = [];
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
        out long publishedVersion,
        string? displayText = null,
        string? speechText = null)
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
                IsIdle: _idleByMessage.GetValueOrDefault(messageId),
                SegmentDisplayText: displayText ?? segmentText,
                SegmentSpeechText: speechText ?? segmentText
            );

            publishedVersion = next.Version;
            _audioByVersion[next.Version] = audio;
            _segmentsByMessage[messageId].Add(
                new SpokenSegment(next.Version, segmentIndex, speechText ?? segmentText));
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

    // Playback progress is a best-effort estimate between Unity reports.
    public string GetHeardText(long messageId, PlaybackStatus playback)
    {
        lock (_lock)
        {
            if (!_segmentsByMessage.TryGetValue(messageId, out List<SpokenSegment>? segments))
                return "";

            StringBuilder heard = new();
            foreach (SpokenSegment segment in segments.OrderBy(item => item.Index))
            {
                string part;
                if (segment.Version <= playback.CompletedVersion)
                {
                    part = segment.Text;
                }
                else if (segment.Version == playback.PlayingVersion && playback.ClipSeconds > 0)
                {
                    double ratio = Math.Clamp(playback.PlayedSeconds / playback.ClipSeconds, 0, 1);
                    var text = new StringInfo(segment.Text);
                    int count = (int)Math.Floor(text.LengthInTextElements * ratio);
                    part = count > 0 ? text.SubstringByTextElements(0, count) : "";
                }
                else
                {
                    break;
                }

                if (part.Length > 0)
                {
                    if (heard.Length > 0) heard.Append(' ');
                    heard.Append(part);
                }
                if (segment.Version > playback.CompletedVersion) break;
            }
            return heard.ToString().Trim();
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _audioByVersion.Clear();
            _idleByMessage.Clear();
            _segmentsByMessage.Clear();
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
            _segmentsByMessage.Remove(messageId);
        }
    }

    private sealed record SpokenSegment(long Version, int Index, string Text);
}
