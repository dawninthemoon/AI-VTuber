namespace AIVTuber.Web.Models;

public sealed record ChatResponse(string Response, IReadOnlyList<SearchHit>? Sources = null);
