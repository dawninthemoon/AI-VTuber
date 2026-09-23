using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

const string defaultServerUrl = "http://127.0.0.1:5050";
string serverUrl = args.Length > 0 ? args[0] : defaultServerUrl;
serverUrl = serverUrl.TrimEnd('/');

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(serverUrl + "/"),
    Timeout = TimeSpan.FromSeconds(20)
};

// CommunicationMod requires this exact handshake on stdout. Do not write logs to stdout.
Console.Out.WriteLine("ready");
Console.Out.Flush();
Console.Error.WriteLine($"Spire bridge ready. Server: {serverUrl}");

while (await Console.In.ReadLineAsync() is { } stateLine)
{
    if (string.IsNullOrWhiteSpace(stateLine))
    {
        continue;
    }

    string command = "WAIT 300";

    try
    {
        using JsonDocument state = JsonDocument.Parse(stateLine);
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "spire/turn",
            state.RootElement);

        response.EnsureSuccessStatusCode();
        SpireTurnResponse? decision = await response.Content.ReadFromJsonAsync<SpireTurnResponse>();

        if (decision != null && IsValidCommunicationModCommand(decision.Command))
        {
            command = decision.Command.Trim();
            Console.Error.WriteLine($"Game state accepted. Command: {command}. Reason: {decision.Reason}");
        }
        else
        {
            Console.Error.WriteLine("Server returned no valid CommunicationMod command; waiting.");
        }
    }
    catch (Exception exception)
    {
        // A temporarily unavailable server must not cause protocol garbage on stdout.
        Console.Error.WriteLine($"Turn forwarding failed: {exception.Message}");
    }

    Console.Out.WriteLine(command);
    Console.Out.Flush();
}

static bool IsValidCommunicationModCommand(string? command)
{
    if (string.IsNullOrWhiteSpace(command) || command.Contains('\n') || command.Contains('\r'))
    {
        return false;
    }

    return Regex.IsMatch(
        command.Trim(),
        "^(START|POTION|PLAY|END|CHOOSE|PROCEED|RETURN|KEY|CLICK|WAIT|STATE)(?:\\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

internal sealed record SpireTurnResponse(
    string Command,
    bool AutoPlay,
    string Reason
);
