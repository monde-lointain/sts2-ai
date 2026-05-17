# Wave 8 / B.2-γ — Model-Bases Bucket Port Log
# v0.103.2 → v0.105.1

**Date:** 2026-05-17
**Stream:** B.2-γ (single stream, Wave 8)
**Engineer:** Sonnet 4.6 subagent
**Base SHA:** a06650794e0f9f599520745f3d24b0c074e6259c (Wave 7 close)
**ADR refs:** ADR-026 (pin semantics), ADR-027 (caps-as-floors), Q1-ADR-009 (multiplayer stripped)

## Summary

All 13 upstream model-bases PORT rows are SKIP-NO-Q1.
No source edits made; no test edits made; no LOC delta.

**Rationale:** Q1's `Content/Models/` directory contains only purpose-built
headless analogue classes (`CardModel`, `MonsterModel`, `PowerModel`, `RelicModel`,
`PotionModel`) plus supporting enums. The 13 upstream `src/Core/Models/` files
are either:

1. **Not present in Q1 at all** (`AbstractModel`, `AfflictionModel`, `BadgeModel`,
   `CharacterModel`, `EventModel`, `ModelDb`, `OrbModel`) — these are Godot-coupled
   base classes or registry utilities with no headless analogue in Q1's substrate.
2. **Present in Q1 under a different path** (`EncounterModel` → `Content/Encounters/`)
   with a completely different structural design (see row 6 below).
3. **Present in Q1 as a stripped headless stub** (`CardModel`, `MonsterModel`,
   `PowerModel`, `RelicModel`, `PotionModel`) where upstream diffs affect only
   Godot-specific members (asset paths, `CombatState` → `ICombatState` param
   threading, VFX/audio/localization helpers, multiplayer scaling) not present
   in Q1's headless design.

The recurring pattern across all 13 rows is the upstream `CombatState→ICombatState`
refactor (Wave 7 context: ICombatState is a new Godot interface that Q1 already
classifies as SKIP-NO-Q1 per wave-7-port-log §Combat/). Q1's engine uses
`ICombatContext` for the equivalent abstraction role.

## EncounterModel.GenerateMonsters(Rng) Signature Check

**UNCHANGED — no re-surface needed.**

Wave 3 (`1efa445`) added `public virtual IReadOnlyList<string> GenerateMonsters(Rng rng) => MonsterIds;`
to Q1's `Content/Encounters/EncounterModel.cs`.

Upstream v0.105.1's `EncounterModel.cs` diff shows only:
```diff
-  public string GetNextSlot(CombatState combatState)
+  public string GetNextSlot(ICombatState combatState)
```
The `GenerateMonsters` method in upstream's file is `abstract IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()` —
a fundamentally different signature (takes `MonsterModel` tuples, no `Rng`). These are
separate methods in separate designs; Q1's `GenerateMonsters(Rng rng)` is Q1's own
virtual that was never derived from the upstream analog. No divergence.

## Per-row breakdown

### 1. AbstractModel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/AbstractModel.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Diff summary:** ~30 virtual method signature changes:
- `AfterAttack(AttackCommand)` → `AfterAttack(PlayerChoiceContext, AttackCommand)`
- 4 new `AfterAutoPlay*` phase-entry virtuals
- `AfterCardGeneratedForCombat(CardModel, bool addedByPlayer)` → `AfterCardGeneratedForCombat(CardModel, Player? creator)`
- `AfterCardRetained` removed
- `BeforeRewardsOffered` removed
- `BeforePlayPhaseStart`/`Late` removed
- 5× `CombatState → ICombatState` param swaps
- New `AfterFlush` virtual
- `ModifyOrbValue(Player, decimal)` → `ModifyOrbValue(OrbModel, decimal)`
- New `TryModifyEnergyCostInCombatLate`

**Reason:** `AbstractModel` is the Godot base class for all content models. It carries
async `Task`-returning virtual hooks, scene-node references, localization, hover-tip,
audio, and multiplayer wiring. Q1's content models (`CardModel`, `MonsterModel`, etc.)
do NOT inherit from `AbstractModel` — each is a clean headless class with only the
catalog metadata shape Q1 needs. There is no `AbstractModel.cs` in Q1 and none will
be added.

---

### 2. AfflictionModel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/AfflictionModel.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Diff summary:**
```diff
-  public CombatState CombatState => Card.CombatState;
+  public ICombatState CombatState => Card.CombatState;
```
One return-type change in the `CombatState` property (part of the systemic
`CombatState→ICombatState` refactor).

**Reason:** No `AfflictionModel` in Q1. Affliction is a card-modifier concept
tied to Godot's card scene-node system. Q1 Phase 1 does not model afflictions;
no headless analogue exists or is planned for Phase 1.

---

### 3. BadgeModel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/BadgeModel.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | A (new file) |

**Diff summary:** New 6-line file:
```csharp
public abstract class BadgeModel : AbstractModel
{
    public override bool ShouldReceiveCombatHooks => true;
}
```

**Reason:** Badges are a new upstream feature (v0.105.1). No badge concept exists
in Q1 Phase 1. Adding `BadgeModel` would require adding `AbstractModel` first, which
is SKIP-NO-Q1. No Q1 consumer. Out of scope for Phase 1.

---

### 4. CardModel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/CardModel.cs` |
| Q1 analogue | `engine/headless/src/Sts2Headless.Domain/Content/Models/CardModel.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Diff summary (behavioral items):**
- `CombatState` property type `CombatState?` → `ICombatState?`
- `CardScope` property simplified to one-liner
- `OnTurnEndInHand` visibility changed `public→protected`; new `OnTurnEndInHandWrapper` method
- `GetResultPileType→GetResultPileTypeForCardPlay` rename
- New `GetResultPileTypeForOnTurnEndInHandEffect` virtual
- `ExhaustOnNextPlay = false` cleared in two places
- `AncientBorderPath`/`AncientBorder` UI assets added
- `GainsBlock` added to description dict
- Asset path string updated for atlas

**Reason:** Q1's `CardModel.cs` (~120 LOC) carries only: `Id`, `Cost`, `IsXCost`,
`Type`, `Rarity`, `Target`, `Tags`, and abstract `OnPlay`. It inherits nothing from
upstream's `CardModel` (~1700 LOC Godot class). Every upstream diff item is in
Godot-only surface: asset paths, scene-wiring, async Task lifecycle, multiplayer
card-scope. No behavioral port applicable.

---

### 5. CharacterModel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/CharacterModel.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Diff summary:**
```diff
+  public virtual bool IsPlayable => true;
```
One new virtual property for character selection eligibility.

**Reason:** No `CharacterModel` in Q1. Character model carries Godot-specific
animation, localization strings, and UI assets. Q1 Phase 1 has no character
selection flow; character identity enters via `RunState` seed configuration.

---

### 6. EncounterModel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/EncounterModel.cs` |
| Q1 analogue | `engine/headless/src/Sts2Headless.Domain/Content/Encounters/EncounterModel.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Diff summary:**
```diff
-  public string GetNextSlot(CombatState combatState)
+  public string GetNextSlot(ICombatState combatState)
```
One `CombatState→ICombatState` param swap in `GetNextSlot`.

**Reason:** Q1's `EncounterModel` (at `Content/Encounters/`, not `Content/Models/`)
has no `GetNextSlot` method. Slot management is a Godot combat-room layout concern.

**GenerateMonsters check:** The upstream diff does NOT touch `GenerateMonsters`. Q1's
`virtual IReadOnlyList<string> GenerateMonsters(Rng rng)` (Wave 3 / `1efa445`) is Q1's
own design, not an analog of upstream's `abstract IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()`.
The two coexist independently. Signature byte-exact: **UNCHANGED.**

---

### 7. EventModel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/EventModel.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Diff summary:**
- New `InitialPhobiaModePortraitPath` property + `HasPhobiaModePortrait` bool + `CreateInitialPhobiaModePortrait()`
- `CombatState` ctor gains `BadgeModels` parameter (2 call sites)
- `GetPreloadPaths` includes phobia-mode portrait if present

**Reason:** No `EventModel` in Q1. Events have Godot scene trees, portrait textures,
and CombatRoom construction logic. Q1 Phase 1 does not model events (out-of-scope per
quantum map; Q1 is encounter-only).

---

### 8. ModelDb.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/ModelDb.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Diff summary:**
- New `_badges` field + `BadgeModels` property (lazy-init via `AllAbstractModelSubtypes`)
- New `Badge<T>()` accessor

**Reason:** No `ModelDb` in Q1. Q1 uses the `ContentTable<TKey, TValue>` catalog
pattern (EncounterCatalog, MonsterCatalog, etc.). `ModelDb` is a Godot static registry
of all `AbstractModel` singletons, which Q1 does not use.

---

### 9. MonsterModel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/MonsterModel.cs` |
| Q1 analogue | `engine/headless/src/Sts2Headless.Domain/Content/Models/MonsterModel.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Diff summary (behavioral items):**
- `MonsterMoveList(NCreatureVisuals)` renamed to `GenerateBestiaryMoveList` with major rewrite
  (Godot bestiary UI — walks `MonsterMoveStateMachine` states, generates `LocString` names)
- `MoveNames`, `BestiaryAttackAnimId` properties removed
- New `ShouldShowInCompendium` virtual bool
- New protected `ShouldShowMoveInBestiary(string)` virtual
- `CombatState` property type `CombatState` → `ICombatState`
- `NCombatRoom.Instance?.GetCreatureNode(Creature)` → `Creature.GetCreatureNode()`

**Reason:** Q1's `MonsterModel.cs` (~300 LOC) is a completely custom headless class
with HP envelope, move-state machine (dictionary of `MonsterMove`), and `AdvanceMoveId`.
It shares only a name with upstream's `MonsterModel`. All upstream diff items are in
Godot-specific surface: bestiary compendium UI, localization, `NCreatureVisuals` scene
nodes, `CombatState` Godot type. No behavioral item applies to Q1's headless design.

---

### 10. OrbModel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/OrbModel.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Diff summary:**
- `CombatState` property type `CombatState` → `ICombatState`
- `ModifyOrbValue` passes `this` (the orb) instead of `Owner` to `Hook.ModifyOrbValue`

**Reason:** No `OrbModel` in Q1's `Content/Models/`. Orbs are a Phase-2+ content
category (Q1 Phase 1 does not implement orb channeling/evocation). The `ModifyOrbValue`
behavioral change (passing orb identity instead of owner) is noted for future orb
implementation reference but not applicable now.

**Note for future waves:** `Hook.ModifyOrbValue` now receives the `OrbModel` (not the
`Player owner`) as the second argument. When Q1 adds `OrbModel` (Phase 2+), the
`ModifyOrbValue` hook signature should reflect this v0.105.1 shape.

---

### 11. PotionModel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/PotionModel.cs` |
| Q1 analogue | `engine/headless/src/Sts2Headless.Domain/Content/Models/PotionModel.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Diff summary:**
```diff
-  CombatState combatState = Owner.Creature.CombatState;
+  ICombatState combatState = Owner.Creature.CombatState;
```
One local variable type change in `OnUseWrapper`.

**Reason:** Q1's `PotionModel.cs` (~35 LOC) is pure catalog metadata (`Id`, `Name`,
`Rarity`). It has no `OnUseWrapper` (potion use effects are a Phase-2 concern). The
upstream change is a Godot-type threading diff with no headless analog.

---

### 12. PowerModel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/PowerModel.cs` |
| Q1 analogue | `engine/headless/src/Sts2Headless.Domain/Content/Models/PowerModel.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Diff summary (behavioral items):**
- `IsInstanced bool` property replaced by `InstanceType PowerInstanceType` enum property
  (`None`/`Instanced`/`InstancedPerApplier` — drives stacking-vs-instancing in `PowerCmd.Apply`)
- `CombatState` property type `CombatState` → `ICombatState`
- `GetScaledAmountForMultiplayer` new virtual (multiplayer power scaling)
- Localization: `ApplierName`/`TargetName` now use `PlatformUtil.GetPlayerName` for player targets
- New `using` imports: `Singleton`, `Platform`, `Runs`

**Reason:** Q1's `PowerModel.cs` (~50 LOC) carries only `Id`, `Type`, `StackType`.
It has no `IsInstanced` / `InstanceType` property — Q1's `PowerStackType` enum
already captures Counter vs Single semantics sufficient for Phase 1 powers
(Strength, Vulnerable, Weak, Poison, Ritual, Thorns, Dexterity, Intangible).

The `IsInstanced→InstanceType` behavioral change affects `PowerCmd.Apply`'s
stacking logic in upstream (controls whether a second application creates a new
instance or stacks onto existing). In Q1, `CombatContext.ApplyPower` uses
`PowerStackType.Counter` (additive) vs `PowerStackType.Single` (replace). Phase 1
powers all use Counter. If instanced powers are added in Phase 2+, Q1 may need
a `PowerInstanceType` equivalent; this is flagged for future waves.

`GetScaledAmountForMultiplayer`: Q1-ADR-009 strips multiplayer. SKIP.

---

### 13. RelicModel.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/RelicModel.cs` |
| Q1 analogue | `engine/headless/src/Sts2Headless.Domain/Content/Models/RelicModel.cs` |
| Status | **SKIP-NO-Q1** |
| git_status | M |

**Diff summary:**
```diff
+  public virtual bool IsAllowedAtNeow(Player player)
+  {
+      return IsAllowed(player.RunState);
+  }
```
New virtual controlling relic eligibility at Neow (post-death reward screen).

**Reason:** Q1's `RelicModel.cs` (~145 LOC) implements the hook-subscription
lifecycle (`OnAdded`/`OnRemoved`/`SubscribeHooks`). It has no `IsAllowed`/`IsAllowedAtNeow`
method — reward filtering and Neow selection are out of scope for Phase 1.
When Q1 implements relic reward selection, `IsAllowedAtNeow` should be considered
alongside `IsAllowed`.

---

## Overall totals

| Category | Count | / 13 |
|---|---|---|
| PORT | 0 | 0 / 13 |
| SKIP-NO-Q1 | 13 | 13 / 13 |
| STUB | 0 | 0 / 13 |

## Upstream behavioral changes to track for future waves

1. **`IsInstanced → InstanceType(PowerInstanceType)`** (PowerModel.cs row 12). If Q1
   adds instanced powers (Phase 2+), `CombatContext.ApplyPower` needs to distinguish
   `None`/`Instanced`/`InstancedPerApplier` to match upstream's stacking behavior.

2. **`ModifyOrbValue` receives `OrbModel` not `Player owner`** (OrbModel.cs row 10).
   When Q1 adds OrbModel (Phase 2+), the `ModifyOrbValue` hook handler signature
   should carry the orb identity per v0.105.1 shape.

3. **`AfterCardRetained` removed** (AbstractModel.cs row 1). If Q1 ever adds
   `AfterCardRetained` hook firing, suppress it — upstream removed it in v0.105.1.

4. **`BeforeRewardsOffered` removed** (AbstractModel.cs row 1). Same note.

5. **`BeforePlayPhaseStart`/`Late` removed** (AbstractModel.cs row 1). Replaced by
   `AfterAutoPrePlayPhaseEntered*` virtuals. If Q1 adds play-phase hooks, use
   the new names.

6. **`AfterCardGeneratedForCombat(CardModel, bool) → (CardModel, Player?)`** — tracked
   in wave-7-port-log §Behavioral changes item 1. Reinforced here.

7. **`IsAllowedAtNeow(Player)` on RelicModel** (RelicModel.cs row 13). When Q1
   implements Neow relic selection, use this virtual instead of `IsAllowed`.

8. **`ExhaustOnNextPlay` cleared in `GetResultPileTypeForCardPlay` and `CloneCard`**
   (CardModel.cs row 4). If Q1 adds per-instance `ExhaustOnNextPlay` state, ensure
   it is cleared on clone and on play to avoid double-exhaust.

## Verification results

No source files were modified. This wave delivers a doc-only commit analogous to Wave 7.

- `dotnet build sts2-headless.sln`: 0 warn 0 err (no source change — unchanged)
- `BitIdenticalRoundtripTests 65/65`: no source change — unchanged
- `probe-upstream-initial-state 140 PASS / 20 SKIP`: no source change — unchanged
- `DllSignatureGate PASS`: no source change — unchanged
- `EncounterModel.GenerateMonsters(Rng)` signature: **UNCHANGED** (Wave 3 ratified; no re-surface)

## LOC delta

- Source: 0 lines changed (no source edits)
- Tests: 0 lines changed (no test edits)
- Port-log doc (`wave-8-port-log.md`): ~240 lines (new file, Wave 8 scope)
