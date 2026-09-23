namespace AIVTuber.Web.Models;

public sealed class SpireOptions
{
    // Disabled by default so connecting a real run cannot play cards unexpectedly.
    public bool AutoPlay { get; init; }

    // CommunicationMod's WAIT command uses frames. At 60 FPS this is about 5 seconds.
    public int ObservationWaitFrames { get; init; } = 300;

    // Keeps UI-only choices readable before the game receives a command.
    public int ScreenActionDelayMilliseconds { get; init; } = 750;
}
