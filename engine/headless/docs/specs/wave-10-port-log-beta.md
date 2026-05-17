# Wave 10 / 10.β — Port Log: Cards St–Wr + U-range

Upstream delta: v0.103.2 → v0.105.1
Stream: 10.β (B.2-δ.cards-2.β)
Q1 cards audited: 14

## Per-card audit

| Card | Upstream diff? | Category | Q1 action |
|---|---|---|---|
| StormOfSteel | None | PORT no-change | No upstream diff; Q1 stub correct |
| Strangle | `PowerCmd.Apply(choiceContext, ...)` only | PORT no-change | Threading plumbing; Q1 uses `ApplyPowerAction` |
| StrikeSilent | None | PORT no-change | No upstream diff; Q1 stub correct |
| SuckerPunch | `PowerCmd.Apply(choiceContext, ...)` only | PORT no-change | Threading plumbing; Q1 uses `ApplyPowerAction` |
| Suppress | `PowerCmd.Apply(choiceContext, ...)` only | PORT no-change | Threading plumbing; Q1 uses `ApplyPowerAction` |
| Survivor | None | PORT no-change | No upstream diff; Q1 stub correct |
| TheHunt | `PowerCmd.Apply(choiceContext, ...)` + `SelectMany` on `Results` | PORT no-change | Threading + Godot `AttackCommand.Results` type restructure; Q1 uses `DealDamageAction` |
| ToolsOfTheTrade | `PowerCmd.Apply(choiceContext, ...)` only | PORT no-change | Threading plumbing; Q1 uses `ApplyPowerAction` |
| Tracking | `PowerCmd.Apply(choiceContext, ...)` only | PORT no-change | Threading plumbing; Q1 uses `ApplyPowerAction` |
| **Untouchable** | `UpgradeValueBy(2m → 3m)` | **PORT applied** | `UpgradeDelta = 2 → 3`; test updated |
| UpMySleeve | None | PORT no-change | No upstream diff; Q1 stub correct |
| WellLaidPlans | `PowerCmd.Apply(choiceContext, ...)` only | PORT no-change | Threading plumbing; Q1 ignores retain in OnPlay |
| Wound | None | PORT no-change | No upstream diff; Q1 stub correct |
| WraithForm | `PowerCmd.Apply(choiceContext, ...)` only | PORT no-change | Threading plumbing; Q1 uses `ApplyPowerAction` |

## Summary

- PORT applied (real change): **1** — Untouchable (`UpgradeDelta` 2 → 3, Block upgrade +2 → +3)
- PORT no-change (threading/no-diff): **13**
- SKIP-NO-Q1: **0**
- Total Q1 cards in range: **14**

## Notes

**TheHunt `SelectMany` change:** Upstream restructured `AttackCommand.Results` from
`IEnumerable<DamageResult>` to `IEnumerable<List<DamageResult>>` (multi-target result
grouping). Change is Godot-engine plumbing; Q1 models TheHunt as `DealDamageAction`
with no kill-detection branching. No Q1 port required.

**Plumbing pattern ignored (13 cards):** `PowerCmd.Apply(choiceContext, target, amount, owner, card)`
— `choiceContext` threading parameter added in v0.105.1 Godot-side. Q1 uses
`ApplyPowerAction` action-queue model; this parameter is irrelevant.
