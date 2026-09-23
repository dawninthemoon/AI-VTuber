# Slay the Spire bridge

`CommunicationMod` starts this executable as its child process. The bridge reserves standard output for the required `ready` handshake and one game command per line; diagnostics go to standard error.

Run the web server first, then configure CommunicationMod with a published bridge executable, for example:

```properties
command=C\:\\AI-VTuber\\AIVTuber.SpireBridge.exe http://127.0.0.1:5050
```

For local development:

```sh
dotnet run --project AIVTuber.SpireBridge -- http://127.0.0.1:5050
```

The server defaults to observation mode (`Spire:AutoPlay: false`) and returns `WAIT 300`; no cards are played. Set it to `true` only for the current provisional first-playable-card policy. The next implementation phase replaces that policy with a validated LLM decision service.
