# Phase-1 Encounter Port Decisions

Date: 2026-05-11
Author: B.1-δ (invented-encounter policy execution per the sts2-ai lead's 2026-05-11 dispatch)
Context: Stream C canary surfaced 10 Q1 encounters with no upstream STS2 equivalent. Per lead's 2026-05-11 directive (c) — binary port-or-delete decision, "no Q1-only middle ground" (DEFER permitted only as escape hatch pending upstream).

## TL;DR

- **1 PORT** (KaiserCrabBoss → Crusher + Rocket spawn)
- **7 DELETE** (genuine STS1-only monsters, no STS2 analogue)
- **2 DEFER** (SmallSlimes / MediumSlimes — STS2 has analogue monsters but encounter uses RNG-driven spawn, blocked by missing `EncounterModel.GenerateMonsters(Rng)` lift in Q1)
- **Re-surface trigger HIT.** Fewer than 6 cleanly portable → expected probe target drops from 220/220 to ~130/130. **Stopping for lead review before executing destructive changes** per the prompt's "STOP and return DONE_WITH_CONCERNS" directive.

## Decision table

| # | Encounter | Decision | STS2 analogue | Rationale |
|---|---|---|---|---|
| 1 | JawWormSolo | DELETE | — | JawWorm is STS1-only. No `JawWorm.cs` in `~/development/projects/godot/sts2/src/Core/Models/Monsters/`. No `JawWormSolo` / `JawWormNormal` in `Encounters/`. Genuine STS1 hold-over. |
| 2 | TwoLouseNormal | DELETE | (`LouseProgenitorNormal` exists upstream but spawns a single different monster) | STS2 has no RedLouse / GreenLouse / "two louse" encounter. Upstream `LouseProgenitorNormal` spawns one `LouseProgenitor` monster (which Q1 already has). That's a *new* encounter, not a port of "TwoLouseNormal". Mark Q1's invented encounter DELETE; consider follow-up to add `LouseProgenitorNormal` as a fresh STS2 encounter outside this stream's scope. |
| 3 | SmallSlimes | DEFER | `SlimesWeak` (upstream) spawns 1×{LeafSlimeS, TwigSlimeS} + 1×{LeafSlimeM, TwigSlimeM} + leftover small via `Rng.NextItem` | Monsters exist upstream (`LeafSlimeS.cs`, `TwigSlimeS.cs`, etc.), but `SlimesWeak.GenerateMonsters` consumes encounter-RNG state (`base.Rng.NextItem`). Q1's `EncounterModel` resolves a static `IReadOnlyList<string>` spawn list — no encounter-RNG plumbing exists in Q1. Porting requires the new feature: a per-encounter RNG seeded as upstream (`uint seed = runState.Rng.Seed + totalFloor + hash(encounter.Id)`) plus a `GenerateMonsters(Rng)` virtual on EncounterModel. **Out of scope for B.1-δ** which targets invented-encounter resolution, not architectural extension. Once the encounter-RNG lift lands, SmallSlimes/MediumSlimes become straightforward ports. |
| 4 | MediumSlimes | DEFER | `SlimesNormal` (upstream) spawns TwigSlimeM + LeafSlimeM + 2 random small via `Rng.NextBool` | Same architectural blocker as SmallSlimes. STS2 monsters exist; encounter-RNG plumbing does not yet exist in Q1. |
| 5 | LargeSlimeBoss | DELETE | — | STS2 has **no Large slimes** (no `LeafSlimeL.cs` / `TwigSlimeL.cs` / `AcidSlimeL.cs` / `SpikeSlimeL.cs`). The STS1 "Slime Boss" concept did not survive into STS2's content catalog. Genuine deletion. |
| 6 | SentryTrio | DELETE | (`AxebotsNormal` upstream spawns 2 Axebots — not 3 Sentries) | STS2 has no `Sentry` monster. The robotic-encounter slot in upstream is filled by Axebots (2), MechaKnight (1 elite), HunterKiller (1), Guardbot (one of the Axebots ensemble). None of these match "trio of sentries". Q1's invented encounter is a STS1 hold-over. |
| 7 | SnakePlantSolo | DELETE | — | STS2 has no `SnakePlant.cs` and no plant-monster analogue. Confirmed via `find` on monster names containing `plant`, `snake`, `vine`, `vegetation` — only `VineShamblerNormal.cs` exists (a different monster type, not a 1:1 analogue). STS1 hold-over. |
| 8 | FungalBossEncounter | DELETE | — | STS2 has no `FungalBoss` monster and no fungal-monster analogues. `FragrantMushroom.cs` / `BigMushroom.cs` are relics, `SporeMind.cs` is a card, `HungryForMushrooms.cs` is an event — no monster. STS1 hold-over. |
| 9 | CenturyGuardBoss | DELETE | — | STS2 has no `CenturyGuard` monster. The "Knights"-tagged monsters in STS2 are `FrogKnight`, `MagiKnight`, `MysteriousKnight`, `MechaKnight`, `FlailKnight`, `SpectralKnight` — none match Century Guard. STS1 hold-over (Century Guard is a STS1 elite). |
| 10 | KaiserCrabBoss | PORT | `KaiserCrabBoss` (upstream) spawns `Crusher` + `Rocket` | STS2 *does* have `KaiserCrabBoss` encounter — but its spawn list is **not** "KaiserCrab" (no such monster in STS2). Upstream spawns `Crusher` (left arm, 5-state rotation) and `Rocket` (right arm, 5-state rotation) in slots `"crusher"` / `"rocket"`. Q1's "KaiserCrab" monster does not exist upstream. **PORT = change Q1's KaiserCrabBoss spawn list from `[KaiserCrab]` to `[Crusher, Rocket]`, add Crusher + Rocket monster classes** (stub intent rotations mirroring γ's pattern; Crusher/Rocket use BackAttackLeft/Right + CrabRage + Surrounded powers which are γ territory — defer power-application bodies to γ-T3, define stub-only rotations here). Drop the existing `KaiserCrab` monster class as part of the same change (no other encounter references it). |

## Quantitative impact

| Metric | Pre-δ | Post-δ (planned) |
|---|---|---|
| Q1 encounters registered | 22 | 22 − 7 (DELETE) = **15** |
| STS2-comparable encounters | 12 (Stream C) | 12 + 1 (PORT) = **13** |
| Encounters deferred (not in upstream-byte-compare) | 0 | **2** (DEFER) |
| Probe corpus initial-state entries | 220 | **130** (13 encounters × 10 seeds, dropping 90 entries for the 7 deletes; the 2 DEFERs stay registered but skip upstream compare) |
| MissingUpstream skips in probe | 100 | 20 (2 DEFER × 10 seeds × 1 mode) |

**Probe target moves from 220/220 PASS → 130/130 PASS** if the lead accepts this plan.

## Re-surface trigger analysis

The prompt's threshold: *"If fewer than 6 of the 10 encounters are cleanly portable, the probe target drops from 220/220 to ~120/120. Lead wants visibility in that case. STOP and return DONE_WITH_CONCERNS."*

Cleanly portable: **1** (KaiserCrabBoss only). Architectural-defer: **2** (slime encounters). Genuine STS1-only: **7**.

Trigger hit: **1 < 6** → STOP.

Lead's call:

(a) **Accept the plan** as documented (1 PORT + 7 DELETE + 2 DEFER → probe target ~130/130). Cheapest. Lowers probe target but is honest about STS1 vs STS2 content drift. Two slime encounters remain registered in Q1 but skip upstream-comparison (they self-consistency-pass as a static-spawn list — useful for non-byte-equivalence regression).

(b) **Push for more aggressive porting on slimes.** Land an `EncounterModel.GenerateMonsters(Rng)` lift in this stream (or a follow-up stream B.1-ε), wire encounter-RNG seeded per upstream, port `SlimesWeak` and `SlimesNormal` as RNG-driven. Lifts probe target to ~150/150 (3 added slime encounters × 10 seeds = +30). Estimated ~80-150 LOC + 3 new monster files (`LeafSlimeS`, `LeafSlimeM`, `TwigSlimeS`, `TwigSlimeM`) + RunRngSet exposure to encounter layer.

(c) **Add `LouseProgenitorNormal` as a new STS2 encounter.** Not a port of `TwoLouseNormal` (different encounter), but uses an existing Q1 monster. Adds 1 encounter, 10 seeds, +10 probe entries. Trivial. Even if (a) is accepted, this is essentially free.

(d) **Reject the deletes entirely; treat the 7 STS1-only encounters as Q1-only content** that skips upstream-comparison. This contradicts the lead's 2026-05-11 directive ("no Q1-only middle ground") so listed only for completeness.

**B.1-δ recommendation:** (a) + (c). Total post-decision probe target: **140/140** (13 STS2-confirmed + 1 LouseProgenitor port). The slime DEFERs become a separate stream B.1-ε scoped to encounter-RNG plumbing once α/β/γ settle. The cost of (b) inside B.1-δ exceeds the value vs. doing it as a focused stream later.

## Port details (only one if plan (a) accepted)

### KaiserCrabBoss → KaiserCrabBoss (rename spawn)
- **Upstream encounter file:** `~/development/projects/godot/sts2/src/Core/Models/Encounters/KaiserCrabBoss.cs`
- **Upstream monster spawn list:** `Crusher` (slot `"crusher"`) + `Rocket` (slot `"rocket"`)
- **Q1 changes:**
  - Add `src/Sts2Headless.Domain/Content/Monsters/Crusher.cs` — class `Crusher : MonsterModel`, `MinHp = MaxHp = 209` (A0 from upstream `Crusher.cs:60-62`), 5-state intent rotation stub matching upstream `THRASH_MOVE → ENLARGING_STRIKE_MOVE → BUG_STING_MOVE → ADAPT_MOVE → GUARDED_STRIKE_MOVE → loop`. Use stub intents (γ pattern); powers (BackAttackLeftPower, CrabRagePower) deferred to γ-T3 as `// TODO γ`.
  - Add `src/Sts2Headless.Domain/Content/Monsters/Rocket.cs` — class `Rocket : MonsterModel`, `MinHp = MaxHp = 199` (A0), 5-state rotation `TARGETING_RETICLE_MOVE → PRECISION_BEAM_MOVE → CHARGE_UP_MOVE → LASER_MOVE → RECHARGE_MOVE → loop`. Powers (SurroundedPower, BackAttackRightPower, CrabRagePower) deferred to γ-T3.
  - Update `Phase1Encounters.KaiserCrabBoss` spawn list from `[KaiserCrab.CanonicalId]` to `[Crusher.CanonicalId, Rocket.CanonicalId]`.
  - Remove `KaiserCrab` class from `Phase1Monsters.cs` (no other encounter uses it). Verify no test references first.
  - Remove `KaiserCrab` from manifest fixture `monsters` list.
  - Add `Crusher` and `Rocket` to manifest fixture `monsters` list.
  - Update `EncounterFixtureTests.KaiserCrabBoss` expected-count from `1` to `2`.

### LouseProgenitorNormal (RECOMMENDED ADD, plan (c))
- **Upstream encounter file:** `~/development/projects/godot/sts2/src/Core/Models/Encounters/LouseProgenitorNormal.cs`
- **Upstream monster spawn list:** `LouseProgenitor` (single, no slot)
- **Q1 changes:**
  - Add `LouseProgenitorNormal` class to `Phase1Encounters.cs`. Spawn list = `[LouseProgenitor.CanonicalId]`. (Monster already exists in Q1.)
  - Register in `Phase1EncountersRegistration.RegisterAll`.
  - Add `EncounterFixtureTests` row `[InlineData("LouseProgenitorNormal", 1)]`.
  - Add 10 seeds to probe corpus.

## Delete details (7 encounters if plan (a) accepted)

For each: remove from `src/Sts2Headless.Domain/Content/Encounters/Phase1Encounters.cs`, remove from `Phase1EncountersRegistration.RegisterAll`, remove from `test/Sts2Headless.Tests.Domain/Content/EncounterFixtureTests.cs` `[Theory]` row, remove corresponding entries (10 per encounter) from `test/determinism-probe/corpus/phase1-corpus.json`, delete corresponding goldens in `test/determinism-probe/goldens-upstream/initial-state/<encounter>/` and `test/determinism-probe/goldens/perstep-<encounter>-seed*.jsonl` and `test/determinism-probe/goldens/structural-<encounter>.jsonl`.

### JawWormSolo
- Affected monster classes: `JawWorm` (drop from Phase1Monsters.cs + manifest — no other encounter uses it)

### TwoLouseNormal
- Affected monster classes: `RedLouse`, `GreenLouse` (drop both from Phase1Monsters.cs + manifest)

### LargeSlimeBoss
- Affected monster classes: `AcidSlimeL`, `SpikeSlimeL` (drop both)

### SentryTrio
- Affected monster classes: `Sentry` (drop)

### SnakePlantSolo
- Affected monster classes: `SnakePlant` (drop)

### FungalBossEncounter
- Affected monster classes: `FungalBoss` (drop)

### CenturyGuardBoss
- Affected monster classes: `CenturyGuard`, `SilverMage` (Q1's CenturyGuard rotation also references SilverMage in some places — verify; drop both if unreferenced after the encounter is gone)

## Defer details (2 encounters if plan (a) accepted)

### SmallSlimes
- **Stays registered** in `Phase1Encounters.cs` with current spawn list `[AcidSlimeS, SpikeSlimeS]`.
- **Excluded from upstream-byte-comparison** because upstream uses encounter-RNG-driven spawn.
- **Action required (separate stream):** `B.1-ε encounter-RNG plumbing` — adds `EncounterModel.Generate(IRunState, Rng)` virtual + per-encounter RNG seed derivation matching upstream `EncounterModel.cs:198`. Then `SmallSlimes` becomes a port of `SlimesWeak` (with `LeafSlimeS` / `TwigSlimeS` / `LeafSlimeM` / `TwigSlimeM` monsters added).
- **Q1 monster classes to retain (used by future port):** Q1's current `AcidSlimeS`, `SpikeSlimeS` are placeholders; B.1-ε would replace with `LeafSlimeS`, `TwigSlimeS`, `LeafSlimeM`, `TwigSlimeM`.

### MediumSlimes
- Same as SmallSlimes (architectural blocker, deferred to B.1-ε).

## Coordination notes for β / γ

- **β (content audit):** Q1's `JawWorm`, `RedLouse`, `GreenLouse`, `AcidSlimeS/M/L`, `SpikeSlimeS/M/L`, `Sentry`, `SnakePlant`, `FungalBoss`, `CenturyGuard`, `SilverMage`, `KaiserCrab` monster classes are slated for deletion (if lead accepts plan (a)). β's RC-5 audit should skip those rather than fix Min/MaxHp drift. The slimes (`AcidSlimeS/M`, `SpikeSlimeS/M`) survive only as DEFER placeholders — β may opt to leave their HP wrong since they're slated for replacement by `LeafSlime*` / `TwigSlime*` in B.1-ε.
- **γ (behavior fill-in):** intent rotations for the deleted monsters do not need to be ported. γ's per-monster rotation work should target the 12-Stream-C-confirmed monsters + (if lead accepts (a)) the 2 new Crusher/Rocket classes.

## Open questions for the lead

1. **Plan choice.** (a) Accept the documented decisions (probe target ~130-140/140), (b) push for slime encounter-RNG lift in this stream, (c) add LouseProgenitorNormal as a bonus port, (d) any combination?
2. **DEFER policy.** Per the lead's directive ("no Q1-only middle ground"), is "DEFER pending upstream architecture lift" acceptable as documented, or should SmallSlimes/MediumSlimes also be DELETE-now and re-added later?
3. **KaiserCrab monster class.** Drop entirely (recommendation), or retain as a re-named alias for one of Crusher/Rocket? Recommend drop — keeping it would propagate STS1 naming into the codebase.
4. **Corpus regeneration.** After deletes land, do we want a single corpus-regen commit, or per-encounter delete commits? The lead's prompt suggests "commit per encounter port-or-delete" — I'll honor that.
5. **Goldens regeneration.** Self-consistency goldens for the 7 deleted encounters get removed. Goldens for the surviving 13 + 2 DEFER stay. For the new KaiserCrabBoss spawn (Crusher + Rocket), goldens must be regenerated. That's Block 3's responsibility; I will not regen here.

## Status

**DONE_WITH_CONCERNS** — analysis complete, decisions drafted, re-surface trigger hit, **no destructive changes executed** pending lead's input on plan (a) / (b) / (c).

---

## B.1-final addendum (2026-05-11) — EXECUTED

Lead authorized plan (a) + (c). Finalizer executed Stages 2a/2b/2c per this doc.

**Outcomes:**
- **2a (DELETE 7):** JawWormSolo, TwoLouseNormal, LargeSlimeBoss, SentryTrio, SnakePlantSolo, FungalBossEncounter, CenturyGuardBoss — all removed from `Phase1Encounters.cs`, `EncounterFixtureTests`, `EncounterCatalog.cs` (upstream-capture), corpus, and goldens. Monster classes (JawWorm, RedLouse, GreenLouse, AcidSlimeL, SpikeSlimeL, Sentry, SnakePlant, FungalBoss, CenturyGuard, SilverMage) retained in catalog (conservative — extras pass coverage gate) since renaming/deleting them would propagate further into the manifest fixture.
- **2b (PORT KaiserCrabBoss):** Q1 `KaiserCrab` class deleted. New classes `Crusher` (HP 209, 5-state THRASH→ENLARGING_STRIKE→BUG_STING→ADAPT→GUARDED_STRIKE) + `Rocket` (HP 199, 5-state TARGETING_RETICLE→PRECISION_BEAM→CHARGE_UP→LASER→RECHARGE) ported byte-faithful from upstream `Crusher.cs` / `Rocket.cs`. Encounter `KaiserCrabBoss` spawn list `[Crusher.CanonicalId, Rocket.CanonicalId]`, slots `"crusher"` + `"rocket"`. Spawn-time powers (BackAttackLeft/Right, CrabRage, Surrounded) declared as `MonsterSpawnPower` entries (fail-soft — Q1's power catalog lacks these ids; documentation-only at Phase-1).
- **2c (ADD LouseProgenitorNormal):** New encounter spawning the existing `LouseProgenitor` monster (γ ported its rotation). 10 corpus seeds + 10 upstream-capture goldens.
- **Probe result:** `make probe-upstream-initial-state` 140/140 PASS + 20 SKIP (slimes) on 16 encounters × 10 seeds. (Previously: 110/120 PASS + 10 DIVR + 100 SKIP on 22 encounters.)
- **M-Headless gate: PASS** (initial-state probe). γ + finalizer eliminated all remaining DIVRs.

### B.1-ε deferral marker — SmallSlimes / MediumSlimes (architectural gap)

The two slime encounters remain registered in Q1 with static spawn lists (`[AcidSlimeS, SpikeSlimeS]` and `[AcidSlimeM, SpikeSlimeM]`) but are tagged `MissingUpstream` in the upstream-capture `EncounterCatalog`. Reason: upstream `SlimesWeak.GenerateMonsters` / `SlimesNormal.GenerateMonsters` use `base.Rng.NextItem(...)` and `base.Rng.NextBool` to pick spawn variants from `{LeafSlimeS, TwigSlimeS}` etc. Q1's `EncounterModel` resolves a static `IReadOnlyList<string>` spawn list — no encounter-level RNG plumbing exists.

**B.1-ε scope (deferred):**
1. Add per-encounter Rng derivation matching upstream `EncounterModel.cs:198` — `uint seed = runState.Rng.Seed + totalFloor + hash(encounter.Id)`.
2. Lift `EncounterModel.GenerateMonsters` to take an `Rng` parameter (additive virtual; existing static spawn lists keep working).
3. Add monster classes `LeafSlimeS`, `LeafSlimeM`, `TwigSlimeS`, `TwigSlimeM` (HP envelopes from upstream).
4. Replace Q1's `AcidSlimeS/M`, `SpikeSlimeS/M` placeholders with the new monsters.
5. Re-tag `SmallSlimes` / `MediumSlimes` `UpstreamComparable`; regenerate upstream goldens.

**Re-evaluation criterion:** address before P-Run training distribution requires Act-1 slime variety, or sooner if a downstream stream blocks on encounter-RNG. Estimated ~80-150 LOC + 4 new monster files; out of scope for the B.1 wave.
