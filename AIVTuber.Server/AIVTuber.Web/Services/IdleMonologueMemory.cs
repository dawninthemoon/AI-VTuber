using System.Text;

namespace AIVTuber.Web.Services;

internal sealed class IdleMonologueMemory
{
    private static readonly string[] Topics =
    [
        "게임: 게임 요소, 플레이 스타일, 해보고 싶은 도전. 실제 플레이 경험은 꾸미지 않는다.",
        "만화/애니메이션: 장르, 연출, 인물 관계, 보고 싶은 분위기. 작품 내용을 지어내지 않는다.",
        "음악(락/J-POP): 악기, 밴드 사운드, 감정, 듣는 상황. 가사를 인용하거나 지어내지 않는다.",
        "디저트: 맛, 식감, 조합, 만들어 보고 싶은 실험. 먹어봤다는 경험은 꾸미지 않는다."
    ];
    private static readonly string[] Angles =
    [
        "의외로 중요한 작은 디테일 하나를 골라 이유를 풀어보기",
        "서로 다른 두 취향을 비교하고 네 취향을 설명하기",
        "조건 하나를 바꾼 상상 속 상황으로 발전시키기",
        "흔한 선택과 정반대 선택의 재미를 생각해보기",
        "초보자 입장에서 궁금할 지점을 혼자 생각해보기",
        "장점 하나와 아쉬운 점 하나를 함께 살펴보기",
        "구체적인 조합이나 아이디어를 하나 제안하고 다듬기",
        "분위기나 감각을 새 비유로 설명하기"
    ];
    private readonly Queue<string> _recent = new();
    private readonly Queue<int> _angles = new();
    private int _topic = -1;
    private int _turn;

    public string[] Recent => _recent.ToArray();

    public string NextDirection()
    {
        if (_topic < 0 || _turn >= 3)
        {
            _topic = _topic < 0 ? Random.Shared.Next(Topics.Length) : (_topic + Random.Shared.Next(1, Topics.Length)) % Topics.Length;
            _turn = 0;
        }
        if (_angles.Count == 0)
        {
            int[] choices = Enumerable.Range(0, Angles.Length).ToArray();
            Random.Shared.Shuffle(choices);
            foreach (int choice in choices) _angles.Enqueue(choice);
        }
        return $"주제: {Topics[_topic]}\n이번 전개: {Angles[_angles.Dequeue()]}\n" +
            (_turn == 0 ? "새 화제로 자연스럽게 넘어간다. 최근에 쓴 소재는 피한다." :
                "직전 혼잣말의 생각을 이어가되 새로운 예시나 관점을 덧붙인다. 앞말을 요약하거나 다시 소개하지 않는다.");
    }

    public void Remember(string text)
    {
        _recent.Enqueue(text);
        while (_recent.Count > 24) _recent.Dequeue();
        _turn++;
    }

    public bool IsRepetitive(string text)
    {
        string normalized = Normalize(text);
        if (normalized.Length == 0) return true;
        var grams = Grams(normalized);
        return _recent.Any(previous =>
        {
            string other = Normalize(previous);
            if (other == normalized) return true;
            var prior = Grams(other);
            int intersection = grams.Count(prior.Contains);
            int union = grams.Count + prior.Count - intersection;
            return union > 0 && (double)intersection / union >= 0.65;
        });
    }

    private static string Normalize(string text) => new(text.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static HashSet<string> Grams(string text) => Enumerable.Range(0, Math.Max(0, text.Length - 2))
        .Select(i => text.Substring(i, 3)).ToHashSet(StringComparer.Ordinal);
}
