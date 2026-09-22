namespace AIVTuber.Web.Services;

public sealed class CharacterAudioService
{
    private readonly object _lock = new();

    private byte[]? _audio;
    private long _version;

    public void Set(
        byte[] audio,
        long version)
    {
        lock (_lock)
        {
            _audio = audio;
            _version = version;
        }
    }

    public (
        byte[]? Audio,
        long Version
    ) Get()
    {
        lock (_lock)
        {
            return (
                _audio,
                _version
            );
        }
    }
}