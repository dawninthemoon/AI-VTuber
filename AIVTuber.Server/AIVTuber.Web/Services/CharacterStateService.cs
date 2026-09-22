using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

public sealed class CharacterStateService
{
    private readonly object _lock = new();
    private byte[]? _audio;

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
            _audio = audio;
            _state = new CharacterState(
                Text: text,
                Emotion: emotion,
                Intensity: intensity,
                Version: _state.Version + 1
            );
        }
    }

    public byte[]? GetAudio(long version)
    {
        lock (_lock)
        {
            return _state.Version == version ? _audio : null;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _audio = null;
            _state = new CharacterState(
                Text: "",
                Emotion: "neutral",
                Intensity: 0.5f,
                Version: _state.Version + 1
            );
        }
    }
}
