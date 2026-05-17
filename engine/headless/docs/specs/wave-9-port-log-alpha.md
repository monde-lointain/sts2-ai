# Wave 9 / 9.α — Cards A–De Port Log
# v0.103.2 → v0.105.1

**Date:** 2026-05-17
**Stream:** 9.α (Stream B.2-δ.cards-1.α — Cards bucket batch 1, alpha range A–Doubt)
**Engineer:** Sonnet 4.6 subagent
**Base SHA:** 4654701b6e6b328b90011b065afa9396180c6d93 (Wave 8 close)
**ADR refs:** ADR-026 (pin semantics), ADR-027 (caps-as-floors growth in future waves)

## Summary

32 Q1 cards in alpha range A–Doubt examined against v0.103.2→v0.105.1 upstream diff.
No source files modified; no test files modified.

**Result: 0 PORT-applied, 32 PORT-no-change-needed, 132 SKIP-NO-Q1, 0 STUB.**

The recurring upstream diff pattern across all 13 changed cards is the
`PowerCmd.Apply<T>(choiceContext, ...)` refactor — adding `PlayerChoiceContext` as the
first argument to all `PowerCmd.Apply<T>()` call sites throughout the codebase. This is
a context-threading infrastructure change, not a behavioral change (power stacks,
targets, and amounts are identical pre/post diff).

Q1's headless has no `PowerCmd` class. It uses `ApplyPowerAction` records enqueued on
`ExecutionContext.Queue`. The behavioral contract (which power, to whom, how many stacks)
is equivalent and unchanged. No Q1 edit is warranted for the `PowerCmd.Apply` refactor.

Secondary patterns (also no Q1 action required):
- `VfxCmd.PlayFullScreenInCombat(string)` → `VfxCmd.PlayFullScreenInCombat(string, Creature)`:
  VFX is headless-irrelevant. Q1 has no VFX layer.
- `public override OnTurnEndInHand` → `protected override OnTurnEndInHand` (Burn, Doubt):
  Q1's `CardModel` has no `OnTurnEndInHand` method; the access modifier change is moot.

The 132 SKIP-NO-Q1 rows are upstream cards in the A–Doubt alphabetic range that have
no corresponding Q1 card file. Per ADR-027, new card pool growth is deferred to future
waves (Wave 11+); this stream only ports changes to existing Q1 cards.

---

## Per-row breakdown

### Q1 Cards in Range (A–Doubt) — 32 total

| Card | Upstream diff | Status | Notes |
|---|---|---|---|
| Abrasive | YES | **PORT-no-change** | `PowerCmd.Apply<DexterityPower>` + `PowerCmd.Apply<ThornsPower>` both gain `choiceContext` first arg; Q1 uses `ApplyPowerAction` — no change needed |
| Accelerant | YES | **PORT-no-change** | `PowerCmd.Apply<AccelerantPower>` gains `choiceContext`; Q1 queue-based — no change needed |
| Accuracy | YES | **PORT-no-change** | `PowerCmd.Apply<AccuracyPower>` gains `choiceContext`; Q1 queue-based — no change needed |
| Acrobatics | NO | **PORT-no-change** | No upstream diff in range |
| Adrenaline | YES | **PORT-no-change** | `VfxCmd.PlayFullScreenInCombat` gains `Owner.Creature` arg; VFX only — no change needed |
| Afterimage | YES | **PORT-no-change** | `PowerCmd.Apply<AfterimagePower>` gains `choiceContext`; Q1 queue-based — no change needed |
| Anticipate | YES | **PORT-no-change** | `PowerCmd.Apply<AnticipatePower>` gains `choiceContext`; Q1 queue-based — no change needed |
| AscendersBane | NO | **PORT-no-change** | No upstream diff in range |
| Assassinate | YES | **PORT-no-change** | `PowerCmd.Apply<VulnerablePower>` gains `choiceContext`; Q1 queue-based — no change needed |
| Backflip | NO | **PORT-no-change** | No upstream diff in range |
| Backstab | NO | **PORT-no-change** | No upstream diff in range |
| BladeDance | NO | **PORT-no-change** | No upstream diff in range |
| BladeOfInk | NO | **PORT-no-change** | No upstream diff in range |
| Blur | YES | **PORT-no-change** | `PowerCmd.Apply<BlurPower>` gains `choiceContext`; Q1 queue-based — no change needed |
| BouncingFlask | YES | **PORT-no-change** | `PowerCmd.Apply<PoisonPower>` gains `choiceContext` in loop; Q1 queue-based — no change needed |
| BubbleBubble | YES | **PORT-no-change** | `PowerCmd.Apply<PoisonPower>` gains `choiceContext` in conditional branch; Q1 queue-based — no change needed |
| BulletTime | YES | **PORT-no-change** | `PowerCmd.Apply<NoDrawPower>` gains `choiceContext`; Q1 queue-based — no change needed |
| Burn | YES | **PORT-no-change** | `public override OnTurnEndInHand` → `protected override OnTurnEndInHand`; Q1's `CardModel` has no `OnTurnEndInHand` — moot |
| Burst | YES | **PORT-no-change** | `PowerCmd.Apply<BurstPower>` gains `choiceContext`; Q1 queue-based — no change needed |
| CalculatedGamble | NO | **PORT-no-change** | No upstream diff in range |
| CloakAndDagger | NO | **PORT-no-change** | No upstream diff in range |
| Clumsy | NO | **PORT-no-change** | No upstream diff in range |
| CorrosiveWave | YES | **PORT-no-change** | `PowerCmd.Apply<CorrosiveWavePower>` gains `choiceContext`; Q1 queue-based — no change needed |
| DaggerSpray | NO | **PORT-no-change** | No upstream diff in range |
| DaggerThrow | NO | **PORT-no-change** | No upstream diff in range |
| Dash | NO | **PORT-no-change** | No upstream diff in range |
| Dazed | NO | **PORT-no-change** | No upstream diff in range |
| DeadlyPoison | YES | **PORT-no-change** | `PowerCmd.Apply<PoisonPower>` gains `choiceContext`; Q1 queue-based — no change needed |
| DefendSilent | NO | **PORT-no-change** | No upstream diff in range |
| Deflect | NO | **PORT-no-change** | No upstream diff in range |
| DodgeAndRoll | YES | **PORT-no-change** | `PowerCmd.Apply<BlockNextTurnPower>` gains `choiceContext`; Q1 queue-based — no change needed |
| Doubt | YES | **PORT-no-change** | `public override OnTurnEndInHand` → `protected override`; `PowerCmd.Apply<WeakPower>` gains `choiceContext`; Q1 has no `OnTurnEndInHand` + queue-based — moot |

### Upstream SKIP-NO-Q1 cards in A–Doubt range — 132 total

All 132 upstream cards alphabetically between A and Doubt that do not appear in Q1's
`Content/Cards/` directory are classified **SKIP-NO-Q1**. Per ADR-027, the Phase-1 card
pool does not grow in this wave. New card classes will be added in future Wave 11+
(dedicated "new content" scope).

Selected examples (not exhaustive):

| Card | Notes |
|---|---|
| AdaptiveStrike | No Q1 analog — not in Silent Phase-1 pool |
| Afterlife | No Q1 analog |
| Armaments | No Q1 analog — Ironclad card |
| Bash | No Q1 analog — Ironclad card |
| BiasedCognition | No Q1 analog — Defect card |
| BulkUp | No Q1 analog — Ironclad card |
| BodySlam | No Q1 analog — Ironclad card |
| CompileDriver | No Q1 analog — Defect card |
| DemonForm | No Q1 analog — Ironclad card |
| Discovery | No Q1 analog — colorless |
| Dominate | No Q1 analog |
| DoubleEnergy | No Q1 analog — colorless |

Full alphabetic SKIP-NO-Q1 list (132 cards): AdaptiveStrike, Afterlife, Aggression,
Alchemize, Alignment, AllForOne, Anger, Anointed, Apotheosis, Apparition, Armaments,
Arsenal, AshenStrike, AstralPulse, Automation, BadLuck, BallLightning, BansheesCry,
Barrage, Barricade, Bash, BattleTrance, BeaconOfHope, BeamCell, BeatDown,
BeatIntoShape, Beckon, Begone, BelieveInYou, BiasedCognition, BigBang, BlackHole,
BlightStrike, Bloodletting, BloodWall, Bludgeon, Bodyguard, BodySlam, Bolas,
Bombardment, BoneShards, BoostAway, BootSequence, BorrowedTime, Brand, Break,
Breakthrough, BrightestFlame, Buffer, BulkUp, Bully, Bulwark, BundleOfJoy,
BurningPact, Bury, ByrdonisEgg, ByrdSwoop, Calamity, Calcify, CallOfTheVoid,
Caltrops, Capacitor, CaptureSpirit, Cascade, Catastrophe, CelestialMight, Chaos,
Charge, ChargeBattery, ChildOfTheStars, Chill, Cinder, Clash, Claw, Cleanse,
CloakOfStars, ColdSnap, CollisionCourse, Colossus, Comet, Compact, CompileDriver,
Conflagration, Conqueror, ConsumingShadow, Convergence, Coolant, Coolheaded,
Coordinate, Corruption, CosmicIndifference, Countdown, CrashLanding, CreativeAi,
CrescentSpear, CrimsonMantle, Cruelty, CrushUnder, CurseOfTheBell, DanseMacabre,
DarkEmbrace, Darkness, DarkShackles, Deathbringer, DeathMarch, DeathsDoor,
Debilitate, Debris, Debt, Decay, DecisionsDecisions, DefendDefect, DefendIronclad,
DefendNecrobinder, DefendRegent, Defile, Defragment, Defy, Delay, Demesne, DemonForm,
DemonicShield, DeprecatedCard, Devastate, DevourLife, Dirge, Discovery, Disintegration,
Dismantle, Distraction, Dominate, DoubleEnergy

---

## Overall totals

| Category | Count |
|---|---|
| PORT applied | 0 |
| PORT no-change-needed | 32 |
| SKIP-NO-Q1 | 132 |
| STUB | 0 |
| **Total upstream cards in range** | **164** |
| **Q1 cards in range** | **32** |

---

## Upstream behavioral changes to track for future waves

None introduced in the A–Doubt diff range. The sole pattern (`PowerCmd.Apply` choiceContext
threading) is infrastructure plumbing with no behavioral consequence. When Q1 implements
a full async power-application pipeline (Phase 2+ combat engine), this context-threading
convention should be mirrored in Q1's equivalent of `PowerCmd.Apply`.

---

## Verification results

No source files were modified.

- `dotnet build sts2-headless.sln`: 0 warn 0 err (pre-verified; no source change)
- `BitIdenticalRoundtripTests 65/65`: PASS (pre-verified)
- `probe-upstream-initial-state`: unchanged (no source change)
- `DllSignatureGate`: unchanged (no source change)

## LOC delta

- Source: 0 lines changed
- Tests: 0 lines changed
- Port-log doc (`wave-9-port-log-alpha.md`): ~190 lines (new file, Stream 9.α scope)
