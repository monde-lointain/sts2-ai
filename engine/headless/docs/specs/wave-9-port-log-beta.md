# Wave 9 / B.2-δ.cards-1.β — Cards Bucket (DeadlyPoison–Hy) Port Log
# v0.103.2 → v0.105.1

**Date:** 2026-05-17
**Stream:** B.2-δ.cards-1.β (parallel with 9.α and 9.γ)
**Engineer:** Sonnet 4.6 subagent
**Base SHA:** 4654701b6e6b328b90011b065afa9396180c6d93 (Wave 8 close)
**ADR refs:** ADR-026 (pin semantics), ADR-027 (caps-as-floors)
**Alpha range:** DeadlyPoison (inclusive) → Hy (inclusive, exclusive of I*)

## Summary

All 21 Q1 cards in this range are PORT (no-change). No source edits. No test edits.
No LOC delta in source or tests.

**Dominant upstream pattern:** Every card with an upstream diff in this range shows
one of two change classes:

1. **`choiceContext` threading into `PowerCmd.Apply<TPower>()`** — upstream added
   `PlayerChoiceContext choiceContext` as the first argument to `PowerCmd.Apply`.
   Q1's headless model has no `PowerCmd`; power application goes through
   `ApplyPowerAction` / `ApplyPowerToAllEnemiesAction` queued into `ExecutionContext.Queue`.
   The `choiceContext` parameter carries Godot-specific undo/replay/multiplayer
   context that Q1's pure action-queue design does not require. No equivalent
   change is needed in Q1.

2. **VFX / scene-graph changes** (`GrandFinale` VFX preamble, `FanOfKnives`
   `BackCombatVfxContainer → GetBackVfxContainer()`) — Q1 has no VFX layer.
   All VFX calls are Godot scene-node operations absent from Q1's headless design.

One structural change (`Doubt.OnTurnEndInHand` visibility: `public→protected`) affects
only the Godot CardModel's polymorphic virtual table. Q1's `Doubt` has no
`OnTurnEndInHand` override (the turn-end Weak application is a known deferred gap).

All cards without upstream diffs: no action needed.
Cards whose upstream diffs are 100% `choiceContext`/VFX: verified PORT (no-change).

## Per-row breakdown

### 1. DeadlyPoison.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/DeadlyPoison.cs` |
| Q1 analogue | `Content/Cards/DeadlyPoison.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | M |

**Upstream diff:** `PowerCmd.Apply<PoisonPower>(target, ...) → PowerCmd.Apply<PoisonPower>(choiceContext, target, ...)`.

**Reason:** Q1 uses `ApplyPowerAction(PowerIds.Poison, BasePoison, target)` — no
`choiceContext` concept in the action queue. No behavioral gap introduced.

---

### 2. DefendSilent.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/DefendSilent.cs` |
| Q1 analogue | `Content/Cards/DefendSilent.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | — (no diff) |

**Reason:** No upstream change between v0.103.2 and v0.105.1.

---

### 3. Deflect.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Deflect.cs` |
| Q1 analogue | `Content/Cards/Deflect.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | — (no diff) |

**Reason:** No upstream change between v0.103.2 and v0.105.1.

---

### 4. DodgeAndRoll.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/DodgeAndRoll.cs` |
| Q1 analogue | `Content/Cards/DodgeAndRoll.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | M |

**Upstream diff:** `PowerCmd.Apply<BlockNextTurnPower>(base.Owner.Creature, amount, ...) → PowerCmd.Apply<BlockNextTurnPower>(choiceContext, ...)`.

**Reason:** `BlockNextTurnPower` application is already documented as a deferred gap
in Q1's `DodgeAndRoll.cs` (S12 note). Even if the power were present, the change is
pure `choiceContext` threading — not applicable to Q1.

---

### 5. Doubt.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Doubt.cs` |
| Q1 analogue | `Content/Cards/Doubt.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | M |

**Upstream diff (2 changes):**
1. `public override async Task OnTurnEndInHand` → `protected override ...` (access modifier).
2. `PowerCmd.Apply<WeakPower>(...)` → `PowerCmd.Apply<WeakPower>(choiceContext, ...)`.

**Reason:** Q1's `Doubt` is an unplayable Curse stub with no `OnTurnEndInHand`
override. The turn-end Weak application is a known deferred gap (Phase 2+). The
access modifier change affects upstream's polymorphism only; the `choiceContext`
threading does not apply to Q1.

---

### 6. EchoingSlash.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/EchoingSlash.cs` |
| Q1 analogue | `Content/Cards/EchoingSlash.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | M |

**Upstream diff:** `AttackCommand.CreateContextAsync(combatState, this) → AttackCommand.CreateContextAsync(combatState, choiceContext, this)`.

**Reason:** Q1 uses `DealDamageAction(BaseDamage, target)` — no `AttackCommand` or
`AttackContext` in Q1's action queue. `choiceContext` threading is not applicable.

---

### 7. Envenom.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Envenom.cs` |
| Q1 analogue | `Content/Cards/Envenom.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | M |

**Upstream diff:** `PowerCmd.Apply<EnvenomPower>(base.Owner.Creature, ...) → PowerCmd.Apply<EnvenomPower>(choiceContext, ...)`.

**Reason:** Q1 uses `ApplyPowerAction(PowerIds.Envenom, BaseAmount, null)` — no
`choiceContext` applicable.

---

### 8. EscapePlan.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/EscapePlan.cs` |
| Q1 analogue | `Content/Cards/EscapePlan.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | — (no diff) |

**Reason:** No upstream change between v0.103.2 and v0.105.1.

---

### 9. Expertise.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Expertise.cs` |
| Q1 analogue | `Content/Cards/Expertise.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | — (no diff) |

**Reason:** No upstream change between v0.103.2 and v0.105.1.

---

### 10. Expose.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Expose.cs` |
| Q1 analogue | `Content/Cards/Expose.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | M |

**Upstream diff:** `PowerCmd.Apply<VulnerablePower>(target, amount, ...) → PowerCmd.Apply<VulnerablePower>(choiceContext, target, amount, ...)`.

**Reason:** Q1 uses `ApplyPowerAction(PowerIds.Vulnerable, BasePower, target)` — no
`choiceContext` applicable.

---

### 11. FanOfKnives.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/FanOfKnives.cs` |
| Q1 analogue | `Content/Cards/FanOfKnives.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | M |

**Upstream diff (2 changes):**
1. `PowerCmd.Apply<FanOfKnivesPower>(base.Owner.Creature, 1m, ...) → PowerCmd.Apply<FanOfKnivesPower>(choiceContext, ...)`.
2. VFX: `NCombatRoom.Instance?.BackCombatVfxContainer.AddChildSafely(...)` → `base.Owner.Creature.GetBackVfxContainer()?.AddChildSafely(...)`.
3. Removed `using MegaCrit.Sts2.Core.Nodes.Rooms` import.

**Reason:** Q1 uses `ApplyPowerAction(PowerIds.FanOfKnives, BaseShivs, null)` — no
`choiceContext` or VFX layer. Both changes are not applicable.

---

### 12. Finisher.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Finisher.cs` |
| Q1 analogue | `Content/Cards/Finisher.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | — (no diff) |

**Reason:** No upstream change between v0.103.2 and v0.105.1.

---

### 13. Flanking.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Flanking.cs` |
| Q1 analogue | `Content/Cards/Flanking.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | M |

**Upstream diff:** `PowerCmd.Apply<FlankingPower>(target, 2m, ...) → PowerCmd.Apply<FlankingPower>(choiceContext, target, 2m, ...)`.

**Reason:** Q1's `Flanking.OnPlay` is a comment-only stub (hook-only effect; smoke
records nothing). No power application is queued; `choiceContext` threading is moot.

---

### 14. Flechettes.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Flechettes.cs` |
| Q1 analogue | `Content/Cards/Flechettes.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | — (no diff) |

**Reason:** No upstream change between v0.103.2 and v0.105.1.

---

### 15. FlickFlack.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/FlickFlack.cs` |
| Q1 analogue | `Content/Cards/FlickFlack.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | — (no diff) |

**Reason:** No upstream change between v0.103.2 and v0.105.1.

---

### 16. FollowThrough.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/FollowThrough.cs` |
| Q1 analogue | `Content/Cards/FollowThrough.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | — (no diff) |

**Reason:** No upstream change between v0.103.2 and v0.105.1.

---

### 17. Footwork.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Footwork.cs` |
| Q1 analogue | `Content/Cards/Footwork.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | M |

**Upstream diff:** `PowerCmd.Apply<DexterityPower>(base.Owner.Creature, ...) → PowerCmd.Apply<DexterityPower>(choiceContext, ...)`.

**Reason:** Q1 uses `ApplyPowerAction(PowerIds.Dexterity, BaseDex, null)` — no
`choiceContext` applicable.

---

### 18. GrandFinale.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/GrandFinale.cs` |
| Q1 analogue | `Content/Cards/GrandFinale.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | M |

**Upstream diff (VFX + hit-fx change):**
1. Added `NGrandFinaleVfx` anticipation preamble before damage (new VFX node, waits
   `NGrandFinaleVfx.totalAnticipationDuration`).
2. `.WithHitFx("vfx/vfx_attack_slash", null, "blunt_attack.mp3")` →
   `.WithHitVfxNode(NGrandFinaleImpactVfx.Create).WithHitFx(null, null, "blunt_attack.mp3")`.
3. New `using` imports: `Helpers`, `NCombatRoom`, `NGrandFinaleVfx`.

**Reason:** Both changes are pure VFX — GrandFinale's damage value (60 base) and
target (all opponents) are unchanged. Q1 uses `DealDamageAction(BaseDamage, target)`.
No VFX layer in Q1. No behavioral delta for the headless engine.

---

### 19. HandTrick.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/HandTrick.cs` |
| Q1 analogue | `Content/Cards/HandTrick.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | — (no diff) |

**Reason:** No upstream change between v0.103.2 and v0.105.1.

---

### 20. Haze.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/Haze.cs` |
| Q1 analogue | `Content/Cards/Haze.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | M |

**Upstream diff:** `PowerCmd.Apply<PoisonPower>(hittableEnemy, ...) → PowerCmd.Apply<PoisonPower>(choiceContext, hittableEnemy, ...)` (inside `foreach` loop).

**Reason:** Q1 uses `ApplyPowerAction(PowerIds.Poison, BasePoison, target)` —
the `foreach` over `HittableEnemies` is handled by `TargetType.AllEnemies` at
dispatch time. No `choiceContext` applicable.

---

### 21. HiddenDaggers.cs

| Field | Value |
|---|---|
| Upstream path | `src/Core/Models/Cards/HiddenDaggers.cs` |
| Q1 analogue | `Content/Cards/HiddenDaggers.cs` |
| Status | **PORT (no-change)** |
| Upstream git_status | — (no diff) |

**Reason:** No upstream change between v0.103.2 and v0.105.1.

---

## SKIP-NO-Q1 upstream cards in range

Cards present in upstream `src/Core/Models/Cards/` alphabetically between
DeadlyPoison and Hy that have **no Q1 analogue**:

DanseMacabre, DarkEmbrace, Darkness, DarkShackles, Deathbringer, DeathMarch,
DeathsDoor, Debilitate, Debris, Debt, Decay, DecisionsDecisions, DefendDefect,
DefendIronclad, DefendNecrobinder, DefendRegent, Defile, Defragment, Defy, Delay,
Demesne, DemonForm, DemonicShield, DeprecatedCard, Devastate, DevourLife, Dirge,
Discovery, Disintegration, Dismantle, Distraction, Dominate, DoubleEnergy,
DrainPower, DramaticEntrance, Dredge, DrumOfBattle, Dualcast, DualWield, DyingStar,
EchoForm, Eidolon, EndOfDays, EnergySurge, EnfeeblingTouch, Enlightenment,
Enthralled, Entrench, Entropy, Equilibrium, Eradicate, EternalArmor, EvilEye,
ExpectAFight, Exterminate, FallingStar, Fasten, Fear, Feed, FeedingFrenzy,
FeelNoPain, Feral, Fetch, FiendFire, FightMe, FightThrough, Finesse, Fisticuffs,
FlakCannon, FlameBarrier, FlashOfSteel, Flatten, FocusedStrike, Folly,
ForbiddenGrimoire, ForegoneConclusion, ForgottenRitual, FranticEscape, Friendship,
Ftl, Fuel, Furnace, Fusion, GammaBlast, GangUp, GatherLight, Genesis,
GeneticAlgorithm, GiantRock, Glacier, Glasswork, Glimmer, GlimpseBeyond,
Glitterstream, Glow, GoForTheEyes, GoldAxe, Graveblast, GraveWarden, Greed,
Guards, GuidingStar, Guilty, GunkUp, Hailstorm, HammerTime, HandOfGreed, Hang,
Haunt, Havoc, Headbutt, HeavenlyDrill, Hegemony, HeirloomHammer, HelixDrill,
HelloWorld, Hellraiser, Hemokinesis, HiddenCache, HiddenGem, HighFive, Hologram,
Hotfix, HowlFromBeyond, HuddleUp, Hyperbeam.

These are all non-Silent / multi-class / new cards per ADR-027 (Phase-1 caps
track upstream). Growth deferred to a future wave.

---

## Overall totals

| Category | Count |
|---|---|
| PORT (no-change) | 21 |
| PORT (applied)   | 0  |
| SKIP-NO-Q1       | 126 (upstream-only, not enumerated per row) |
| STUB             | 0  |
| Total Q1 rows    | 21 |

## Verification results

No source files were modified. Doc-only commit.

- `dotnet build sts2-headless.sln`: no source change — build state unchanged
- `BitIdenticalRoundtripTests 65/65`: no source change — unchanged
- `probe-upstream-initial-state 140 PASS / 20 SKIP`: no source change — unchanged

## LOC delta

- Source: 0 lines changed
- Tests: 0 lines changed
- Port-log doc (`wave-9-port-log-beta.md`): ~195 lines (new file)
