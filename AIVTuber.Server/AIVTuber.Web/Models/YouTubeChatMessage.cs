namespace AIVTuber.Web.Models;

public sealed record YouTubeChatMessage(
    string Id,
    string AuthorName,
    string AuthorChannelId,
    string Text);
