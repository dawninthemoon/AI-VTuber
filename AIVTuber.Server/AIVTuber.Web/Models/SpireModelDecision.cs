namespace AIVTuber.Web.Models;

public sealed record SpireModelDecision(
    string ActionId,
    string Speech,
    string Emotion,
    float Intensity
);
