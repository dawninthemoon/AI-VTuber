using System.Text.Json;
using AIVTuber.Web.Models;
using Microsoft.Extensions.Options;

namespace AIVTuber.Web.Services;

public sealed class SpireTurnService
{
    private readonly SpireOptions _options;
    private readonly SpireContextBuilder _contextBuilder;
    private readonly OpenAiSpireDecisionService _openAiDecisionService;
    private readonly SpireSpeechService _speechService;
    private readonly object _lock = new();
    private SpireTurnContext? _lastContext;
    private JsonElement? _lastRawState;

    public SpireTurnService(
        IOptions<SpireOptions> options,
        SpireContextBuilder contextBuilder,
        OpenAiSpireDecisionService openAiDecisionService,
        SpireSpeechService speechService)
    {
        _options = options.Value;
        _contextBuilder = contextBuilder;
        _openAiDecisionService = openAiDecisionService;
        _speechService = speechService;
    }

    public async Task<SpireTurnResponse> DecideAsync(
        JsonElement state,
        CancellationToken cancellationToken)
    {
        SpireTurnContext context = _contextBuilder.Build(state);

        lock (_lock)
        {
            _lastContext = context;
            _lastRawState = state.Clone();
        }

        if (!_options.AutoPlay)
        {
            int frames = Math.Max(_options.ObservationWaitFrames, 1);
            return new SpireTurnResponse(
                $"WAIT {frames}",
                false,
                "Observation mode is enabled.");
        }

        if (!context.Snapshot.ReadyForCommand)
        {
            return new SpireTurnResponse("WAIT 1", true, "Game is not ready.");
        }

        SpireModelDecision? modelDecision = await _openAiDecisionService.DecideAsync(
            context,
            cancellationToken);
        if (modelDecision != null)
        {
            SpireLegalAction? selected = context.LegalActions.FirstOrDefault(action =>
                action.Id == modelDecision.ActionId);
            if (selected != null)
            {
                await DelayForScreenActionAsync(selected.Command, cancellationToken);
                _speechService.Publish(modelDecision);
                return new SpireTurnResponse(selected.Command, true, "Validated OpenAI decision.");
            }
        }

        SpireLegalAction? firstPlay = context.LegalActions.FirstOrDefault(action =>
            action.Command.StartsWith("PLAY ", StringComparison.Ordinal));
        if (firstPlay != null)
        {
            return new SpireTurnResponse(firstPlay.Command, true, "First validated playable action.");
        }

        SpireLegalAction? endTurn = context.LegalActions.FirstOrDefault(action =>
            action.Command == "END");
        if (endTurn != null)
        {
            return new SpireTurnResponse(endTurn.Command, true, "No playable card remains.");
        }

        SpireLegalAction? firstChoice = SelectFallbackChoice(context);
        if (firstChoice != null)
        {
            await DelayForScreenActionAsync(firstChoice.Command, cancellationToken);
            return new SpireTurnResponse(firstChoice.Command, true, "First validated screen choice.");
        }

        SpireLegalAction? proceed = context.LegalActions.FirstOrDefault(action =>
            action.Command == "PROCEED");
        if (proceed != null)
        {
            await DelayForScreenActionAsync(proceed.Command, cancellationToken);
            return new SpireTurnResponse(proceed.Command, true, "Validated proceed action.");
        }

        return new SpireTurnResponse("WAIT 1", true, "No safe generic action exists.");
    }

    public SpireStateSnapshot? GetLastState()
    {
        lock (_lock)
        {
            return _lastContext?.Snapshot;
        }
    }

    public SpireTurnContext? GetLastContext()
    {
        lock (_lock)
        {
            return _lastContext;
        }
    }

    public JsonElement? GetLastRawState()
    {
        lock (_lock)
        {
            return _lastRawState;
        }
    }

    private async Task DelayForScreenActionAsync(string command, CancellationToken cancellationToken)
    {
        bool isScreenAction = command.StartsWith("CHOOSE ", StringComparison.Ordinal) ||
            command == "PROCEED" || command == "RETURN";
        int delay = Math.Max(_options.ScreenActionDelayMilliseconds, 0);
        if (isScreenAction && delay > 0)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static SpireLegalAction? SelectFallbackChoice(SpireTurnContext context)
    {
        IEnumerable<SpireLegalAction> choices = context.LegalActions.Where(action =>
            action.Command.StartsWith("CHOOSE ", StringComparison.Ordinal));

        // Never waste a required hand-selection action on an unremovable Ascender's Bane.
        SpireLegalAction? removable = choices.FirstOrDefault(action =>
            !action.Description.Contains("cannot be removed", StringComparison.OrdinalIgnoreCase) &&
            !action.Description.Contains("제거할 수 없", StringComparison.Ordinal));

        return removable ?? choices.FirstOrDefault();
    }
}
