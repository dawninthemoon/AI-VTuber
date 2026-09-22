namespace AIVTuber.Web.Models;

public sealed record GenerationProfile(
    ChatMode Mode,
    bool Think,
    int NumPredict,
    double Temperature,
    int MaxHistoryMessages
);