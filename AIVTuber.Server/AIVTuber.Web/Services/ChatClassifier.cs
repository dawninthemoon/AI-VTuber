using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

public sealed class ChatClassifier
{
    private static readonly string[] ThinkKeywords =
    {
        "분석",
        "비교",
        "장단점",
        "이유를 설명",
        "자세히 설명",
        "원인을 설명",
        "단계별로",
        "논리적으로",
        "어떻게 생각해",
        "뭐가 더 좋아",
        "왜 그런지"
    };

    private static readonly string[] FastPatterns =
    {
        "안녕",
        "ㅎㅇ",
        "하이",
        "ㅋㅋ",
        "ㅋㅋㅋ",
        "ㅎㅎ",
        "ㅇㅇ",
        "아니",
        "응",
        "어",
        "그래",
        "뭐해",
        "뭐함",
        "잘자",
        "굿나잇"
    };

    public GenerationProfile Classify(string message)
    {
        var text = message.Trim();

        if (ThinkKeywords.Any(keyword =>
            text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return Think();
        }

        if (text.Length >= 100)
        {
            return Think();
        }

        if (text.Count(c => c == '\n') >= 2)
        {
            return Think();
        }

        if (text.Length <= 20 &&
            FastPatterns.Any(pattern =>
                text.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
        {
            return Fast();
        }

        if (text.Length <= 30)
        {
            return Fast();
        }

        return Normal();
    }

    private static GenerationProfile Fast()
    {
        return new GenerationProfile(
            Mode: ChatMode.Fast,
            Think: false,
            NumPredict: 100,
            Temperature: 0.7
        );
    }

    private static GenerationProfile Normal()
    {
        return new GenerationProfile(
            Mode: ChatMode.Normal,
            Think: false,
            NumPredict: 180,
            Temperature: 0.65
        );
    }

    private static GenerationProfile Think()
    {
        return new GenerationProfile(
            Mode: ChatMode.Think,
            Think: true,
            NumPredict: 400,
            Temperature: 0.6
        );
    }
}
