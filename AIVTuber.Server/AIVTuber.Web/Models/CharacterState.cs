namespace AIVTuber.Web.Models;

public sealed record CharacterState(
    string Text,
    string Emotion,
    float Intensity,
    long Version,
    bool HasAudio
);
