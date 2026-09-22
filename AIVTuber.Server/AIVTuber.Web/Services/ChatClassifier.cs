using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

public sealed class ChatClassifier
{
    private static readonly string[] ThinkKeywords =
    {
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

    private static readonly string[] SearchKeywords =
    {
        "최신", "최근", "뉴스", "가격", "시세", "날씨",
        "태어", "언제", "몇", "누구", "멤버", "출시", "발매", "작곡", "작사"
    };

    private static readonly string[] KnowledgeQuestionPatterns =
    {
        "라고 아냐", "인지 아냐", "알아?", "알아봐", "알고 있", "알고있",
        "들어봤", "들어 봤", "본 적 있", "아는 작품", "아는 게임",
        "뭐야", "뭔데", "뭐 하는", "뭘 하는", "무엇이야",
        "어떤 작품", "무슨 작품", "어떤 게임", "무슨 게임",
        "어떤 애니", "무슨 애니", "어떤 영화", "무슨 영화",
        "어떤 만화", "무슨 만화", "어떤 소설", "무슨 소설",
        "어떤 노래", "무슨 노래", "어떤 그룹", "무슨 그룹",
        "어떤 회사", "무슨 회사", "어떤 브랜드", "무슨 브랜드",
        "누구야", "누군데", "어디야", "어디에 있", "어디에 있는",
        "언제 나왔", "언제 생겼", "언제 출시", "언제 발매",
        "뜻이 뭐", "무슨 뜻", "의미가 뭐", "정의가 뭐",
        "설명해", "소개해", "정보 알려", "정보 좀", "에 대해 알려",
        "원작이 뭐", "장르가 뭐", "줄거리가 뭐", "내용이 뭐"
    };

    private static readonly string[] KnowledgeQuestionExceptions =
    {
        "너 나 알아", "나 알아?", "내가 누군지", "내 마음 알아",
        "내 기분 알아", "내 말 알아", "내 말 무슨 뜻", "방금 말한",
        "무슨 말인지 알아", "내가 말한 거", "우리 사이", "나 기억해"
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

        // 작품명/인물명/용어를 묻는 구어체는 짧아도 사실 질문으로 처리한다.
        if (IsKnowledgeQuestion(text))
            return Fact();

        // 명확한 잡담 처리
        if (IsCasual(text))
            return Chat();

        // 분석/추론 요청
        if (ContainsAny(text, ThinkKeywords))
            return Think(ContainsAny(text, SearchKeywords));

        // 사실 확인 질문
        if (ContainsAny(text, FactKeywords))
            return Fact();

        if (ContainsAny(text, SearchKeywords))
            return Fact();

        // 긴 입력은 사고가 필요할 가능성이 높음
        if (text.Length >= 60)
            return Think(ContainsAny(text, SearchKeywords));

        // 일반적인 짧은 질문
        if (text.Contains('?') && ContainsAny(text, SearchKeywords))
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

    private static bool IsKnowledgeQuestion(string text)
    {
        return ContainsAny(text, KnowledgeQuestionPatterns) &&
            !ContainsAny(text, KnowledgeQuestionExceptions);
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
            // The response includes a JSON envelope as well as the spoken text.
            // 70 tokens can stop generation before the closing JSON fields arrive.
            NumPredict: 160,
            Temperature: 0.7,
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
            MaxHistoryMessages: 12,
            NeedsSearch: true
        );
    }

    private static GenerationProfile Think(bool needsSearch)
    {
        return new GenerationProfile(
            ChatMode.Think,
            // qwen3:4b can spend the whole small output budget on hidden thinking
            // and return an empty content field. Prefer a direct answer for live chat.
            Think: false,
            NumPredict: 300,
            Temperature: 0.55,
            MaxHistoryMessages: 20,
            NeedsSearch: needsSearch
        );
    }
}
