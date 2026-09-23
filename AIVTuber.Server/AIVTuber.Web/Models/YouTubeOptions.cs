namespace AIVTuber.Web.Models;

public sealed class YouTubeOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string VideoId { get; set; } = "";
    public string IgnoreChannelId { get; set; } = "";
    public string TriggerPrefix { get; set; } = "";
    public int MaxMessageLength { get; set; } = 200;
    public int QueueCapacity { get; set; } = 20;
}
