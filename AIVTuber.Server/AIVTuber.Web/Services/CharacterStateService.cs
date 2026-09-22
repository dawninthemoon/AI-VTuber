using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

public sealed class CharacterStateService
{
    private readonly object _lock = new();
    private readonly Dictionary<long, byte[]> _audioByVersion = [];

    private CharacterState _state =
        new(
            Text: "",
            Emotion: "neutral",
            Intensity: 0.5f,
            Version: 0
        );

    public CharacterState Get()
    {
        lock (_lock)
        {
            return _state;
        }
    }

    public void SetResponse(
        string text,
        string emotion = "neutral",
        float intensity = 0.5f,
        byte[]? audio = null)
    {
        lock (_lock)
        {
            CharacterState next = new(
                Text: text,
                Emotion: emotion,
                Intensity: intensity,
                Version: _state.Version + 1
            );

            if (audio is { Length: > 0 })
            {
                _audioByVersion[next.Version] = audio;
            }

            _audioByVersion.Remove(next.Version - 2);
            _state = next;
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
                Version: _state.Version + 1
            );
        }
    }
}
