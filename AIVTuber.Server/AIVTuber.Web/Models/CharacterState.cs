namespace AIVTuber.Web.Models;

public sealed record CharacterState(
    string Text,
    string Emotion,
    float Intensity,
    long Version,
    bool HasAudio,
    long MessageId,
    int SegmentIndex,
    int SegmentCount,
    string SegmentText,
    bool IsIdle,
    string SegmentDisplayText = "",
    string SegmentSpeechText = ""
);
