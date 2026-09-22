namespace AIVTuber.Web.Models;

public enum ChatMode
{
    Fast,
    Normal,
    Think
}

public sealed record GenerationProfile(
    ChatMode Mode,
    bool Think,
    int NumPredict,
    double Temperature
);
