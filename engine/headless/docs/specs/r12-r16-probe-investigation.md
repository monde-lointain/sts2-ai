# R12-R16 Probe False-Negative Investigation (wave-49/A.1)

> Triggered by wave-48 audit findings. R16 OPEN. Project-lead-mandated investigation per wave-48 close response (2026-05-22).
> Output drives wave-49/A.2 probe enhancement design.

## 1 — Background

Wave-46 mid-combat probe reported `10/10 PASS` on two encounters that contain active behavioral divergences vs upstream:

**LouseProgenitor** (`engine/headless/src/Sts2Headless.Domain/Content/Monsters/Phase1Monsters.cs:175`):
> Q1 `PounceDamage = 16`. Upstream A0 = 14 (`GetValueIfAscension(DeadlyEnemies, 16, 14)`). Q1 is using the DeadlyEnemies (Ascension) value. POUNCE deals 2 extra damage per turn vs upstream — active behavioral divergence. (Quoted verbatim from §24 of q1-substrate-stub-audit.md.)

**Exoskeleton** (`engine/headless/src/Sts2Headless.Domain/Content/Monsters/Phase1Monsters.cs:75`):
> Per-slot initial-move (ConditionalBranchState "INIT_MOVE"): Q1 always starts SKITTER; ExoskeletonsNormal spawns exactly slots first/second/third so ENRAGE-first and MANDIBLES-first paths are unreachable in current encounters. (Quoted verbatim from §4 of q1-substrate-stub-audit.md.)

The upstream `Exoskeleton.GenerateMoveStateMachine()` constructs a `ConditionalBranchState("INIT_MOVE")` that routes:
- `Creature.SlotName == "first"` → SKITTER_MOVE
- `Creature.SlotName == "second"` → MANDIBLES_MOVE
- `Creature.SlotName == "third"` → ENRAGE_MOVE
- `Creature.SlotName == "fourth"` → RAND

The state machine initial state is this conditional branch, not SKITTER_MOVE directly.

## 2 — Test methodology

All four hypotheses tested by code-level inspection of the probe pipeline rather than live execution. The probe's three constituent files were read in full:

- `test/determinism-probe/src/Program.cs` — mode dispatch, capture vs compare logic, `RunMidCombat()`
- `test/determinism-probe/src/Q1MidCombatCaptureDriver.cs` — Q1-side capture loop
- `test/determinism-probe/src/MidCombatComparer.cs` — diff-summary logic
- `test/determinism-probe/src/MidCombatRecord.cs` — field-set and wire format

Plus:
- `test/determinism-probe-upstream-capture/src/UpstreamDriver.cs` — `CaptureMidCombat()` (upstream-side)
- `engine/headless/Makefile` — make targets and their `--mode` flags
- `test/determinism-probe-upstream-capture/src/EncounterCatalog.cs` — spawn plans and slot values
- Both action-sequence JSON files
- Upstream `LouseProgenitor.cs` and `Exoskeleton.cs` source

H-A was tested by reading the action sequences and computing cumulative damage manually. H-B was tested by reading `MidCombatRecord` field definitions and the comparer loop. H-C was tested by reading the driver capture-timing code. H-D was tested by tracing the full execution path from make target through `Program.cs` to the driver.

## 3 — Hypothesis H-A: Action-sequence depth

**Verdict: NOT the root cause. Sequence depth is adequate — POUNCE fires twice (turns 3 and 6). But depth is irrelevant given H-D's finding.**

LouseProgenitor (HP 134–136). The action sequence plays 2 × StrikeSilent (6 dmg each = 12/turn) on attacking turns. Across 7 turns: ~12 × 5 attack turns = 60 damage. LouseProgenitor has 134–136 HP and CURL_AND_GROW gives it 14+ block plus Strength +5 per cycle. The monster is NOT killed before turn 3 or turn 6. POUNCE fires at turn 3-enemy-end and turn 6-enemy-end. Player HP at those snapshots would reflect different values depending on whether POUNCE deals 16 (Q1) or 14 (upstream A0) — AFTER accounting for CURL_AND_GROW's Strength +5 applying to POUNCE too (Q1: 16+5=21; upstream: 14+5=19 after first CURL).

ExoskeletonsNormal: 3× Exoskeleton at HP 24–28. Turn 1 each takes 1 StrikeSilent hit (6 dmg) to exo1. Exo1 HP ~18–22 after turn 1 — still alive. SKITTER fires from all three slots. Sequence depth (12 turns) covers multiple full SKITTER→MANDIBLES→ENRAGE→RAND cycles. Slot-1+2 divergences would manifest from turn 1 onward IF the initial-move divergence were real in the driver.

Depth is not the problem. The sequence is long enough to exercise the divergences multiple times. However depth is moot once H-D is established.

## 4 — Hypothesis H-B: Field-set encoding

**Verdict: NOT the root cause independently, but contains a secondary gap. Field-set encoding is structurally correct — `MidCombatRecord` captures `MoveId`, `IntentDamagePerHit`, `IntentHitCount`, and `PlayerHp` exactly as needed to detect both divergences. However, the golden was not captured from upstream, so correct encoding never had a chance to catch anything.**

`EnemySnapshot` carries:
- `MoveId` (string) — the state-machine cursor. Would differ: Q1 exo[1] `MoveId="SKITTER_MOVE"` vs upstream exo[1] `MoveId="MANDIBLES_MOVE"` if upstream actually ran with slot names.
- `IntentDamagePerHit` (int) — would differ for POUNCE: Q1=16 vs upstream=14.
- `IntentHitCount` (int) — correctly captures multi-attack vs single.

`MidCombatComparer.BuildDiffSummary()` compares `Enemy[i].MoveId` and `Enemy[i].Intent.DmgPerHit` field-by-field. These are the exactly correct fields. If upstream goldens were present, the divergences would be caught.

**Secondary gap identified**: `MoveId` for the upstream Exoskeleton case depends on `GetProperty(ec, "MonsterState")?.CurrentMoveId` in `UpstreamDriver.SnapshotUpstream()`. If `MonsterState` does not exist as a property on the upstream creature type (reflection miss), `moveId` stays `""` and the divergence on `MoveId` would be masked. This is a robustness concern for `UpstreamDriver` that should be verified during A.2 implementation. A secondary fix: if `moveId == ""` after reflection, treat as an error rather than silently using empty string.

## 5 — Hypothesis H-C: Capture-timing offset

**Verdict: NOT the root cause. Capture timing is correct for the divergences in question.**

`Q1MidCombatCaptureDriver.Capture()` emits snapshots at:
1. `"player-pre"` — after `CombatEngine.StartPlayerTurn()`, before card play
2. `"player-end"` — after `CombatEngine.EndPlayerTurn()`
3. `"enemy-end"` — after `CombatEngine.EnemyTurn()` + `CheckCombatEnd()`

For LouseProgenitor POUNCE damage: the damage is applied during `EnemyTurn()`. The `"enemy-end"` snapshot captures `PlayerHp` AFTER POUNCE resolves. This is the correct checkpoint for detecting a per-hit damage value error.

For Exoskeleton initial move: `"player-pre"` captures `Enemy[i].MoveId` at the START of the player turn, which is the enemy's currently-queued next move. Turn 1 `"player-pre"` would show the initial move (SKITTER vs MANDIBLES vs ENRAGE by slot). Turn 1 `"enemy-end"` shows the follow-up move queued after the initial move executed.

Both divergences are fully visible at existing capture checkpoints. Timing is not a gap. An alternative approach (capturing `MoveId` at turn-start explicitly as "intended next move") is equivalent to `"player-pre"` and would not change the verdict.

## 6 — Hypothesis H-D: Driver semantic mismatch

**Verdict: PRIMARY ROOT CAUSE. The mid-combat golden files are NOT upstream-derived. They are generated from `Q1MidCombatCaptureDriver` and compared against `Q1MidCombatCaptureDriver`. The probe is a Q1 self-consistency check, not a Q1-vs-upstream comparison.**

Evidence chain:

**1. The `make probe-upstream-mid-combat-capture` target:**

```makefile
probe-upstream-mid-combat-capture: probe-build
    dotnet run --project test/determinism-probe -c Debug --no-build -- \
      --mode mid-combat-capture
```

This uses `--project test/determinism-probe` (the Q1-side probe), not `test/determinism-probe-upstream-capture` (the upstream capture project). `UpstreamDriver.CaptureMidCombat()` is never invoked by this target.

**2. `Program.RunMidCombat()` (lines 405–631):**

```csharp
// Both modes use the Q1MidCombatCaptureDriver (parallel system to per-step probe;
// no UpstreamDriver involvement here since the upstream capture runs separately
// via probe-upstream-mid-combat-capture target).
var driver = new Q1MidCombatCaptureDriver();
var comparer = new MidCombatComparer(goldensRoot);
// ...
q1Records = driver.Capture(seed, encId, plan);
// ...
MidCombatRecord.WriteFile(goldenPath, q1Records);  // capture mode: Q1 → golden
// vs
comparer.CompareOne(encId, seed, q1Records);       // compare mode: Q1 → vs Q1-golden
```

In capture mode: Q1 output → golden file.
In compare mode: Q1 output → compared against golden file (which contains Q1 output from prior capture).
Both sides of the comparison are Q1. `UpstreamDriver.CaptureMidCombat()` exists in code but is wired to nothing in the make pipeline for mid-combat mode.

**3. `UpstreamDriver.CaptureMidCombat()` additionally skips card play:**

```csharp
// For wave-1 cultist (no actual card-play needed for data-path parity check),
// we skip card play on upstream side — intent/HP comparison is the gate.
// Card-play reflection is complex...
// Document: this is a known simplification for wave-1; per-action smoke is opt-in.
```

Even if `UpstreamDriver.CaptureMidCombat()` were wired into the compare pipeline today, it would produce wrong player HP/block values because it does not execute card plays. Q1 driver does execute cards. PlayerHp and PlayerBlock would diverge from turn 1 for every encounter that plays a DefendSilent. The upstream driver needs the card-play gap closed before it can serve as the golden source.

**4. The `goldens-upstream/mid-combat/` directory naming is misleading:**

The directory is named `goldens-upstream` but for the mid-combat sub-directory, the goldens were established from Q1, not from upstream. The `goldens-upstream/initial-state/` sub-directory IS upstream-derived (via `make probe-upstream-capture` → `UpstreamDriver.Capture()`). The `mid-combat/` sub-directory is Q1-derived. The naming suggests upstream origin but the content is Q1-self-captured.

**Consequence**: Every Q1 behavioral bug present when the goldens were captured is baked into the golden. Q1 runs with `PounceDamage=16` → golden contains `IntentDamagePerHit=16` for POUNCE. Q1 runs with all exoskeletons starting SKITTER → golden contains `MoveId="SKITTER_MOVE"` for all slots at turn-1-player-pre. Re-running Q1 with the same bugs produces identical snapshots. The comparer reports PASS because both sides reflect the same (incorrect) Q1 behavior.

**`UpstreamDriver.CaptureMidCombat()` is effectively dead code in the probe pipeline as of wave-46.**

## 7 — Root cause identified

**The mid-combat probe false-negative is caused by a single architectural gap: the mid-combat golden files are generated from the Q1-side driver, not from upstream.**

The `make probe-upstream-mid-combat-capture` target uses `--project test/determinism-probe` with `--mode mid-combat-capture`, routing through `Q1MidCombatCaptureDriver.Capture()`. `UpstreamDriver.CaptureMidCombat()` was implemented in wave-45 but never wired into the make capture target. The golden files in `goldens-upstream/mid-combat/` encode Q1's behavior at capture time. Subsequent compare runs compare Q1-current against Q1-historical. Any bug present at both capture time and compare time produces a PASS.

H-A (depth) and H-C (timing) are not issues — sequence and capture points are adequate. H-B (field encoding) is structurally correct but irrelevant while goldens are Q1-derived. H-D (driver mismatch) is the sole root cause; additionally, `UpstreamDriver.CaptureMidCombat()` has a card-play gap and the ExoskeletonsNormal spawn plan has a null-slot gap that must both be closed before upstream-derived goldens can serve as the comparison baseline.

**Summary**: two fixes needed for R16 discharge:
1. Wire `UpstreamDriver.CaptureMidCombat()` into `make probe-upstream-mid-combat-capture` (primary fix, closes the architectural gap).
2. Close `UpstreamDriver.CaptureMidCombat()` card-play gap + ExoskeletonsNormal null-slot gap (required for fix 1 to produce correct goldens).

## 8 — Probe enhancement spec for A.2

### 8.1 — Wire upstream capture into the golden pipeline (PRIMARY)

**File: `engine/headless/Makefile`** — add or replace `probe-upstream-mid-combat-capture` target to invoke the upstream-capture project, not the Q1-side probe:

```makefile
probe-upstream-mid-combat-capture: probe-build
    @echo "==> probe-upstream-mid-combat-capture (upstream-derived mid-combat goldens via sts2.dll)"
    dotnet build test/determinism-probe-upstream-capture/Sts2Headless.UpstreamCapture.csproj -c Debug
    dotnet run --project test/determinism-probe-upstream-capture --no-build -c Debug -- \
      --mode mid-combat-capture
```

**File: `test/determinism-probe-upstream-capture/src/Program.cs`** — add `--mode mid-combat-capture` dispatch that:
1. Iterates all encounters in `EncounterCatalog` that have `MidCombatActionSequenceId` set.
2. Loads the action plan JSON.
3. Calls `UpstreamDriver.CaptureMidCombat(seed, plan, actionPlan)` for each seed.
4. Writes `MidCombatRecord.WriteFile(goldenPath, records)` to `goldens-upstream/mid-combat/{encounter}/{seed}.bin`.

### 8.2 — Close card-play gap in `UpstreamDriver.CaptureMidCombat()` (REQUIRED)

**File: `test/determinism-probe-upstream-capture/src/UpstreamDriver.cs`** — implement card-play reflection in the per-turn loop (currently a no-op at lines 1240–1252).

Minimum required for the two known divergences:

- **LouseProgenitor**: player HP at `"enemy-end"` captures the POUNCE-damage divergence. Player block from DefendSilent reduces the net damage. Without card-play: upstream player takes full 14 dmg, Q1 player takes 16-5=11 dmg → PlayerHp diverges even with no substrate fix. Card-play must be implemented so both sides execute DefendSilent and the HP comparison isolates the POUNCE-damage difference.

- **ExoskeletonsNormal**: `"player-pre"` at turn 1 captures `Enemy[1].MoveId`. No card-play is needed for this comparison since it's a pre-player-action snapshot. Card-play still matters for turns 2+ where PlayerHp diverges without it.

Recommended implementation path:
1. After upstream `StartTurn` runs, locate the player's hand via `PlayerCombatState.Hand.Cards` (reflection).
2. For each action where `!action.EndTurn && action.CardId != null`: find card instance in hand by `CardModel.Id` match; invoke `CombatManager.PlayCard(cardInstance, target)` via reflection (or equivalent internal method). Skip gracefully if card not found (same behavior as Q1 driver).
3. After all actions, invoke `EndPlayerTurn` (already done at line 1254).

A simplified approximation (if full reflection is too risky for A.2 scope): for `StrikeSilent`, compute damage with Weak/Strength powers applied and directly subtract from target creature HP. For `DefendSilent`, add block to player creature. This avoids CombatManager.PlayCard reflection but is sufficient for the PlayerHp/PlayerBlock parity needed by `MidCombatComparer`.

### 8.3 — Fix ExoskeletonsNormal null-slot gap (REQUIRED for slot-1+2 divergence)

**File: `test/determinism-probe-upstream-capture/src/EncounterCatalog.cs`** — change ExoskeletonsNormal slots from `null/null/null` to `"first"/"second"/"third"`:

```csharp
"ExoskeletonsNormal" => new EncounterPlan(
    id,
    new[] { "Exoskeleton", "Exoskeleton", "Exoskeleton" },
    new string?[] { "first", "second", "third" },   // was: null, null, null
    PlanKind.UpstreamComparable,
    null,
    MidCombatActionSequenceId: "exoskeletons-normal-strategy.json"
),
```

Without this fix, upstream's `ConditionalBranchState("INIT_MOVE")` evaluates `Creature.SlotName == "first"` against `null`; all predicates fail; the upstream state machine falls through to an implementation-defined default. Both Q1 and upstream would start at a similar undefined state, masking the slot-1+2 divergence.

### 8.4 — Fix `MonsterState` reflection robustness in `UpstreamDriver.SnapshotUpstream()`

**File: `test/determinism-probe-upstream-capture/src/UpstreamDriver.cs`** — at line 1356–1361, add fallback and empty-string guard:

```csharp
object? monsterState = GetProperty(ec, "MonsterState") ?? GetProperty(ec, "State");
if (monsterState is not null)
{
    object? currentMoveId = GetProperty(monsterState, "CurrentMoveId")
        ?? GetProperty(monsterState, "MoveId")
        ?? GetProperty(monsterState, "CurrentState");  // additional fallback
    moveId = currentMoveId?.ToString() ?? "";
}
// If moveId is still "" after all fallbacks, log a warning so silent misses are surfaced.
if (moveId == "" && monsterState is not null)
    Console.Error.WriteLine($"warn: SnapshotUpstream: could not resolve MoveId for enemy at turn {turn} side {side}");
```

### 8.5 — Golden re-capture scope

After fixes 8.1–8.4 are implemented, run:

```
make probe-upstream-mid-combat-capture
```

This regenerates all goldens in `goldens-upstream/mid-combat/` from upstream. The commit that lands the new goldens is the canonical pin for R16 verification. All 90 goldens (9 encounters with action sequences × 10 seeds) will change content; binary diff is expected and correct.

No wire-format schema bump needed unless new fields are added to `MidCombatRecord`. If field additions are required, bump `FileMagic` per ADR-001.

### 8.6 — Summary of file changes for A.2

| File | Change |
|---|---|
| `engine/headless/Makefile` | Replace `probe-upstream-mid-combat-capture` target to invoke upstream-capture project |
| `test/determinism-probe-upstream-capture/src/Program.cs` | Add `--mode mid-combat-capture` dispatch |
| `test/determinism-probe-upstream-capture/src/UpstreamDriver.cs` | Implement card-play + robustify `MonsterState` reflection |
| `test/determinism-probe-upstream-capture/src/EncounterCatalog.cs` | Fix ExoskeletonsNormal slots to `"first"/"second"/"third"` |
| `goldens-upstream/mid-combat/**/*.bin` | Re-capture from upstream (all encounters × 10 seeds; ~90 files) |

## 9 — Validation plan for A.4 red-team

After A.2 (probe enhancement) and A.3 (substrate fixes) are merged, A.4 validates that the enhanced probe detects the two sub-findings as injections.

### 9.1 — LouseProgenitor PounceDamage regression injection

**Pre-condition**: A.3 has fixed `PounceDamage = 14` in `Phase1Monsters.cs`. Upstream golden for LouseProgenitorNormal encodes `IntentDamagePerHit=14` for POUNCE at `"player-pre"` on POUNCE turns.

**Injection**: set `PounceDamage = 17` in `Phase1Monsters.LouseProgenitor` (different from both A0=14 and pre-fix Q1=16).

**Expected probe output**: at turn-3-player-pre, `Enemy[0](LouseProgenitor).Intent.DmgPerHit` = 17 in Q1 vs 14 in golden. `MidCombatComparer` reports:
```
encounter=LouseProgenitorNormal seed=42 turn=3 side=player-pre field=Enemy[0](LouseProgenitor).Intent.DmgPerHit q1=17 golden=14
```
Exit code = 1 (FAIL). Enhanced probe MUST report DIVERGED.

**Revert**: `git checkout HEAD -- engine/headless/src/Sts2Headless.Domain/Content/Monsters/Phase1Monsters.cs`

### 9.2 — Exoskeleton slot-1 INIT deletion regression injection

**Pre-condition**: A.3 has fixed `ExoskeletonsNormal` to use per-slot initial-move overrides (slot 0→SKITTER, 1→MANDIBLES, 2→ENRAGE). Upstream golden encodes `Enemy[1](Exoskeleton).MoveId="MANDIBLES_MOVE"` at turn-1-player-pre.

**Injection**: collapse slot-1's initial move back to SKITTER (pre-fix Q1 behavior). In Q1's `ExoskeletonsNormal.GenerateMonstersWithMoves()` (or equivalent), change slot-1 override from `Exoskeleton.MandiblesMoveId` back to `Exoskeleton.SkitterMoveId`.

**Expected probe output**: at turn-1-player-pre, `Enemy[1](Exoskeleton).MoveId` = "SKITTER_MOVE" in Q1 vs "MANDIBLES_MOVE" in golden. `MidCombatComparer` reports:
```
encounter=ExoskeletonsNormal seed=42 turn=1 side=player-pre field=Enemy[1](Exoskeleton).MoveId q1=SKITTER_MOVE golden=MANDIBLES_MOVE
```
Exit code = 1 (FAIL). Enhanced probe MUST report DIVERGED.

**Revert**: `git checkout HEAD -- engine/headless/src/Sts2Headless.Domain/Content/Encounters/Phase1Encounters.cs`

### 9.3 — Full corpus regression gate

After both injections pass and are reverted, run full corpus:

```
make probe-upstream-mid-combat
```

Expected: all encounters PASS (0 diverged, 0 errored). Confirms A.3 substrate fixes are correct AND A.2 enhancement does not introduce false positives.

### 9.4 — Failure criteria (R16 NOT discharged)

If either injection fails to produce DIVERGED:

- Check that golden re-capture (8.5) was run AFTER A.2 changes — if goldens are still Q1-derived, A.4 fails for the same reason as the original false-negative.
- Check that ExoskeletonsNormal slots are `"first"/"second"/"third"` in `EncounterCatalog.cs` (8.3).
- Check that `UpstreamDriver.CaptureMidCombat()` card-play is implemented (8.2) — missing card-play causes PlayerHp divergence that may hit before `MoveId`/`DmgPerHit` in the comparer's ordered field check, but this should surface as a DIVERGED on a different field, not as a false PASS. Verify verbose comparer output identifies the correct field.
- If comparer reports DIVERGED on the wrong field (e.g., PlayerHp instead of DmgPerHit), consider adding `"player-pre"` snapshot comparison for intents only — intents are set at turn-start before any card-play and are unaffected by the card-play gap.
