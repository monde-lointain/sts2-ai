# Wave 10 / B.2-δ.cards-2.γ — Cards Bucket Batch 2, Alpha Wraithform-Z + Q-residual Port Log
# v0.103.2 → v0.105.1

**Date:** 2026-05-17
**Stream:** B.2-δ.cards-2.γ (one of 3 parallel streams in Wave 10)
**Alpha range:** Pounce through Z (inclusive), plus WraithForm and Wound
**Engineer:** Sonnet 4.6 subagent
**Base SHA:** 81f0039faf8a9fcfb2ce5af4d9d24beaad2e6c02 (Wave 9 close)
**ADR refs:** ADR-026 (pin semantics), ADR-027 (caps-as-floors), Q1-ADR-009 (multiplayer stripped)

## Summary

32 Q1 cards in the Pounce-Z alpha range examined against v0.103.2→v0.105.1 upstream diff.

**One real stat change found and ported:**
- `Untouchable.UpgradeDelta` 2 → 3 (upstream `OnUpgrade` changed `UpgradeValueBy(2m)` → `UpgradeValueBy(3m)`).
  This is the exact card flagged in the Wave 10 task brief as missed by Wave 9 audit.

**All other upstream diffs in this range are threading-only (`PowerCmd.Apply` choiceContext
threading) or structural-only changes with no Q1 analogue** — categorized PORT no-change
or SKIP-NO-Q1 per the Wave 9 methodology established in wave-9-port-log-gamma.md.

**Source edits:** 1 file (`Untouchable.cs` — `UpgradeDelta` 2→3)
**Test edits:** 1 file (`Phase1CardTests.cs` — `Untouchable_canonical` assertion 2→3)
**LOC delta:** source +2 / tests +1 / port-log ~200 lines

## Per-row breakdown

### 1. Pounce.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Pounce.cs` |
| Q1 file | `Content/Cards/Pounce.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<FreeSkillPower>` gains `choiceContext` first arg |

`PowerCmd` is SKIP-WHOLE-SUBTREE (Wave 7). Q1's `Pounce.OnPlay` uses
`ApplyPowerAction` queued into `ExecutionContext.Queue`. No Q1 port action.

---

### 2. PreciseCut.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/PreciseCut.cs` |
| Q1 file | `Content/Cards/PreciseCut.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff between v0.103.2 and v0.105.1.

---

### 3. Predator.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Predator.cs` |
| Q1 file | `Content/Cards/Predator.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<DrawCardsNextTurnPower>` gains `choiceContext` first arg |

Same `PowerCmd` pattern as Pounce. Q1's `Predator` queue-based. No Q1 port action.

---

### 4. Prepared.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Prepared.cs` |
| Q1 file | `Content/Cards/Prepared.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff.

---

### 5. Reflex.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Reflex.cs` |
| Q1 file | `Content/Cards/Reflex.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff.

---

### 6. Regret.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Regret.cs` |
| Q1 file | `Content/Cards/Regret.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `public override OnTurnEndInHand` → `protected override OnTurnEndInHand` |

Access-modifier-only change. Q1's `Regret` has no `OnTurnEndInHand` override
(turn-end HP loss is a known deferred gap). No Q1 port action.

---

### 7. Ricochet.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Ricochet.cs` |
| Q1 file | `Content/Cards/Ricochet.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff.

---

### 8. SerpentForm.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/SerpentForm.cs` |
| Q1 file | `Content/Cards/SerpentForm.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<SerpentFormPower>` gains `choiceContext` first arg |

Same `PowerCmd` pattern. Q1 queue-based. No Q1 port action.

---

### 9. Shadowmeld.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Shadowmeld.cs` |
| Q1 file | `Content/Cards/Shadowmeld.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<ShadowmeldPower>` gains `choiceContext` first arg |

Same `PowerCmd` pattern. No Q1 port action.

---

### 10. ShadowStep.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/ShadowStep.cs` |
| Q1 file | `Content/Cards/ShadowStep.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<ShadowStepPower>` gains `choiceContext` first arg |

Same `PowerCmd` pattern. No Q1 port action.

---

### 11. Shiv.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Shiv.cs` |
| Q1 file | `Content/Cards/Shiv.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `CreateInHand(Player, CombatState)` → `CreateInHand(Player, ICombatState)` signature; `CardPileCmd.AddGeneratedCardsToCombat(shivs, PileType.Hand, addedByPlayer: true)` → `(..., owner)` |

Q1's `Shiv` has no static `CreateInHand` factory method. The `CombatState → ICombatState`
refactor is structural (Godot-only class hierarchy). `CardPileCmd` is SKIP-WHOLE-SUBTREE
(Wave 7). No Q1 port action.

---

### 12. Skewer.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Skewer.cs` |
| Q1 file | `Content/Cards/Skewer.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff.

---

### 13. Slice.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Slice.cs` |
| Q1 file | `Content/Cards/Slice.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff.

---

### 14. Slimed.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Slimed.cs` |
| Q1 file | `Content/Cards/Slimed.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff.

---

### 15. Snakebite.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Snakebite.cs` |
| Q1 file | `Content/Cards/Snakebite.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<PoisonPower>` gains `choiceContext` first arg |

Same `PowerCmd` pattern. No Q1 port action.

---

### 16. Sneaky.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Sneaky.cs` |
| Q1 file | `Content/Cards/Sneaky.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<SneakyPower>` gains `choiceContext` first arg |

Same `PowerCmd` pattern. No Q1 port action.

---

### 17. Speedster.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Speedster.cs` |
| Q1 file | `Content/Cards/Speedster.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<SpeedsterPower>` gains `choiceContext` first arg |

Same `PowerCmd` pattern. No Q1 port action.

---

### 18. StormOfSteel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/StormOfSteel.cs` |
| Q1 file | `Content/Cards/StormOfSteel.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff.

---

### 19. Strangle.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Strangle.cs` |
| Q1 file | `Content/Cards/Strangle.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<StranglePower>` gains `choiceContext` first arg |

Same `PowerCmd` pattern. No Q1 port action.

---

### 20. StrikeSilent.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/StrikeSilent.cs` |
| Q1 file | `Content/Cards/StrikeSilent.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff.

---

### 21. SuckerPunch.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/SuckerPunch.cs` |
| Q1 file | `Content/Cards/SuckerPunch.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<WeakPower>` gains `choiceContext` first arg |

Same `PowerCmd` pattern. No Q1 port action.

---

### 22. Suppress.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Suppress.cs` |
| Q1 file | `Content/Cards/Suppress.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<WeakPower>` gains `choiceContext` first arg |

Same `PowerCmd` pattern. No Q1 port action.

---

### 23. Survivor.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Survivor.cs` |
| Q1 file | `Content/Cards/Survivor.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff.

---

### 24. Tactician.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Tactician.cs` |
| Q1 file | `Content/Cards/Tactician.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff.

---

### 25. TheHunt.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/TheHunt.cs` |
| Q1 file | `Content/Cards/TheHunt.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `attackCommand.Results.Any(r => r.WasTargetKilled)` → `attackCommand.Results.SelectMany(r => r).Any(r => r.WasTargetKilled)`; `PowerCmd.Apply<TheHuntPower>` gains `choiceContext` first arg |

Two changes: (a) `Results` collection structure changed (now `IEnumerable<List<DamageResult>>`
per upstream refactor); (b) `PowerCmd` threading. Q1's `TheHunt.OnPlay` enqueues
`DealDamageAction` only — no kill-reward logic (`CardReward`, `TheHuntPower`) in Phase 1.
Both changes are SKIP-NO-Q1. No Q1 port action.

---

### 26. ToolsOfTheTrade.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/ToolsOfTheTrade.cs` |
| Q1 file | `Content/Cards/ToolsOfTheTrade.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<ToolsOfTheTradePower>` gains `choiceContext` first arg |

Same `PowerCmd` pattern. No Q1 port action.

---

### 27. Tracking.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Tracking.cs` |
| Q1 file | `Content/Cards/Tracking.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | Both `PowerCmd.Apply<TrackingPower>` branches gain `choiceContext` first arg |

Same `PowerCmd` pattern (two call sites). No Q1 port action.

---

### 28. Untouchable.cs ← PORT APPLIED

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Untouchable.cs` |
| Q1 file | `Content/Cards/Untouchable.cs` |
| Status | **PORT applied (real stat change)** |
| Upstream diff | `OnUpgrade: UpgradeValueBy(2m)` → `UpgradeValueBy(3m)` |

**This is the card flagged in the Wave 10 task brief as missed by Wave 9 audit.**

Upstream `OnUpgrade` changed the block upgrade amount from +2 to +3. Q1 tracks this
via the `UpgradeDelta` documentation constant (Q1's `Untouchable` has no `OnUpgrade`
override yet — upgrade routing is Phase 2). The constant was wrong (2); corrected to 3.
Test assertion in `Phase1CardTests.Untouchable_canonical` updated to match.

**Change applied:**
- `Untouchable.cs`: `UpgradeDelta = 2` → `UpgradeDelta = 3`; XML doc `Upgrade: +2` → `+3`
- `Phase1CardTests.cs`: `Assert.Equal(2, Untouchable.UpgradeDelta)` → `Assert.Equal(3, ...)`

---

### 29. UpMySleeve.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/UpMySleeve.cs` |
| Q1 file | `Content/Cards/UpMySleeve.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff.

---

### 30. WellLaidPlans.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/WellLaidPlans.cs` |
| Q1 file | `Content/Cards/WellLaidPlans.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | `PowerCmd.Apply<WellLaidPlansPower>` gains `choiceContext` first arg |

Same `PowerCmd` pattern. No Q1 port action.

---

### 31. Wound.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Wound.cs` |
| Q1 file | `Content/Cards/Wound.cs` |
| Status | **PORT no-change** |
| Upstream diff | None |

No upstream diff.

---

### 32. WraithForm.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/WraithForm.cs` |
| Q1 file | `Content/Cards/WraithForm.cs` |
| Status | **SKIP-NO-Q1** |
| Upstream diff | Both `PowerCmd.Apply` calls (`IntangiblePower`, `WraithFormPower`) gain `choiceContext` first arg |

Same `PowerCmd` pattern (two call sites). Q1's `WraithForm.OnPlay` uses
`ApplyPowerAction` for both effects. No Q1 port action.

---

## Overall totals

| Category | Count | / 32 |
|---|---|---|
| PORT applied (real change) | 1 | 1 / 32 |
| PORT no-change (no upstream diff) | 12 | 12 / 32 |
| SKIP-NO-Q1 (threading/structural) | 19 | 19 / 32 |
| **Total** | **32** | **32 / 32** |

**PORT applied:** Untouchable (UpgradeDelta 2→3)

**PORT no-change (no diff):** PreciseCut, Prepared, Reflex, Ricochet, Skewer, Slice, Slimed,
StormOfSteel, StrikeSilent, Survivor, Tactician, UpMySleeve, Wound

**SKIP-NO-Q1 (PowerCmd threading or structural):** Pounce, Predator, Regret, SerpentForm,
Shadowmeld, ShadowStep, Shiv, Snakebite, Sneaky, Speedster, Strangle, SuckerPunch,
Suppress, TheHunt, ToolsOfTheTrade, Tracking, WellLaidPlans, WraithForm

(Regret and Shiv are structural-only; all others are `PowerCmd.Apply` threading.)

## Analysis

All 18 SKIP-NO-Q1 `PowerCmd` rows follow the same pattern documented in Wave 9 gamma
§Analysis. The `choiceContext` threading is SKIP-NO-Q1 for the same reason established there.

**Regret.OnTurnEndInHand access modifier** (`public → protected`): Q1's `Regret` has
no `OnTurnEndInHand` override; the access-modifier change has zero Q1 relevance.

**Shiv.CreateInHand signature** (`CombatState → ICombatState`; `addedByPlayer: true → owner`):
Q1's `Shiv` has no static factory. This is Godot-internal infrastructure. No Q1 port action.

**TheHunt.Results.SelectMany**: The upstream refactored `AttackCommand.Results` to
`IEnumerable<List<DamageResult>>`. Q1 tracks no combat results on card models — all result
processing happens at `CombatEngine` level. No Q1 port action.

## LOC delta

- Source: +2 lines (`Untouchable.cs` — `UpgradeDelta` value + XML doc update)
- Tests: +1 line (`Phase1CardTests.cs` — assertion update)
- Port-log doc (`wave-10-port-log-gamma.md`): ~230 lines (new file, Wave 10 / 10.γ scope)

## Verification results

- `dotnet build sts2-headless.sln`: 0 warn 0 err
- `BitIdenticalRoundtripTests`: 65 / 65 PASS
- `probe-upstream-initial-state`: 140 PASS / 20 SKIP (no FAIL)

## Deferrals

None. All rows fully resolved.

The `TheHunt` kill-reward logic (`AddExtraReward` + `TheHuntPower`) and `Regret`
`OnTurnEndInHand` HP loss remain Phase-2 deferred gaps — pre-existing, not introduced
by this port wave.
