# Wave 9 / B.2-δ.cards-1.γ — Cards Bucket Batch 1, Alpha I-Po Port Log
# v0.103.2 → v0.105.1

**Date:** 2026-05-17
**Stream:** B.2-δ.cards-1.γ (one of 3 parallel card streams)
**Alpha range:** I through PoisonedStab (inclusive)
**Engineer:** Sonnet 4.6 subagent
**Base SHA:** 4654701b6e6b328b90011b065afa9396180c6d93 (Wave 8 close)
**ADR refs:** ADR-026 (pin semantics), ADR-027 (caps-as-floors), Q1-ADR-009 (multiplayer stripped)

## Summary

All 18 cards in the I-Po alpha range are either PORT no-change (no upstream diff) or
SKIP-NO-Q1 (upstream diff is entirely in Godot-layer surface with no Q1 analogue).
No source edits made; no test edits made; no LOC delta.

**Dominant upstream change pattern:** Every card with an upstream diff in this range
has the same `PowerCmd.Apply` signature change: `choiceContext` added as new first
argument. `PowerCmd` is in `src/Core/Commands/`, classified as `SKIP-WHOLE-SUBTREE`
in Wave 7 (B.2-β port log §Commands/ row 10). Q1 cards use
`ctx.Queue.Enqueue(new ApplyPowerAction(...))` directly — no `PowerCmd` analogue.

**`Pinpoint` outlier:** Parameter rename `context → choiceContext` in upstream's
`AfterCardPlayed` override — a virtual method on upstream's async `AbstractModel`
hierarchy. Q1's `Pinpoint` has no `AfterCardPlayed` method (Q1 `CardModel` is a
synchronous, queue-based class with no Godot virtual lifecycle hooks).

## Per-row breakdown

### 1. InfiniteBlades.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/InfiniteBlades.cs` |
| Q1 file | `Content/Cards/InfiniteBlades.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<InfiniteBladesPower>(base.Owner.Creature, 1m, base.Owner.Creature, this);
+await PowerCmd.Apply<InfiniteBladesPower>(choiceContext, base.Owner.Creature, 1m, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Reason:** `PowerCmd` is a Godot scene-tree command class (`src/Core/Commands/PowerCmd.cs`),
SKIP-WHOLE-SUBTREE since Wave 7. Q1's `InfiniteBlades.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.InfiniteBlades, 1, null))`. No Q1
port action.

---

### 2. Injury.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Injury.cs` |
| Q1 file | `Content/Cards/Injury.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 3. KnifeTrap.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/KnifeTrap.cs` |
| Q1 file | `Content/Cards/KnifeTrap.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 4. LeadingStrike.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/LeadingStrike.cs` |
| Q1 file | `Content/Cards/LeadingStrike.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 5. LegSweep.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/LegSweep.cs` |
| Q1 file | `Content/Cards/LegSweep.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<WeakPower>(cardPlay.Target, base.DynamicVars.Weak.BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target, base.DynamicVars.Weak.BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Reason:** Same as InfiniteBlades row 1. Q1's `LegSweep.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Weak, BaseWeak, target))`. No Q1
port action.

---

### 6. Malaise.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Malaise.cs` |
| Q1 file | `Content/Cards/Malaise.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<StrengthPower>(cardPlay.Target, -powerAmount, base.Owner.Creature, this);
-await PowerCmd.Apply<WeakPower>(cardPlay.Target, powerAmount, base.Owner.Creature, this);
+await PowerCmd.Apply<StrengthPower>(choiceContext, cardPlay.Target, -powerAmount, base.Owner.Creature, this);
+await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target, powerAmount, base.Owner.Creature, this);
```
Add `choiceContext` first arg to two `PowerCmd.Apply` calls.

**Reason:** Same as InfiniteBlades row 1. Q1's `Malaise.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(...))` for both effects. No Q1 port action.

---

### 7. MasterPlanner.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/MasterPlanner.cs` |
| Q1 file | `Content/Cards/MasterPlanner.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<MasterPlannerPower>(base.Owner.Creature, 1m, base.Owner.Creature, this);
+await PowerCmd.Apply<MasterPlannerPower>(choiceContext, base.Owner.Creature, 1m, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Reason:** Same as InfiniteBlades row 1. Q1's `MasterPlanner.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.MasterPlanner, 1, null))`. No Q1
port action.

---

### 8. MementoMori.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/MementoMori.cs` |
| Q1 file | `Content/Cards/MementoMori.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 9. Mirage.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Mirage.cs` |
| Q1 file | `Content/Cards/Mirage.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 10. Murder.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Murder.cs` |
| Q1 file | `Content/Cards/Murder.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 11. Neutralize.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Neutralize.cs` |
| Q1 file | `Content/Cards/Neutralize.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<WeakPower>(cardPlay.Target, base.DynamicVars.Weak.BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target, base.DynamicVars.Weak.BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Reason:** Same as InfiniteBlades row 1. Q1's `Neutralize.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Weak, BaseWeak, target))`. No Q1
port action.

---

### 12. Nightmare.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Nightmare.cs` |
| Q1 file | `Content/Cards/Nightmare.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-(await PowerCmd.Apply<NightmarePower>(base.Owner.Creature, 3m, base.Owner.Creature, this)).SetSelectedCard(selectedCard);
+(await PowerCmd.Apply<NightmarePower>(choiceContext, base.Owner.Creature, 3m, base.Owner.Creature, this)).SetSelectedCard(selectedCard);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Reason:** Same as InfiniteBlades row 1. Q1's `Nightmare.OnPlay` is a smoke stub
(card-selection effect; combat-state dependent) with no `PowerCmd.Apply` call. No Q1
port action.

---

### 13. NoxiousFumes.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/NoxiousFumes.cs` |
| Q1 file | `Content/Cards/NoxiousFumes.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<NoxiousFumesPower>(base.Owner.Creature, base.DynamicVars["PoisonPerTurn"].BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<NoxiousFumesPower>(choiceContext, base.Owner.Creature, base.DynamicVars["PoisonPerTurn"].BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Reason:** Same as InfiniteBlades row 1. Q1's `NoxiousFumes.OnPlay` is a hook-only
smoke stub. No Q1 port action.

---

### 14. Outbreak.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Outbreak.cs` |
| Q1 file | `Content/Cards/Outbreak.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<OutbreakPower>(base.Owner.Creature, base.DynamicVars["OutbreakPower"].BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<OutbreakPower>(choiceContext, base.Owner.Creature, base.DynamicVars["OutbreakPower"].BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Reason:** Same as InfiniteBlades row 1. Q1's `Outbreak.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Outbreak, BaseAmount, null))`. No Q1
port action.

---

### 15. PhantomBlades.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/PhantomBlades.cs` |
| Q1 file | `Content/Cards/PhantomBlades.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<PhantomBladesPower>(base.Owner.Creature, base.DynamicVars["PhantomBladesPower"].BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<PhantomBladesPower>(choiceContext, base.Owner.Creature, base.DynamicVars["PhantomBladesPower"].BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Reason:** Same as InfiniteBlades row 1. Q1's `PhantomBlades.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.PhantomBlades, BaseAmount, null))`.
No Q1 port action.

---

### 16. PiercingWail.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/PiercingWail.cs` |
| Q1 file | `Content/Cards/PiercingWail.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<PiercingWailPower>(hittableEnemy, base.DynamicVars["StrengthLoss"].BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<PiercingWailPower>(choiceContext, hittableEnemy, base.DynamicVars["StrengthLoss"].BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply` (inside foreach loop over enemies).

**Reason:** Same as InfiniteBlades row 1. Q1's `PiercingWail.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Strength, -BaseStrengthLoss, target))`.
Note: Q1 currently applies to a single `target` rather than iterating all enemies —
this is a pre-existing approximation in Q1's Phase 1 headless model
(`AllEnemies` target routing is handled at the `CombatEngine` level, not per-card).
No change needed from the v0.105.1 diff specifically. No Q1 port action.

---

### 17. Pinpoint.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Pinpoint.cs` |
| Q1 file | `Content/Cards/Pinpoint.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
+public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
```
Parameter rename `context → choiceContext` in the `AfterCardPlayed` virtual override.

**Reason:** Q1's `Pinpoint` has no `AfterCardPlayed` method. `AfterCardPlayed` is a
virtual hook on upstream's async `AbstractModel` hierarchy — a Godot lifecycle hook
fired after any card is played in the scene-tree combat loop. Q1's `CardModel` is a
synchronous, queue-based class with no Godot virtual lifecycle hooks. This is a
pure cosmetic rename with no behavioral change even in the Godot codebase. No Q1
port action.

---

### 18. PoisonedStab.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/PoisonedStab.cs` |
| Q1 file | `Content/Cards/PoisonedStab.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<PoisonPower>(cardPlay.Target, base.DynamicVars.Poison.BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<PoisonPower>(choiceContext, cardPlay.Target, base.DynamicVars.Poison.BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Reason:** Same as InfiniteBlades row 1. Q1's `PoisonedStab.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Poison, BasePoison, target))`. No Q1
port action.

---

## Overall totals

| Category | Count | / 18 |
|---|---|---|
| PORT no-change | 6 | 6 / 18 |
| SKIP-NO-Q1 | 12 | 12 / 18 |
| STUB | 0 | 0 / 18 |
| **Total** | **18** | **18 / 18** |

**No-change cards:** Injury, KnifeTrap, LeadingStrike, MementoMori, Mirage, Murder
**SKIP-NO-Q1 cards:** InfiniteBlades, LegSweep, Malaise, MasterPlanner, Neutralize,
Nightmare, NoxiousFumes, Outbreak, PhantomBlades, PiercingWail, Pinpoint, PoisonedStab

## Analysis of the `PowerCmd.Apply` choiceContext change

All 11 SKIP-NO-Q1 rows (except Pinpoint) are instances of the same v0.105.1 change:
`PowerCmd.Apply` gained `PlayerChoiceContext` as a new first parameter. The Wave 7
port log (B.2-β §Commands/ row 10) already classified `PowerCmd.cs` as SKIP-NO-Q1
and documented why Q1 uses `CombatContext.ApplyPower` instead. This wave confirms
that classification propagates uniformly to all card consumers of `PowerCmd.Apply`.

The `choiceContext` arg was added to give `PowerCmd.Apply` access to the current
player's choice context, enabling downstream behavior (e.g. `FindExistingInstanceForStacking`
described in Wave 7 §Commands/ row 10). Q1's `CombatContext.ApplyPower` already has
access to full combat state via the `ICombatContext` interface; no signature change is
needed.

## Deferred / future-wave notes

None. All rows are fully resolved. The `PowerCmd.Apply` pattern change does not require
any Q1 behavioral adaptation.

## Verification results

No source files were modified. This wave delivers a doc-only commit.

- `dotnet build sts2-headless.sln`: 0 warn 0 err (no source change — verified pre-commit)
- `BitIdenticalRoundtripTests 65/65`: no source change — expected unchanged
- `probe-upstream-initial-state 140 PASS / 20 SKIP`: no source change — expected unchanged
- `DllSignatureGate PASS`: no source change — expected unchanged

## LOC delta

- Source: 0 lines changed (no source edits)
- Tests: 0 lines changed (no test edits)
- Port-log doc (`wave-9-port-log-gamma.md`): ~215 lines (new file, Wave 9 / 9.γ scope)
