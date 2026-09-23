namespace AIVTuber.Web.Models;

// The bridge writes Command verbatim to CommunicationMod stdout.
public sealed record SpireTurnResponse(
    string Command,
    bool AutoPlay,
    string Reason
);
