namespace AIVTuber.Web.Models;

public sealed record SearchHit(string Title, string Url, string Excerpt);

public sealed record SearchEvidence(IReadOnlyList<SearchHit> Hits, bool IsCurrentInfo = false)
{
    public bool HasResults => Hits.Count > 0;
}
