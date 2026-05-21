using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2Headless.DeterminismProbe;

/// <summary>
/// A scripted action sequence for a single encounter, used by both the Q1-side
/// and upstream-side mid-combat capture drivers. Loaded from a
/// <c>goldens-upstream/mid-combat/action-sequences/{id}.json</c> file.
///
/// <para>
/// Schema (wave-45 H8 baked decision):
/// <code>
/// {
///   "encounter": "CultistsNormal",
///   "version": 1,
///   "derivation": "...",
///   "actions": [
///     {"turn": 1, "side": "player", "card_id": "StrikeSilent",
///      "target_creature_id": 1, "end_turn": false},
///     ...
///   ]
/// }
/// </code>
/// </para>
/// </summary>
public sealed class MidCombatActionPlan
{
    [JsonPropertyName("encounter")]
    public string Encounter { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("derivation")]
    public string Derivation { get; set; } = "";

    [JsonPropertyName("actions")]
    public List<MidCombatAction> Actions { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Deserialize from a JSON file path.</summary>
    public static MidCombatActionPlan LoadFromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MidCombatActionPlan>(json, JsonOpts)
            ?? throw new InvalidDataException($"Failed to deserialize MidCombatActionPlan from '{path}'.");
    }

    /// <summary>
    /// Return all actions for <paramref name="turn"/> (1-indexed). Returns an empty
    /// list if no actions are scripted for that turn — the driver will end-turn immediately.
    /// </summary>
    public IReadOnlyList<MidCombatAction> ActionsForTurn(int turn) =>
        Actions.Where(a => a.Turn == turn).ToList();
}

/// <summary>One scripted action within a <see cref="MidCombatActionPlan"/>.</summary>
public sealed class MidCombatAction
{
    [JsonPropertyName("turn")]
    public int Turn { get; set; }

    [JsonPropertyName("side")]
    public string Side { get; set; } = "player";

    [JsonPropertyName("card_id")]
    public string CardId { get; set; } = "";

    /// <summary>
    /// Creature id of the target. Null for non-targeted cards (Defend, buffs).
    /// Positive integer for enemy targets (1 = first enemy in spawn order).
    /// </summary>
    [JsonPropertyName("target_creature_id")]
    public int? TargetCreatureId { get; set; }

    /// <summary>
    /// When true: end the player turn after this action (if a card_id is also set,
    /// play the card first). When false: play the card and continue.
    /// An action with end_turn=true and no card_id is a pure end-turn marker.
    /// </summary>
    [JsonPropertyName("end_turn")]
    public bool EndTurn { get; set; }
}
