namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Per-combat player configuration: relic roster, starting deck, HP envelope,
/// energy and hand-draw baselines. Defaults match Silent A0 upstream values.
/// </summary>
public sealed record PlayerSpec(
    IReadOnlyList<string> RelicIds,
    IReadOnlyList<CardInstance> Deck,
    int InitialHp = CombatEngine.BaseMaxHpSilent,
    int MaxHp = CombatEngine.BaseMaxHpSilent,
    int BaseEnergyPerTurn = CombatEngine.BaseEnergyPerTurnSilent,
    int BaseHandDrawCount = CombatEngine.BaseHandDrawCount
);
