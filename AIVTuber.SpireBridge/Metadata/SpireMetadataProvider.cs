using System.Text.Json;
using System.Text.RegularExpressions;
using AIVTuber.SpireBridge.Models.Metadata;

namespace AIVTuber.SpireBridge.Metadata;

/// <summary>
/// Read-only metadata lookup shared by MCTS and heuristic evaluation. Call Initialize once at startup.
/// </summary>
public static class SpireMetadataProvider
{
    private static readonly object Sync = new();
    private static readonly Regex NumberRegex = new(@"\b(\d+)\b", RegexOptions.Compiled);
    private static readonly Regex DamageRegex = new(@"(?:deal|deals)\s+(\d+)\s+damage", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BlockRegex = new(@"gain\s+(\d+)\s+block", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static Dictionary<string, CardMetadata> _cards = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, RelicMetadata> _relics = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, PotionMetadata> _potions = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, PowerMetadata> _powers = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    public static IReadOnlyDictionary<string, CardMetadata> Cards => _cards;
    public static IReadOnlyDictionary<string, RelicMetadata> Relics => _relics;
    public static IReadOnlyDictionary<string, PotionMetadata> Potions => _potions;
    public static IReadOnlyDictionary<string, PowerMetadata> Powers => _powers;

    public static void Initialize(Action<string>? log = null)
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Metadata");
            _cards = LoadCards(root, log);
            _relics = LoadEffects(root, ["relics.json", "reliclist.json"], (id, description) => new RelicMetadata(id, id, description, ExtractMagic(description)), log);
            _potions = LoadEffects(root, ["potions.json", "potionlist.json"], (id, description) => new PotionMetadata(id, id, description, ExtractMagic(description)), log);
            _powers = LoadEffects(root, ["powers.json", "powerlist.json"], (id, description) => new PowerMetadata(id, id, description, ExtractMagic(description)), log);
            _initialized = true;
            log?.Invoke($"Spire metadata loaded: cards={_cards.Count}, relics={_relics.Count}, potions={_potions.Count}, powers={_powers.Count}.");
        }
    }

    public static CardMetadata GetCard(string cardId)
    {
        EnsureInitialized();
        return !string.IsNullOrWhiteSpace(cardId) && _cards.TryGetValue(cardId, out CardMetadata? card)
            ? card
            : CardMetadata.Unknown(cardId);
    }

    public static RelicMetadata GetRelic(string relicId) => GetEffect(relicId, _relics, RelicMetadata.Unknown);
    public static PotionMetadata GetPotion(string potionId) => GetEffect(potionId, _potions, PotionMetadata.Unknown);
    public static PowerMetadata GetPower(string powerId) => GetEffect(powerId, _powers, PowerMetadata.Unknown);

    public static int GetDamageValue(string cardId, int upgradeCount)
    {
        CardMetadata card = GetUpgradedCard(cardId, upgradeCount);
        return card.Damage;
    }

    public static int GetBlockValue(string cardId, int upgradeCount)
    {
        CardMetadata card = GetUpgradedCard(cardId, upgradeCount);
        return card.Block;
    }

    public static string GetFormattedString(string cardId)
    {
        CardMetadata card = GetCard(cardId);
        return $"[{card.Name}] (Cost: {card.Cost}, Dmg: {card.Damage}, Blk: {card.Block}) - {card.Description}";
    }

    private static CardMetadata GetUpgradedCard(string cardId, int upgradeCount)
    {
        CardMetadata baseCard = GetCard(cardId);
        if (upgradeCount <= 0 || baseCard.Description.Length == 0)
        {
            return baseCard;
        }

        string normalizedId = cardId.TrimEnd('+');
        if (_cards.TryGetValue(normalizedId + "+", out CardMetadata? upgraded))
        {
            return upgraded;
        }

        return baseCard;
    }

    private static Dictionary<string, CardMetadata> LoadCards(string root, Action<string>? log)
    {
        var result = new Dictionary<string, CardMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach ((string id, string description) in ReadEntries(root, ["cards.json", "cardlist.json"], log))
        {
            try
            {
                CardMetadata card = new(
                    id,
                    id,
                    InferCost(id, description),
                    InferType(id, description),
                    InferTarget(description),
                    Extract(DamageRegex, description),
                    Extract(BlockRegex, description),
                    ExtractMagic(description),
                    description);
                result[id] = card;
            }
            catch (Exception exception)
            {
                log?.Invoke($"Skipped card metadata '{id}': {exception.Message}");
            }
        }
        return result;
    }

    private static Dictionary<string, T> LoadEffects<T>(string root, string[] fileNames, Func<string, string, T> factory, Action<string>? log)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach ((string id, string description) in ReadEntries(root, fileNames, log))
        {
            try { result[id] = factory(id, description); }
            catch (Exception exception) { log?.Invoke($"Skipped metadata '{id}': {exception.Message}"); }
        }
        return result;
    }

    private static IEnumerable<(string Id, string Description)> ReadEntries(string root, string[] fileNames, Action<string>? log)
    {
        string? path = fileNames.Select(name => Path.Combine(root, name)).FirstOrDefault(File.Exists);
        if (path == null)
        {
            log?.Invoke($"Metadata file missing: {string.Join(" or ", fileNames)}");
            yield break;
        }

        JsonDocument? document = null;
        try { document = JsonDocument.Parse(File.ReadAllText(path)); }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            log?.Invoke($"Metadata file ignored ({Path.GetFileName(path)}): {exception.Message}");
            yield break;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                log?.Invoke($"Metadata file ignored ({Path.GetFileName(path)}): expected a JSON object.");
                yield break;
            }
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    yield return (property.Name, property.Value.GetString() ?? string.Empty);
                }
            }
        }
    }

    private static T GetEffect<T>(string id, IReadOnlyDictionary<string, T> source, Func<string?, T> fallback)
    {
        EnsureInitialized();
        return !string.IsNullOrWhiteSpace(id) && source.TryGetValue(id, out T? value) ? value : fallback(id);
    }

    private static void EnsureInitialized()
    {
        if (!_initialized) Initialize();
    }

    private static int Extract(Regex regex, string description)
    {
        Match match = regex.Match(description);
        return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : 0;
    }

    private static int ExtractMagic(string description)
    {
        Match match = NumberRegex.Match(description);
        return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : 0;
    }

    private static CardTarget InferTarget(string description)
    {
        if (description.Contains("ALL enemies", StringComparison.OrdinalIgnoreCase)) return CardTarget.AllEnemies;
        if (description.Contains("enemy", StringComparison.OrdinalIgnoreCase) || description.Contains("damage", StringComparison.OrdinalIgnoreCase)) return CardTarget.Enemy;
        if (description.Contains("Gain", StringComparison.OrdinalIgnoreCase) || description.Contains("Draw", StringComparison.OrdinalIgnoreCase)) return CardTarget.Self;
        return CardTarget.None;
    }

    private static CardType InferType(string id, string description)
    {
        if (id.Contains("Curse", StringComparison.OrdinalIgnoreCase) || id is "Injury" or "Wound" or "Dazed" or "Burn") return CardType.Curse;
        if (id is "Strike" or "Strike_R" or "Strike_G" or "Strike_B" or "Strike_P" || description.Contains("Deal", StringComparison.OrdinalIgnoreCase)) return CardType.Attack;
        if (description.Contains("At the end of your turn", StringComparison.OrdinalIgnoreCase) || description.Contains("Whenever", StringComparison.OrdinalIgnoreCase)) return CardType.Power;
        return CardType.Skill;
    }

    private static int InferCost(string id, string description)
    {
        if (id is "Zap" or "Miracle" or "Anger" or "Finesse" or "Flash of Steel") return 0;
        if (description.Contains("X Energy", StringComparison.OrdinalIgnoreCase)) return -1;
        return 1;
    }
}
