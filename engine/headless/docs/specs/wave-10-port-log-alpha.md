# Wave 10 / B.2-δ.cards-2.α — Cards Bucket Batch 2, Alpha Po-So Port Log
# v0.103.2 → v0.105.1

**Date:** 2026-05-17
**Stream:** B.2-δ.cards-2.α (one of 3 parallel card streams, Wave 10)
**Alpha range:** Pounce through Survivor (inclusive, 23 Q1 cards)
**Engineer:** Sonnet 4.6 subagent
**Base SHA:** 81f0039faf8a9fcfb2ce5af4d9d24beaad2e6c02 (Wave 9 close)
**ADR refs:** ADR-026 (pin semantics), ADR-027 (caps-as-floors), Q1-ADR-009 (multiplayer stripped)

## Summary

All 23 cards in the Po-So alpha range examined line-by-line per the corrective
Wave-10 methodology (explicit stat-block comparison per card, not pattern-matching).

**Result: 0 PORT applied, 10 PORT no-change, 13 SKIP-NO-Q1, 0 STUB.**

No source files modified; no test files modified.

**Upstream diff patterns in this range:**

1. **`PowerCmd.Apply<T>(choiceContext, ...)` threading (11 cards):** Same refactor
   classified in Wave 7 (B.2-β §Commands/ row 10). Q1 cards use
   `ctx.Queue.Enqueue(new ApplyPowerAction(...))` — no `PowerCmd` analogue. No Q1
   port action.

2. **`CombatState → ICombatState` interface refactor (Shiv):** Two `CreateInHand`
   static helpers changed parameter type from `CombatState` to `ICombatState` and
   `CardPileCmd.AddGeneratedCardsToCombat` signature changed. Q1's `Shiv` has no
   `CreateInHand` static helpers (those depend on Godot scene-tree card spawning).
   No Q1 port action.

3. **`public override → protected override` visibility on `OnTurnEndInHand` (Regret):**
   Pure visibility modifier change on a Godot async virtual. Q1's `Regret` is a
   minimal Curse stub with no `OnTurnEndInHand` method. No Q1 port action.

4. **No diff (10 cards):** PreciseCut, Prepared, Reflex, Ricochet, Skewer, Slice,
   Slimed, StormOfSteel, StrikeSilent, Survivor — unchanged between versions.

**Explicit stat audit result:** Every `UpgradeValueBy(Nm)`, `BaseDamage`, `BaseBlock`,
and `BaseValue` constant was compared between v0.103.2 and v0.105.1 for every card in
range. Zero numeric changes found.

---

## Per-row breakdown

### 1. Pounce.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Pounce.cs` |
| Q1 file | `Content/Cards/Pounce.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<FreeSkillPower>(base.Owner.Creature, 1m, base.Owner.Creature, this);
+await PowerCmd.Apply<FreeSkillPower>(choiceContext, base.Owner.Creature, 1m, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Stat audit:** `UpgradeValueBy(6m)` — identical in both versions. Q1 `UpgradeDelta = 6` correct.

**Reason:** `PowerCmd` is a Godot scene-tree command class, SKIP-WHOLE-SUBTREE since
Wave 7. Q1's `Pounce.OnPlay` uses `ctx.Queue.Enqueue(new DealDamageAction(...))`.
No Q1 port action.

---

### 2. PreciseCut.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/PreciseCut.cs` |
| Q1 file | `Content/Cards/PreciseCut.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 3. Predator.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Predator.cs` |
| Q1 file | `Content/Cards/Predator.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<DrawCardsNextTurnPower>(base.Owner.Creature, 2m, base.Owner.Creature, this);
+await PowerCmd.Apply<DrawCardsNextTurnPower>(choiceContext, base.Owner.Creature, 2m, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Stat audit:** `UpgradeValueBy(5m)` — identical in both versions. Q1 `UpgradeDelta = 5` correct.

**Reason:** Same as Pounce. Q1's `Predator.OnPlay` uses `ctx.Queue.Enqueue(...)`. No Q1 port action.

---

### 4. Prepared.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Prepared.cs` |
| Q1 file | `Content/Cards/Prepared.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 5. Reflex.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Reflex.cs` |
| Q1 file | `Content/Cards/Reflex.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 6. Regret.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Regret.cs` |
| Q1 file | `Content/Cards/Regret.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-public override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
+protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
```
Visibility change: `public override` → `protected override` on `OnTurnEndInHand`.

**Stat audit:** `OnTurnEndInHand` body uses `CardsInHand` field — no numeric constant changes.

**Reason:** `OnTurnEndInHand` is a Godot async virtual lifecycle hook fired at turn end.
Q1's `Regret` is a minimal Curse stub — no `OnTurnEndInHand` method exists. The
visibility change is a pure refactor with no behavioral impact even upstream. No Q1
port action.

---

### 7. Ricochet.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Ricochet.cs` |
| Q1 file | `Content/Cards/Ricochet.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 8. SerpentForm.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/SerpentForm.cs` |
| Q1 file | `Content/Cards/SerpentForm.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<SerpentFormPower>(base.Owner.Creature, base.DynamicVars["SerpentFormPower"].BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<SerpentFormPower>(choiceContext, base.Owner.Creature, base.DynamicVars["SerpentFormPower"].BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Stat audit:** `UpgradeValueBy(2m)` — identical in both versions. Q1 `UpgradeDelta = 2` correct.

**Reason:** Same as Pounce. Q1's `SerpentForm.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.SerpentForm, BaseAmount, null))`. No Q1 port action.

---

### 9. Shadowmeld.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Shadowmeld.cs` |
| Q1 file | `Content/Cards/Shadowmeld.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<ShadowmeldPower>(base.Owner.Creature, base.DynamicVars["Power"].BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<ShadowmeldPower>(choiceContext, base.Owner.Creature, base.DynamicVars["Power"].BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Stat audit:** No `UpgradeValueBy` in Shadowmeld (no upgrade). No numeric changes.

**Reason:** Same as Pounce. Q1's `Shadowmeld.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Shadowmeld, BaseAmount, null))`. No Q1 port action.

---

### 10. ShadowStep.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/ShadowStep.cs` |
| Q1 file | `Content/Cards/ShadowStep.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<ShadowStepPower>(base.Owner.Creature, 1m, base.Owner.Creature, this);
+await PowerCmd.Apply<ShadowStepPower>(choiceContext, base.Owner.Creature, 1m, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Stat audit:** No `UpgradeValueBy` (ShadowStep upgrade discards all cards; no power
upgrade delta). Constant value `1m` unchanged.

**Reason:** Same as Pounce. Q1's `ShadowStep.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.ShadowStep, 1, null))`. No Q1 port action.

---

### 11. Shiv.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Shiv.cs` |
| Q1 file | `Content/Cards/Shiv.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-public static async Task<CardModel?> CreateInHand(Player owner, CombatState combatState)
+public static async Task<CardModel?> CreateInHand(Player owner, ICombatState combatState)
-public static async Task<IEnumerable<CardModel>> CreateInHand(Player owner, int count, CombatState combatState)
+public static async Task<IEnumerable<CardModel>> CreateInHand(Player owner, int count, ICombatState combatState)
-await CardPileCmd.AddGeneratedCardsToCombat(shivs, PileType.Hand, addedByPlayer: true);
+await CardPileCmd.AddGeneratedCardsToCombat(shivs, PileType.Hand, owner);
```
`CombatState` → `ICombatState` interface refactor on two `CreateInHand` static helpers;
`CardPileCmd.AddGeneratedCardsToCombat` signature change.

**Stat audit:** `UpgradeValueBy(2m)` — identical in both versions. Q1 `Shiv.Damage = 4` correct.

**Reason:** `CreateInHand` helpers depend on Godot scene-tree card spawning
(`CardPileCmd`, `CombatState`). Q1's `Shiv` has no `CreateInHand` — Shivs are
generated via `ctx.Queue.Enqueue(new CreateCardInHandAction(...))` in callers
(e.g., `Accuracy`, `AThousandCuts` power handlers). The interface refactor has no
Q1 substrate impact. No Q1 port action.

---

### 12. Skewer.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Skewer.cs` |
| Q1 file | `Content/Cards/Skewer.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 13. Slice.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Slice.cs` |
| Q1 file | `Content/Cards/Slice.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 14. Slimed.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Slimed.cs` |
| Q1 file | `Content/Cards/Slimed.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 15. Snakebite.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Snakebite.cs` |
| Q1 file | `Content/Cards/Snakebite.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<PoisonPower>(cardPlay.Target, base.DynamicVars.Poison.BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<PoisonPower>(choiceContext, cardPlay.Target, base.DynamicVars.Poison.BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Stat audit:** `UpgradeValueBy(3m)` — identical in both versions. Q1 `UpgradeDelta = 3` correct.

**Reason:** Same as Pounce. Q1's `Snakebite.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Poison, BasePoison, target))`. No Q1 port action.

---

### 16. Sneaky.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Sneaky.cs` |
| Q1 file | `Content/Cards/Sneaky.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<SneakyPower>(base.Owner.Creature, base.DynamicVars["SneakyPower"].BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<SneakyPower>(choiceContext, base.Owner.Creature, base.DynamicVars["SneakyPower"].BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Stat audit:** `UpgradeValueBy(1m)` — identical in both versions. Q1 `UpgradeDelta = 1` correct.

**Reason:** Same as Pounce. Q1's `Sneaky.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Sneaky, BaseAmount, null))`. No Q1 port action.

---

### 17. Speedster.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Speedster.cs` |
| Q1 file | `Content/Cards/Speedster.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<SpeedsterPower>(base.Owner.Creature, base.DynamicVars["SpeedsterPower"].IntValue, base.Owner.Creature, this);
+await PowerCmd.Apply<SpeedsterPower>(choiceContext, base.Owner.Creature, base.DynamicVars["SpeedsterPower"].IntValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Stat audit:** No `UpgradeValueBy` in Speedster `OnUpgrade` (upgrade reduces cost to 0).
`IntValue` constant unchanged.

**Reason:** Same as Pounce. Q1's `Speedster.OnPlay` uses
`ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Speedster, BaseAmount, null))`. No Q1 port action.

---

### 18. StormOfSteel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/StormOfSteel.cs` |
| Q1 file | `Content/Cards/StormOfSteel.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 19. Strangle.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Strangle.cs` |
| Q1 file | `Content/Cards/Strangle.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<StranglePower>(cardPlay.Target, base.DynamicVars["StranglePower"].BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<StranglePower>(choiceContext, cardPlay.Target, base.DynamicVars["StranglePower"].BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Stat audit:** `UpgradeValueBy(2m)` (damage), `UpgradeValueBy(1m)` (StranglePower) —
both identical in both versions. Q1 upgrade deltas correct.

**Reason:** Same as Pounce. Q1's `Strangle.OnPlay` uses `ctx.Queue.Enqueue(...)` for
both attack and power. No Q1 port action.

---

### 20. StrikeSilent.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/StrikeSilent.cs` |
| Q1 file | `Content/Cards/StrikeSilent.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

### 21. SuckerPunch.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/SuckerPunch.cs` |
| Q1 file | `Content/Cards/SuckerPunch.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<WeakPower>(cardPlay.Target, base.DynamicVars.Weak.BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target, base.DynamicVars.Weak.BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Stat audit:** `UpgradeValueBy(2m)` (damage), `UpgradeValueBy(1m)` (Weak) —
both identical in both versions. Q1 upgrade deltas correct.

**Reason:** Same as Pounce. Q1's `SuckerPunch.OnPlay` uses `ctx.Queue.Enqueue(...)` for
both attack and power. No Q1 port action.

---

### 22. Suppress.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Suppress.cs` |
| Q1 file | `Content/Cards/Suppress.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Upstream diff summary:**
```diff
-await PowerCmd.Apply<WeakPower>(cardPlay.Target, base.DynamicVars.Weak.BaseValue, base.Owner.Creature, this);
+await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target, base.DynamicVars.Weak.BaseValue, base.Owner.Creature, this);
```
Add `choiceContext` first arg to `PowerCmd.Apply`.

**Stat audit:** `UpgradeValueBy(6m)` (damage), `UpgradeValueBy(2m)` (Weak) —
both identical in both versions. Q1 upgrade deltas correct.

**Reason:** Same as Pounce. Q1's `Suppress.OnPlay` uses `ctx.Queue.Enqueue(...)` for
both attack and power. No Q1 port action.

---

### 23. Survivor.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Survivor.cs` |
| Q1 file | `Content/Cards/Survivor.cs` |
| Status | **PORT no-change** |
| git_status | — (no diff) |

No upstream diff between v0.103.2 and v0.105.1.

---

## Overall totals

| Category | Count |
|---|---|
| PORT applied (real stat/behavior change) | 0 |
| PORT no-change (no upstream diff) | 10 |
| SKIP-NO-Q1 (Godot plumbing only) | 13 |
| STUB | 0 |
| **Total Q1 cards in range** | **23** |

**PORT no-change cards (10):** PreciseCut, Prepared, Reflex, Ricochet, Skewer, Slice,
Slimed, StormOfSteel, StrikeSilent, Survivor

**SKIP-NO-Q1 cards (13):** Pounce, Predator, Regret, SerpentForm, Shadowmeld,
ShadowStep, Shiv, Snakebite, Sneaky, Speedster, Strangle, SuckerPunch, Suppress

## Analysis of upstream diff patterns

### Pattern A: `PowerCmd.Apply<T>(choiceContext, ...)` threading (11 cards)

Pounce, Predator, SerpentForm, Shadowmeld, ShadowStep, Snakebite, Sneaky, Speedster,
Strangle, SuckerPunch, Suppress — same `choiceContext` first-arg insertion documented
in Wave 7 B.2-β §Commands/ row 10. No Q1 impact; Q1 enqueues via `CombatContext`.

### Pattern B: `CombatState → ICombatState` interface refactor (Shiv)

Godot `CombatState` sealed class was extracted to `ICombatState` interface to support
multiplayer/async state injection. Affects `CreateInHand` static helpers. Q1 has no
such helpers.

### Pattern C: Access modifier refactor (Regret)

`public override → protected override` on `OnTurnEndInHand` — cosmetic visibility
correction for a Godot async virtual. No behavioral change. Q1 has no such method.

## Corrective-methodology application (Wave-10 mandate)

Per Wave-10 corrective mandate: every card's stat block was independently compared
at v0.103.2 and v0.105.1. Specifically verified:
- All `UpgradeValueBy(Nm)` calls: unchanged in every card
- All `BaseDamage` / base power constants: unchanged in every card
- No `decimal X = Nm` constant changes found

**Conclusion:** Zero real stat changes in this range. The Wave-9 Untouchable miss pattern
(numeric constant buried in same diff hunk as threading change) did not occur here.
All diff hunks in this range are purely plumbing.

## Deferred / future-wave notes

None. All 23 rows fully resolved.

## Verification results

No source files modified. Doc-only commit.

- `dotnet build sts2-headless.sln`: no source change — build state unchanged
- `BitIdenticalRoundtripTests 65/65`: no source change — expected unchanged
- `probe-upstream-initial-state 140 PASS / 20 SKIP`: no source change — expected unchanged

## LOC delta

- Source: 0 lines changed (no source edits)
- Tests: 0 lines changed (no test edits)
- Port-log doc (`wave-10-port-log-alpha.md`): ~280 lines (new file, Wave 10 / 10.α scope)
