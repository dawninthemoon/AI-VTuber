namespace AIVTuber.Web.Models;

public sealed class SupertonicOptions
{
    public string ModelDirectory { get; init; } =
        "Models/sherpa-onnx-supertonic-3-tts-int8-2026-05-11";

    public int SpeakerId { get; init; }

    public int NumSteps { get; init; } = 8;

    public int NumThreads { get; init; } = 2;

    public float Speed { get; init; } = 1.0f;
}
