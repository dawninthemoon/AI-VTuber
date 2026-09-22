using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

public sealed class WikipediaSearchService
{
    private static readonly Regex HtmlTags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly string[] QueryCutoffs =
    [
        "라고 알고 있", "라고 알고있", "라고 아냐", "인지 아냐",
        "에 대해 알고 있", "에 대해서 알고 있", "에 대해 알려", "에 대해서 알려",
        "어떤 작품", "무슨 작품", "어떤 게임", "무슨 게임",
        "어떤 애니", "무슨 애니", "어떤 영화", "무슨 영화",
        "어떤 만화", "무슨 만화", "어떤 소설", "무슨 소설",
        "어떤 노래", "무슨 노래", "어떤 그룹", "무슨 그룹",
        "원작이 뭐", "장르가 뭐", "줄거리가 뭐", "내용이 뭐",
        "뜻이 뭐", "무슨 뜻", "의미가 뭐", "정의가 뭐",
        "알고 있", "알고있", "알아", "들어봤", "들어 봤",
        "뭐야", "뭔데", "뭐 하는", "누구", "언제", "몇", "어디", "무슨",
        "태어", "출시", "발매", "작곡", "작사", "뜻", "정의", "알려", "설명", "소개"
    ];
    private static readonly string[] LeadingFillers =
    [
        "혹시 ", "근데 ", "그런데 ", "그럼 ", "저기 ", "야 ", "너 "
    ];
    private static readonly string[] KoreanSuffixes =
    [
        "에 대해서", "에 대해", "이라고", "라는", "이란", "라고",
        "은", "는", "이", "가", "을", "를", "도"
    ];
    private static readonly string[] CurrentInfoKeywords =
    [
        "최신", "최근", "오늘", "지금", "현재", "올해", "뉴스", "가격", "시세"
    ];

    private readonly HttpClient _httpClient;

    public WikipediaSearchService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://ko.wikipedia.org/");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AIVTuber/0.1 (local development)");
        _httpClient.Timeout = TimeSpan.FromSeconds(4);
    }

    public async Task<SearchEvidence> SearchAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        if (CurrentInfoKeywords.Any(question.Contains))
        {
            // Wikipedia search snippets cannot verify live or time-sensitive facts.
            return new SearchEvidence([], IsCurrentInfo: true);
        }

        string query = BuildQuery(question);
        string path = $"w/rest.php/v1/search/page?q={Uri.EscapeDataString(query)}&limit=3";

        using HttpResponseMessage response = await _httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("pages", out JsonElement pages))
        {
            return new SearchEvidence([]);
        }

        List<SearchHit> hits = [];
        foreach (JsonElement page in pages.EnumerateArray())
        {
            string title = page.GetProperty("title").GetString() ?? "";
            string key = page.GetProperty("key").GetString() ?? "";
            string description = page.TryGetProperty("description", out JsonElement descriptionElement)
                ? descriptionElement.GetString() ?? ""
                : "";
            string excerpt = page.TryGetProperty("excerpt", out JsonElement excerptElement)
                ? excerptElement.GetString() ?? ""
                : "";

            string cleaned = WebUtility.HtmlDecode(HtmlTags.Replace(excerpt, ""));
            if (cleaned.Length > 350)
            {
                cleaned = cleaned[..350];
            }

            hits.Add(new SearchHit(
                title,
                $"https://ko.wikipedia.org/wiki/{Uri.EscapeDataString(key)}",
                $"{description}. {cleaned}".Trim()));
        }

        return new SearchEvidence(hits);
    }

    private static string BuildQuery(string question)
    {
        string query = question.Trim().Trim('"', '\'', '“', '”', '‘', '’');

        bool removedFiller;
        do
        {
            removedFiller = false;
            foreach (string filler in LeadingFillers)
            {
                if (query.StartsWith(filler, StringComparison.OrdinalIgnoreCase))
                {
                    query = query[filler.Length..].TrimStart();
                    removedFiller = true;
                    break;
                }
            }
        }
        while (removedFiller);

        foreach (string cutoff in QueryCutoffs)
        {
            int index = query.IndexOf(cutoff, StringComparison.OrdinalIgnoreCase);
            if (index > 1)
            {
                query = query[..index];
                break;
            }
        }

        query = query.Trim().TrimEnd(' ', '?', '!', '.', ',', '"', '\'', '“', '”', '‘', '’');

        bool removedSuffix;
        do
        {
            removedSuffix = false;
            foreach (string suffix in KoreanSuffixes)
            {
                if (query.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                    query.Length > suffix.Length + 1)
                {
                    query = query[..^suffix.Length].TrimEnd();
                    removedSuffix = true;
                    break;
                }
            }
        }
        while (removedSuffix);

        return query.Length >= 2 ? query : question.Trim();
    }
}
