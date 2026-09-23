namespace AIVTuber.Web.Models;

public sealed record SpireTurnContext(
    SpireStateSnapshot Snapshot,
    IReadOnlyList<SpireCard> Hand,
    IReadOnlyList<SpireMonster> Monsters,
    IReadOnlyList<SpireLegalAction> LegalActions,
    string Prompt
);

public sealed record SpireCard(int Index, string Name, string? Type, int? Cost, bool IsPlayable, bool HasTarget);
public sealed record SpireMonster(int Index, string Name, int? CurrentHp, int? MaxHp, int? Block, string? Intent, int? Damage, int? Hits);

// Id is what an LLM chooses. Command never comes from the model.
public sealed record SpireLegalAction(string Id, string Command, string Description);
