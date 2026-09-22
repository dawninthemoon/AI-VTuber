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
        "누구", "언제", "몇", "어디", "무슨", "태어", "출시", "발매",
        "작곡", "작사", "뜻", "정의", "알려", "설명"
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
        string query = question.Trim();
        foreach (string cutoff in QueryCutoffs)
        {
            int index = query.IndexOf(cutoff, StringComparison.OrdinalIgnoreCase);
            if (index > 1)
            {
                query = query[..index];
                break;
            }
        }

        query = query.TrimEnd(' ', '?', '!', '.', ',');
        if (query.Length > 3 && (query.EndsWith('은') || query.EndsWith('는')))
        {
            query = query[..^1];
        }
        return query.Length >= 2 ? query : question.Trim();
    }
}
