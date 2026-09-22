using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

public sealed class ChatClassifier
{
    private static readonly string[] ThinkKeywords =
    {
        "왜",
        "이유",
        "분석",
        "비교",
        "설계",
        "어떻게 해야",
        "장단점",
        "차이",
        "설명해줘",
        "정리해줘",
        "추천해줘",
        "원인",
        "해결 방법"
    };

    private static readonly string[] FactKeywords =
    {
        "누구",
        "누가",
        "언제",
        "어디",
        "몇",
        "몇 명",
        "몇 년",
        "무슨",
        "이름",
        "멤버",
        "작곡",
        "작사",
        "출시",
        "발매",
        "국적",
        "수도",
        "데뷔",
        "출생",
        "사망",
        "뜻",
        "정의"
    };

    private static readonly string[] CasualKeywords =
    {
        "안녕",
        "ㅎㅇ",
        "하이",
        "ㅋㅋ",
        "ㅎㅎ",
        "뭐해",
        "뭐함",
        "심심해",
        "놀자",
        "잘자",
        "굿밤",
        "배고파",
        "졸려"
    };

    public GenerationProfile Classify(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Chat();

        string text = message
            .Trim()
            .ToLowerInvariant();

        // 명확한 잡담은 가장 먼저 처리
        if (IsCasual(text))
            return Chat();

        // 분석/추론 요청
        if (ContainsAny(text, ThinkKeywords))
            return Think();

        // 사실 확인 질문
        if (ContainsAny(text, FactKeywords))
            return Fact();

        // 긴 입력은 사고가 필요할 가능성이 높음
        if (text.Length >= 60)
            return Think();

        // 일반적인 짧은 질문
        if (text.Contains('?'))
            return Fact();

        return Chat();
    }

    private static bool IsCasual(string text)
    {
        if (text.Length > 15)
            return false;

        return ContainsAny(
            text,
            CasualKeywords
        );
    }

    private static bool ContainsAny(
        string text,
        string[] keywords)
    {
        return keywords.Any(
            text.Contains
        );
    }

    private static GenerationProfile Chat()
    {
        return new GenerationProfile(
            ChatMode.Chat,
            Think: false,
            NumPredict: 80,
            Temperature: 0.8,
            MaxHistoryMessages: 8
        );
    }

    private static GenerationProfile Fact()
    {
        return new GenerationProfile(
            ChatMode.Fact,
            Think: false,
            NumPredict: 120,
            Temperature: 0.2,
            MaxHistoryMessages: 12
        );
    }

    private static GenerationProfile Think()
    {
        return new GenerationProfile(
            ChatMode.Think,
            Think: true,
            NumPredict: 300,
            Temperature: 0.55,
            MaxHistoryMessages: 20
        );
    }
}