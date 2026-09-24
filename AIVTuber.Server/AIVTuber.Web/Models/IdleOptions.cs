namespace AIVTuber.Web.Models;

public sealed class IdleOptions
{
    public bool Enabled { get; set; } = true;
    public int MinimumSilenceSeconds { get; set; } = 45;
    public int MaximumSilenceSeconds { get; set; } = 90;
    public int ContinuationPauseSeconds { get; set; } = 2;
}
