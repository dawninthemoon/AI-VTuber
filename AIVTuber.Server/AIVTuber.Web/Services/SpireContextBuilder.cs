using System.Text;
using System.Text.Json;
using AIVTuber.Web.Models;

namespace AIVTuber.Web.Services;

public sealed class SpireContextBuilder
{
    public SpireTurnContext Build(JsonElement state)
    {
        TryGetProperty(state, "game_state", out JsonElement gameState);
        TryGetProperty(gameState, "combat_state", out JsonElement combatState);
        TryGetProperty(gameState, "screen_state", out JsonElement screenState);
        TryGetProperty(combatState, "player", out JsonElement player);
        SpireStateSnapshot snapshot = new(DateTimeOffset.UtcNow, GetString(gameState, "screen_name"), GetInt(gameState, "floor"), GetInt(gameState, "current_hp"), GetInt(gameState, "max_hp"), GetInt(player, "energy"), GetBoolean(state, "ready_for_command"));
        List<SpireCard> hand = ReadHand(combatState);
        List<SpireMonster> monsters = ReadMonsters(combatState);
        List<SpireLegalAction> legalActions = BuildLegalActions(state, gameState, screenState, hand, monsters);
        return new SpireTurnContext(snapshot, hand, monsters, legalActions, BuildPrompt(snapshot, hand, monsters, legalActions));
    }

    private static List<SpireCard> ReadHand(JsonElement combatState)
    {
        var cards = new List<SpireCard>();
        if (!TryGetProperty(combatState, "hand", out JsonElement hand) || hand.ValueKind != JsonValueKind.Array) return cards;
        int index = 0;
        foreach (JsonElement card in hand.EnumerateArray())
        {
            index++;
            cards.Add(new SpireCard(index, GetString(card, "name") ?? "Unknown", GetString(card, "type"), GetInt(card, "cost"), GetBoolean(card, "is_playable"), GetBoolean(card, "has_target")));
        }
        return cards;
    }

    private static List<SpireMonster> ReadMonsters(JsonElement combatState)
    {
        var monsters = new List<SpireMonster>();
        if (!TryGetProperty(combatState, "monsters", out JsonElement source) || source.ValueKind != JsonValueKind.Array) return monsters;
        int index = 0;
        foreach (JsonElement monster in source.EnumerateArray())
        {
            if (!GetBoolean(monster, "is_gone") && !GetBoolean(monster, "half_dead"))
            {
                monsters.Add(new SpireMonster(index, GetString(monster, "name") ?? "Unknown", GetInt(monster, "current_hp"), GetInt(monster, "max_hp"), GetInt(monster, "block"), GetString(monster, "intent"), GetInt(monster, "move_adjusted_damage") ?? GetInt(monster, "move_base_damage"), GetInt(monster, "move_hits")));
            }
            index++;
        }
        return monsters;
    }

    private static List<SpireLegalAction> BuildLegalActions(JsonElement state, JsonElement gameState, JsonElement screenState, IReadOnlyList<SpireCard> hand, IReadOnlyList<SpireMonster> monsters)
    {
        var actions = new List<SpireLegalAction>();
        if (HasAvailableCommand(state, "play"))
        {
            foreach (SpireCard card in hand.Where(card => card.IsPlayable))
            {
                if (card.HasTarget)
                {
                    foreach (SpireMonster monster in monsters) actions.Add(new SpireLegalAction($"play_{card.Index}_target_{monster.Index}", $"PLAY {card.Index} {monster.Index}", $"Play {card.Name} on {monster.Name}."));
                }
                else actions.Add(new SpireLegalAction($"play_{card.Index}", $"PLAY {card.Index}", $"Play {card.Name}."));
            }
        }
        if (HasAvailableCommand(state, "choose"))
        {
            bool isMapScreen = GetString(gameState, "screen_name") == "MAP" ||
                GetString(gameState, "screen_type") == "MAP";
            List<string> choices = isMapScreen
                ? ReadMapChoices(screenState).ToList()
                : ReadChoices(screenState).ToList();
            int index = 0;
            foreach (string choice in choices)
            {
                // CommunicationMod CHOOSE indices are zero-based, like monster targets.
                actions.Add(new SpireLegalAction($"choose_{index}", $"CHOOSE {index}", $"Choose option #{index + 1}: {choice}"));
                index++;
            }
        }
        if (HasAvailableCommand(state, "end")) actions.Add(new SpireLegalAction("end_turn", "END", "End the current turn."));
        if (HasAvailableCommand(state, "proceed")) actions.Add(new SpireLegalAction("proceed", "PROCEED", "Proceed on the current screen."));
        // CommunicationMod CJK exposes the blue confirm button as `confirm` on GRID screens.
        // PROCEED is the protocol command that activates the same button.
        if (HasAvailableCommand(state, "confirm")) actions.Add(new SpireLegalAction("confirm", "PROCEED", "Confirm the current card selection."));
        if (HasAvailableCommand(state, "return")) actions.Add(new SpireLegalAction("return", "RETURN", "Return, skip, or leave the current screen."));
        return actions;
    }

    private static IEnumerable<string> ReadMapChoices(JsonElement screenState)
    {
        if (!TryGetProperty(screenState, "next_nodes", out JsonElement nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement node in nodes.EnumerateArray())
        {
            string symbol = GetString(node, "symbol") ?? "?";
            int? x = GetInt(node, "x");
            int? y = GetInt(node, "y");
            yield return $"{DescribeMapSymbol(symbol)} node at ({x}, {y})";
        }
    }

    private static string DescribeMapSymbol(string symbol)
    {
        return symbol switch
        {
            "M" => "Monster",
            "E" => "Elite",
            "R" => "Rest site",
            "$" => "Shop",
            "?" => "Unknown event",
            "T" => "Treasure",
            "B" => "Boss",
            _ => symbol
        };
    }

    private static IEnumerable<string> ReadChoices(JsonElement screenState)
    {
        // CommunicationMod/CJK uses different collections by screen type.
        foreach (string property in new[] { "options", "choices", "rest_options", "cards", "relics", "potions", "hand" })
        {
            if (!TryGetProperty(screenState, property, out JsonElement items) || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement item in items.EnumerateArray())
            {
                string? label = item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : BuildChoiceLabel(item);

                yield return string.IsNullOrWhiteSpace(label) ? "Unknown option" : label;
            }

            yield break;
        }
    }

    private static string? BuildChoiceLabel(JsonElement item)
    {
        string? name = GetString(item, "name") ?? GetString(item, "text") ?? GetString(item, "id");
        string? description = GetString(item, "description");
        return string.IsNullOrWhiteSpace(description) ? name : $"{name}: {description.ReplaceLineEndings(" ")}";
    }

    private static string BuildPrompt(SpireStateSnapshot snapshot, IReadOnlyList<SpireCard> hand, IReadOnlyList<SpireMonster> monsters, IReadOnlyList<SpireLegalAction> legalActions)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("You are a sharp-tongued Korean VTuber playing Slay the Spire.");
        prompt.AppendLine("Choose exactly one legal action ID. Never invent actions, cards, targets, or game facts.");
        prompt.AppendLine($"Screen={snapshot.Screen}; floor={snapshot.Floor}; HP={snapshot.CurrentHp}/{snapshot.MaxHp}; energy={snapshot.Energy}.");
        prompt.AppendLine("Hand:");
        foreach (SpireCard card in hand) prompt.AppendLine($"- #{card.Index} {card.Name} cost={card.Cost} type={card.Type} playable={card.IsPlayable} target={card.HasTarget}");
        prompt.AppendLine("Enemies:");
        foreach (SpireMonster monster in monsters) prompt.AppendLine($"- #{monster.Index} {monster.Name} HP={monster.CurrentHp}/{monster.MaxHp} block={monster.Block} intent={monster.Intent} damage={monster.Damage} hits={monster.Hits}");
        prompt.AppendLine("Legal actions:");
        foreach (SpireLegalAction action in legalActions) prompt.AppendLine($"- {action.Id}: {action.Description}");
        return prompt.ToString().TrimEnd();
    }

    private static bool HasAvailableCommand(JsonElement state, string command) => TryGetProperty(state, "available_commands", out JsonElement commands) && commands.ValueKind == JsonValueKind.Array && commands.EnumerateArray().Any(item => string.Equals(item.GetString(), command, StringComparison.OrdinalIgnoreCase));
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) { value = default; return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value); }
    private static bool GetBoolean(JsonElement element, string name) => TryGetProperty(element, name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    private static int? GetInt(JsonElement element, string name) => TryGetProperty(element, name, out JsonElement value) && value.TryGetInt32(out int result) ? result : null;
    private static string? GetString(JsonElement element, string name) => TryGetProperty(element, name, out JsonElement value) ? value.GetString() : null;
}
