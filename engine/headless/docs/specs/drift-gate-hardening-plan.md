---
quantum: Q1
substrate: engine/headless/
related-quanta: Q4
status: PLANNED (architect-draft; pre-execution)
---

> Status legend per ADR-023. All sections badged `[PLANNED]` or `[ASPIRATION]`
> in this draft; engineer waves promote to `[SHIPPED]` per gate completion.

# Drift-Gate Hardening Plan — Phase-1.5 Prerequisite

> Origin: project-lead commission 2026-05-21 following ADR-034 R12 carve-out
> (mid-combat behavioral drift undetected by `SyncStatePinGate` +
> `DllSignatureGate` + `probe-upstream-initial-state`).
>
> Scope: design the gate set that must ship before Phase-1.5 encounter ports
> resume. Out of scope: implementation, resurrection of headless/Godot-headless
> approaches (closed by ADR-034 / Wave-43 spike).

## 0. Why this plan exists `[PLANNED]`

The Q1 substrate is a behavior-mirroring parallel C# reimplementation (ADR-034).
Three structural drift gates exist today:

| Gate | Substrate plane | Detects | Misses |
|---|---|---|---|
| `SyncStatePinGate` | content baseline file | pin vs sync-state buildid mismatch | semantic behavior |
| `DllSignatureGate` | reflection-target shape | upstream signature drift (rename/arity) | per-method behavior change |
| `probe-upstream-initial-state` | post-SetUpCombat byte snapshot | start-of-combat state divergence | post-turn-N divergence |

R12 names the residual: an upstream `OnPlay` body, monster move payload, or
power-hook trigger that mutates damage/block/power-stack values without changing
signature shape, file-set, or initial state. Wave-32 (ADR-032) is a concrete
example of this class — `Defend(N)` was silently dropped pre-wave for a year
under the prior `ExtractAttackSelfBlock` switch, with no structural gate firing.

Until R12 is hardened, every Phase-1.5 encounter port silently accumulates risk:
the port engineer reads upstream by eye, transcribes intent, and the regression
net is the engineer's care plus the **convention** in ADR-026 §Negative concern
#1 ("paste upstream code as inline comments"). A convention is not a gate.

## 1. Resolved known-unknowns (load-bearing factual decisions) `[PLANNED]`

These are the questions the commission flagged as load-bearing. Each carries
explicit rationale; project-lead may override but cannot defer.

### 1.1 `CombatState.GodotTimerTask` does NOT block mid-combat reflection-snapshot

Read of upstream `Core/Combat/CombatState.cs:211-244, 455-459`:
`GodotTimerTask` is invoked **only** from `GetCreatureAsync(combatId, timeoutSec)`
— a helper used to wait on a creature that has not yet spawned. It is not in
the `SetUpCombat → StartTurn → EndTurn → ExecuteEnemyTurn → AfterAllPlayersReadyToEndTurn`
critical path. The wave-43 spike report's "FAIL by proof-of-absence" referred
to a different blocker — `SaveManager`/scene-tree-gated singletons in the **save/restore**
path (no upstream serialization primitive for live `CombatState`), not to
`GodotTimerTask` per se.

**Side-step pattern is identical to the existing wave-6 SetUpCombat shim:**
`UpstreamDriver.cs:514-607` already manually replays the SetUpCombat body
(`L195–L215`) bypassing `NetCombatCardDb.Instance.StartCombat` (L590-592
skipped). The same pattern applies for `StartCombatInternal → StartTurn →
ExecuteEnemyTurn`:
- skip the audio/banner/`Cmd.CustomScaledWait(...)` lines (visual pacing);
- skip the `NCombatRoom.Instance?.AddChildSafely(...)` scene-tree adds;
- skip `RunManager.Instance.ActionExecutor` action-queue pumping (we already
  manually drove state in wave-6);
- skip `RunManager.Instance.ActionQueueSynchronizer.SetCombatState(...)` (no
  multiplayer net path);
- skip `NRunMusicController.Instance?.PlayCustomMusic(...)`;
- drive the hook-call chain (`Hook.BeforeSideTurnStart`, `Hook.AfterBlockCleared`,
  `Hook.BeforeTurnEnd`, `Hook.AfterAutoPostPlayPhaseEntered`) directly via
  reflection;
- read post-turn state from `_state` exactly as wave-6 does at L605-609.

The **only** new code-paths are pure-data hook dispatchers. None of them touch
`GodotTimerTask`. **Decision**: the mid-combat probe is feasible; ADR-034 §Future
work's "if R11 ESCALATES, drift-gate hardening becomes prerequisite" condition
is satisfied by this plan.

*Lead-with-negatives*: this binds R12 mitigation to the reflection-harness's
continued tractability. A future upstream patch that introduces a `GodotTimerTask`
call inside the EndTurn critical path (e.g., a "wait-for-animation" inline
gate) would break the harness with a SIGSEGV. Mitigation: the `DllSignatureGate`
already monitors reflection-target shape; widen it to alert on new `GodotTimerTask`
call-sites reachable from the EndTurn chain (scope §1 sub-stream A.2). Cost
of the alert is one signature check per gate run (negligible).

### 1.2 Per-encounter parity granularity is per-turn + per-action-side, NOT per-action

Three candidates:

| Granularity | Storage / golden / encounter / seed | Diagnosis power | Cost |
|---|---|---|---|
| Terminal-only | 1 snapshot | Names encounter; cannot localize move | ~free |
| Per-turn (player+enemy collapsed) | ≤20 snapshots (cultist solves ≤20 rounds; budget per ADR-031 Appendix A `Round: [0, 256)`) | Names turn at which divergence appears | small |
| Per-action (each card play, each enemy intent application) | ≤200 snapshots | Names exact card/intent | medium |

*Lead-with-negatives*: per-action storage compounds fast. 22 encounters × 10
seeds × ≤200 actions × ~200 bytes/snapshot ≈ 8.8 MB **per probe corpus** ×
running both Q1 and upstream sides ≈ 17.6 MB. Goldens live in git per the
`probe-upstream-initial-state` precedent — non-trivial repo growth and merge
friction.

Terminal-only is rejected: when SmallSlimes diverges on turn 4 because of an
unported `kSpitSmall` Status payload (the Wave-32 ADR-032 class of bug), terminal
HP may still match if the same total damage is dealt elsewhere. The point of
mid-combat probing is to localize the divergence.

Per-action is rejected as wave-1 floor. Engineers will not pay 8.8 MB to learn
"Cultist `kIncantation` ritual-stack +2 vs upstream's +5 on turn 3" — turn-level
already names the move.

**Decision**: per-turn-side at wave-1 (one snapshot post-player-EndTurn, one
post-enemy-HandleEnemyTurn, per turn). Per-action is opt-in for encounters that
exhibit per-turn divergence under triage. Two snapshot points per round × ≤20
rounds × ≤22 encounters × ≤10 seeds × ~200 bytes ≈ 1.76 MB per side ≈ 3.5 MB
total — under the existing `goldens-upstream/initial-state/` 2.2 MB precedent
by less than 2×.

### 1.3 Roslyn analyzer for the inline-upstream linter; reject grep-based pre-commit hook

*Lead-with-negatives on Roslyn*:
- requires a new analyzer project under `engine/headless/test/` (modeled on the
  existing `Sts2Headless.AnalyzerTripwire/` precedent);
- engineer who writes a new monster/card port without the inline comment sees
  an analyzer warning, not an error initially (warn → block per ADR-024 §promotion
  precedent);
- **false-negative on diff**: the analyzer can detect "does this method body
  have a `// upstream:` comment block?" but cannot detect "is the comment block
  *actually current* relative to the upstream source." An engineer who copies
  a stale inline comment from a related monster passes the check.

*Lead-with-negatives on grep-based pre-commit hook*:
- mirrors ADR-024's `spec-edit-tracker.py` pattern but inverts the polarity
  (positive presence rather than negative absence);
- runs only on the developer's machine if registered (`.claude/settings.local.json`
  precedent — gitignored, never enforces for teammates without their own
  registration);
- regex semantics on C# source are fragile across multi-line comment blocks,
  raw strings, file-scoped vs traditional namespaces.

**Decision**: Roslyn analyzer. The false-negative on stale comments is mitigated
in scope item 4 below by extending the lint to require a structured `// upstream-source:`
path token that the analyzer verifies exists in upstream and contains an SHA
fragment matching a recent upstream-pin commit. This converts "stale comment"
from a silent failure into an analyzer error.

The grep-based pre-commit hook is rejected because it does not run for teammates
who have not registered their hooks, which exactly the engineers most likely
to introduce stale inline comments (new contributors). The analyzer fires
during `dotnet build` for everyone.

### 1.4 Q4-side semantic DSL coherence populates via upstream-source AST extraction, NOT hand-port

*Lead-with-negatives on AST extraction*:
- requires a Roslyn-backed source walker over `~/development/projects/godot/sts2/src/Core/Models/Cards/`;
- each card's `OnPlay` body is **arbitrary** C# code (await chains, condition
  branches, dynamic-var reads, command builders). A perfect AST → DSL mapping
  is infeasible. We extract the **subset of card semantics expressible in the
  registry DSL** (`{op, base, target}` triples per scope item 3).
- non-mapped semantics emit `{op: "unknown", source-line: N}` rather than the
  current `{op: "stub", source: "phase1-seed"}`. The DSL is honest about its
  coverage gap.
- cards that genuinely do not fit the DSL (e.g., choose-N, X-cost, dynamic
  upgrade) emit a `dsl_coverage: false` flag; Q4-coherence gate counts these
  but does not fail on them.

*Lead-with-negatives on hand-port*:
- 98 cards × ~5 min/card ≈ 8 person-hours **per upstream patch**. R11 says
  per-patch port treadmill is the operating mode (~1-3 weeks/major patch) —
  adding 8 hours to that is a 5% tax. Multiplied by ~10 patches/year ≈ 80
  hours/year on hand-DSL alone.
- hand-port is what the current `seed_phase1_registry.py:69-79` does — and it
  has been frozen at `{op: "stub"}` since Q4 boot because no engineer wants
  to pay the 8 hours.

**Decision**: AST extraction. The semantic loss vs hand-port is bounded
(unknowns are flagged, not silently mistyped). The maintenance win is dominant.
Wave-2 sub-stream Q4-A2 owns the extractor; engineer dispatch tooling
(`tools/upstream-sync/src/upstream_sync/prompt_generator.py`) gains a card-DSL
prompt template referencing the extractor.

### 1.5 Goldens storage strategy: file-per-encounter-per-seed, per-turn-side rows inside

Two extremes:
- file-per-turn-snapshot: `goldens/mid-combat/{encounter}/{seed}/turn-{N}-{side}.bin` —
  for cultist-normal 10 seeds × 5 rounds × 2 sides = 100 files/encounter ×
  22 encounters = 2200 files. Storage is git-friendly (tiny files diff cleanly)
  but ls/walk cost compounds.
- compressed-batch: one `.gz` per encounter — opaque to git diff, requires
  decompress to inspect.

**Decision**: file-per-encounter-per-seed, with the file as a sequence of
length-prefixed per-turn-side records. `goldens-upstream/mid-combat/{encounter}/{seed}.bin`.
Diff at the binary level is record-by-record; failure summaries (per
`UpstreamInitialStateComparer.cs:406-473` precedent) report `turn N, side X,
field Y` rather than file paths.

*Lead-with-negatives*: a single corrupt golden file invalidates all turn-side
snapshots for that encounter/seed; per-turn files would limit blast radius.
Mitigation: the byte-prefix header includes a per-turn CRC32; `BuildDiffSummary`
fails the **first** divergent turn-side rather than continuing to compare
downstream turns (which would propagate noise from the actual divergence point).
This matches how `UpstreamInitialStateComparer` already short-circuits on length
mismatch.

## 2. Gate set — sub-stream specifications

Six sub-streams (numbered to match commission scope §1-6). Each specifies wall-clock
budget, where it runs, and red-team validation.

### 2.1 Mid-combat behavioral probe `[PLANNED]`

**Sub-stream Q1-A1** — extend `UpstreamDriver` with `CaptureMidCombat(seed, plan, maxTurns)`
that drives the per-turn loop:

```
SetUpCombat (existing manual replay, L514-607)
→ snapshot(turn=0, side=initial) [matches existing initial-state probe]
→ StartTurn (manual replay of CombatManager.cs:259-432 minus visual / scene-tree
              / ActionExecutor lines; see §1.1 above for the exact skip list)
→ snapshot(turn=N, side=player-pre-action)
→ for each scripted player action in the fixed-action sequence:
    → invoke CardPlayer.PlayCard via reflection (Q1's CombatEngine.PlayerPlayCard
      equivalent on upstream-side)
    → snapshot(turn=N, side=after-action-{idx}) [opt-in per encounter, scope §1.2]
→ SetReadyToEndTurn(player, canBackOut: false)
→ drive AfterAllPlayersReadyToEndTurn → EndPlayerTurnPhaseOneInternal
→ snapshot(turn=N, side=player-end)
→ drive ExecuteEnemyTurn
→ snapshot(turn=N, side=enemy-end)
→ check CheckWinCondition (upstream) / IsCombatEnded (Q1)
→ repeat until terminal or turn==maxTurns (budget cap)
```

**Field set diffed** — extend `UpstreamInitialStateComparer.cs:38-39` with:
- existing: `TurnCounter, Phase, Player(HP/Block/Powers), Enemies[](HP/Block/Powers), Energy, pile-counts`;
- new for mid-combat: **per-power-stack values** (e.g., `Ritual:2, Strength:5`),
  **monster intent** (`(MoveId, DamagePerHit, HitCount, AppliesPowers, SelfBlockGain)`
  per ADR-032's v4 wire layout), **monster move-table position** (`current_move_index`
  in MonsterIntent), **RNG counter** (`Player.RngCounter.ForShuffle.Counter`,
  `Monster.RngCounter.Counter` — needed because divergence in RNG plumbing is
  invisible at pile-counts alone; example: SmallSlimes spawn list).

*Lead-with-negatives*:
- **Field-set expansion ≈ 4× the per-snapshot bytes vs initial-state.** Per-turn
  diff is now ~400 bytes (was ~100 for initial-state). Budget §1.2 above
  accounts for this.
- **RNG-counter visibility couples this gate to Q1's `RngCounter` schema.** A
  future Q1 refactor of RNG-counter semantics (not currently planned) would
  require a goldens re-capture. Mitigation: probe emits a schema-version byte
  prefix; engine breaks legible at gate-run time.
- **Monster intent shape is recently-ADR-032-bumped (StateCodec v4).** This
  plan inherits that wire format; if a future ADR-035+ bumps it again the
  mid-combat goldens migrate per ADR-032's `probe-capture` precedent (no
  separate migration story).

**Encounters covered at wave-1**:
- `CultistsNormal` — regression-locked baseline (golden frozen at v0.105.1
  pin; any future Q1 substrate change that mutates cultist behavior fails the
  gate). This is the equivalent of `cultist_zobrist_pin.h` for behavioral
  ground-truth.
- Per-encounter coverage extends per Phase-1.5 wave as encounters port:
  Wave-N+1 of any Phase-1.5 encounter port MUST land a mid-combat golden for
  the newly-ported encounter as part of the same wave.

**Wall-clock budget**: target ≤90s for cultist-only on `make q1-ci` fast lane;
≤8 min on nightly for the full corpus (22 encounters × 10 seeds × ≤20 turns
× ~2ms reflection-invocation cost per snapshot = 528s ≈ 8.8 min). Existing
`probe-upstream-initial-state` runs ~5 min at 220 entries; mid-combat at 4400
turn-snapshots is ~4× cost.

**Where it runs**:
- `make q1-ci` (≤60s commit): cultist-only smoke (1 encounter × 1 seed × full
  turn loop) — budget ~8s. Catches local regressions before push.
- New target `make probe-upstream-mid-combat` (nightly + pre-merge for any
  PR touching `engine/headless/src/Sts2Headless.Domain/`): full corpus.
- Existing `make probe-upstream-initial-state` (5 min, pre-merge) stays as-is
  — orthogonal coverage.

**Red-team validation** (scope §5):
1. On a throwaway branch, modify `CalcifiedCultist.cs` to make `kIncantation`
   apply `Ritual+3` instead of `Ritual+2`.
2. Run `make probe-upstream-mid-combat`.
3. Confirm: gate FAILS, report names `encounter=CultistsNormal seed=42 turn=2
   side=enemy-end field=Enemy[0].Powers[Ritual].Stacks q1=3 golden=2`.
4. Confirm: failure message does NOT name the file (`CalcifiedCultist.cs`) —
   the gate sees behavior, not source. Engineer follows the divergence to source
   manually. This is intentional; rule-of-three is: gate localizes to (encounter,
   turn, field), engineer localizes to (file, line) via inline-comment lookup.

If the gate does not fire on the +1 Ritual stack injection, the gate is broken
and Phase-1.5 stays paused.

### 2.2 Per-encounter behavioral parity tests `[PLANNED]`

**Sub-stream Q1-A2** — fixed-action-sequence harness wrapping §2.1's probe.

Per-encounter golden = `(seed, [PlayerAction]) → mid-combat snapshot sequence`.
`PlayerAction` is the existing Q1 wire shape (per `PlayerAction.cs`); upstream
side replays the same logical actions via reflection-invoked
`CardPlayer.PlayCard`.

**Action sequence selection**:
- For `CultistsNormal` (regression-locked): the action sequence is the
  oracle's optimal cultist strategy as solved by `engine/cpp/` (Q2). This
  binds Q1 mid-combat goldens to oracle-derived ground truth. Per ADR-029
  campaign tracker, cultist is the only Phase-1 encounter currently solved
  by Q2.
- For Phase-1.5 encounters (no Q2 solution): the action sequence is a
  hand-authored representative strategy committed alongside the golden.
  Spec: "play `StrikeSilent` until enemy dies, then `DefendSilent` to end of
  turn." Mechanical and deterministic; no engineer judgement at gate-run time.
- For RNG-driven encounters (`SmallSlimes`, `MediumSlimes`, …): action sequence
  is parameterized by seed; the harness regenerates the spawn list per seed
  exactly as `UpstreamInitialStateComparer.cs:217-222` already does for
  `GenerateMonsters`.

**Fast subset for `q1-ci` ≤60s**:
- `CultistsNormal × 1 seed × full turn loop` = cultist mid-combat smoke
  (~8s).
- 6 currently-ported encounters (`LouseProgenitor`, `NibbitsWeak`, plus the
  4 wave-26+ shipped encounters per ADR-029) × 1 seed × terminal-only diff
  = 6 × ~1s = 6s. Terminal-only on these is acceptable because they don't
  yet have full per-turn upstream coverage (Q2 oracle doesn't solve them);
  the smoke catches gross regression.
- Total fast subset: ~14s. Headroom of ~46s vs the ≤60s budget; nightly/pre-merge
  has the full corpus.

**New-encounter opt-in gate** (commission §2 sub-bullet 3):
Phase-1.5 Wave-N+1 ports encounter `X`. Wave N+1 cannot merge until:
1. A mid-combat golden for `X` is captured against upstream v0.105.1 (current
   pin) and committed under `goldens-upstream/mid-combat/{X}/`. Capture via
   `make probe-upstream-mid-combat-capture` (new target, mirrors
   `probe-upstream-capture`).
2. Q1's substrate emits identical bytes for `X` at all probed turn-sides;
   `probe-upstream-mid-combat` PASS includes `X`.
3. The wave's fixed action sequence is documented in
   `engine/headless/test/determinism-probe-upstream-capture/src/EncounterCatalog.cs`
   alongside the existing `EncounterPlan.Reason` field — a new optional
   `MidCombatActionSequenceId` string referencing a sequence in
   `goldens-upstream/mid-combat/action-sequences/`.

*Lead-with-negatives*:
- The per-encounter action sequence is a **hand-authored artifact** for
  Phase-1.5 encounters (no oracle to derive it). Engineers writing a
  representative sequence may not exercise the most divergence-likely paths
  (e.g., authoring a sequence that never triggers `kStickyShot` for a pokey
  variant). Mitigation: peer-review at wave dispatch requires Q2-lead sign-off
  on the sequence's coverage for the new encounter (Q2 owns oracle-side
  coverage analysis even though oracle execution is decoupled from this
  gate). Q2-lead can review without participating in the wave.
- **The terminal-only fallback for ported encounters is intentionally weaker
  than cultist's per-turn coverage** until those encounters' goldens grow.
  This is a known short-term gap; ADR-035 (draft below) ratifies it as a
  bootstrap state, not a permanent posture.

**Red-team validation**:
1. On a throwaway branch, modify `LouseProgenitor.cs`'s `CURL_AND_GROW` move
   to apply `Strength+6` instead of `Strength+5`.
2. Run `make probe-upstream-mid-combat`.
3. Confirm: per-encounter parity for `LouseProgenitor` FAILS at turn ≥2
   (when curl-and-grow first fires), naming `Enemy[0].Powers[Strength].Stacks
   q1=6 golden=5`.

### 2.3 Q4 token-coherence regression battery hardening pass `[PLANNED]`

**Sub-stream Q4-B1** — semantic DSL extractor from upstream source.

New tool: `tools/content/extract_card_dsl.py` (Python; CWD: project root;
`.venv/bin/python` per `[[feedback-python-venv]]`). Walks
`~/development/projects/godot/sts2/src/Core/Models/Cards/` via line-regex
matching (no full Roslyn parser — same constraint that `entity_extract.py`
operates under per its docstring lines 14-26). Recognizes a constrained set
of `OnPlay` patterns:

> **Superseded — see ADR-035 Amendment 2026-05-21 §1.** Original 5-pattern table
> below extracted zero Silent cards against actual upstream syntax. Verified
> 12-pattern surface is in the ADR amendment; engineer brief MUST quote the
> amendment, not this section's original table.

| Upstream pattern (ORIGINAL — INCORRECT) | Registry DSL emission |
|---|---|
| `DamageCmd.Attack(N).FromCard(...).Targeting(cardPlay.Target).Execute(...)` | `{op: "attack", base: N, target: "single"}` |
| `DamageCmd.AttackAll(N)...Execute(...)` | `{op: "attack", base: N, target: "all_enemies"}` |
| `BlockCmd.Gain(N).Execute(...)` | `{op: "block_self", base: N}` |
| `ApplyPowerCmd.Apply(<Power>, N).Targeting(cardPlay.Target).Execute(...)` | `{op: "apply_power", power: "<Power>", base: N, target: "single"}` |
| `DrawCmd.Draw(N).Execute(...)` | `{op: "draw", base: N}` |
| anything else | `{op: "unknown", source: "card-extractor", upstream-line: <N>}` |

Per-card `OnPlay` bodies typically contain 1-3 of these patterns. Multi-pattern
bodies emit a list of effects in source order.

**Sub-stream Q4-B2** — extend `validate_registry.py:47-68` with:

1. **DSL-coherence invariant** — every `card_dsl` record with `op != "unknown"
   AND op != "stub"` must have all required fields (`base` for attack/block_self/
   apply_power/draw; `power` for apply_power; `target` for attack/apply_power).
2. **Stub-rejection invariant** — `op == "stub"` is REJECTED as an error
   (currently accepted because `seed_phase1_registry.py:69-79` emits it). The
   bridging plan: wave-2 substream Q4-B3 re-seeds the registry from
   `extract_card_dsl.py` output, so post-merge no `op == "stub"` exists. The
   stub-rejection invariant promotes from warn to fail at wave-2 merge.
3. **Unknown-tolerance invariant** — `op == "unknown"` is ALLOWED but counted;
   the test reports the count and asserts `unknown_count <= K_UNKNOWN_MAX`.
   `K_UNKNOWN_MAX` starts at the post-extraction count (e.g., 12 cards that
   don't fit the patterns above) and ratchets down only on explicit ADR-style
   PR (mirrors the `phantom feature` policy in ADR-023 — unknowns are an
   honest gap, not a silent failure).

**Sub-stream Q4-B3** — canonical vs Q1-fixture drift gate.

New test in `tools/tests/content/test_registry.py`: `test_canonical_vs_q1_fixture_token_set`.
Loads `contracts/registry/phase1-silent.json` and
`engine/headless/test/fixtures/q4-manifest-phase1.json`; compares the
token-name set for each kind (`card`, `relic`, `power`, `enemy`, `potion`,
`encounter`).

*Lead-with-negatives*:
- The current monster-token drift is **34 vs 37** (per ADR-027 close-out wording
  in the commission brief). The gate would fail today. The gate ships
  in a "report-only" mode for one wave, allowing ADR-027's documented growth
  policy to land the missing 3 monster tokens to canonical, then promotes to
  "fail" the next wave.
- The Q1 fixture is structurally smaller than canonical (no encounter tokens
  per `content-registry.md:14`). The gate accepts asymmetric coverage per kind
  *as configured*; the asymmetry is checked into the gate config, so any
  unconfigured asymmetry is a fail.

**Sub-stream Q4-B4** — wiring into upstream-sync pipeline.

`tools/upstream-sync/src/upstream_sync/cli.py sync-check` already emits a
delta report (per ADR-026 §1). Add a post-detect step: after categorization,
run `extract_card_dsl.py` against the changed card files; emit a
`q4-dsl-drift-report.json` sidecar listing per-card DSL changes (new
patterns, removed patterns, unchanged). This report is consumed by the
existing `cli.py dispatch-quantum-lead` flow as a Q4 advisory section in
the per-row port-decisions sidecar.

**Wall-clock budget**:
- `extract_card_dsl.py` runs once per `make sync`; not on `make q1-ci` /
  `make ci`. Budget: ~2s for ~98 cards.
- `test_registry.py` (existing battery + 3 new tests) runs in `make phase0-gate`
  per `content-registry.md:18`. Current ~0.5s; new tests add ~0.3s. ≤1s total.
  Fits inside the `make q1-ci` ≤60s budget if folded in (recommend folding —
  Q4 coherence is cheap and the only quantum-specific check Q4 has).

**Where it runs**:
- `make phase0-gate` (existing): all 3 new tests run here. ~1s additional.
- `make sync` (existing, ADR-026): `extract_card_dsl.py` runs here, emits sidecar.
- Recommendation: add `make q4-ci` aliased to `make phase0-gate` content-test
  subset, for symmetry with `q1-ci` / `q2-ci`.

**Red-team validation**:
1. On a throwaway branch, modify upstream `~/development/projects/godot/sts2/src/Core/Models/Cards/StrikeSilent.cs:26`
   to read `DamageCmd.Attack(7)` instead of `Attack(6)`. (Equivalently:
   simulate an upstream rebalance.)
2. Run `tools/content/extract_card_dsl.py` then `tools/tests/content/test_registry.py`.
3. Confirm: extractor emits `{op: "attack", base: 7}` for `StrikeSilent`;
   `test_registry.py` test_canonical_vs_q1_fixture_token_set has no signal
   (token set unchanged) — this is the **expected gap**.
4. Confirm: DSL-coherence invariant catches the change ONLY if the canonical
   registry also re-extracts (which it does in `make sync` → seed regeneration
   path). End-to-end red-team requires running `make sync` against the modified
   upstream tree and observing the resulting `phase1-silent.json` diff at PR
   time.

*Lead-with-negatives — gap*: this gate detects DSL semantic drift only at the
**Q4 registry-vs-Q4 registry** level (post-`make sync`); it does not detect
"Q1 substrate's `OnPlay` emits attack(6) while upstream's emits attack(7)" —
that is §2.1's mid-combat probe job. The two gates are orthogonal:
§2.1 detects substrate behavior drift; §2.3 detects registry-vs-upstream-source
drift. Both are needed for full R12 coverage.

### 2.4 Inline-upstream-comment Roslyn analyzer `[PLANNED]`

**Sub-stream Q1-A3** — new analyzer project
`test/Sts2Headless.UpstreamCommentAnalyzer/` (modeled on the existing
`test/Sts2Headless.AnalyzerTripwire/` precedent; wired via
`Directory.Build.props` so it fires for all Domain projects).

**Diagnostic ID**: `STS2_UPSTREAM_001`. Severity: Warning (Phase 3a — warn-only
per ADR-024 promotion-window precedent); promotion to Error after 2 wave
cycles green.

**Rule**: every public/internal method of types under
`Sts2Headless.Domain.Content.{Cards,Monsters,Powers,Relics,Encounters}` that
mirrors an upstream type MUST have at least one of:
1. A documentation comment containing the literal token `upstream-source:`
   followed by a path under `~/development/projects/godot/sts2/src/Core/Models/`.
2. An attribute `[UpstreamSource(Path = "...", LineRange = "L..L")]` (new
   attribute under `Sts2Headless.Domain/Attributes/UpstreamSourceAttribute.cs`).

**Granularity**: per-method, NOT per-file. Per-file is too coarse — a single
stale comment at top of file doesn't certify each method body. Per-behavior-change-site
is too fine — engineers don't want to comment every `if` branch.

**Resolved unknown — "granularity: per-method"**. *Lead-with-negatives*:
- A method body can drift without changing the inline comment (the gate doesn't
  parse upstream source to verify semantic alignment — too expensive and
  fragile per §1.4). The comment is a code-review hook, not a verifier.
- Methods that genuinely have no upstream analog (e.g., Q1-only adapter
  helpers like `CombatEngine.PlayerPlayCard`'s typed-id wrapper per ADR-033)
  need an exemption: `[UpstreamSource(Path = null, Reason = "Q1-only")]`.
  The analyzer accepts a sentinel `null` path with non-empty `Reason`.

**Wall-clock budget**: analyzer fires during `dotnet build` per
`Directory.Build.props`. Existing build is ~30s for full sln; analyzer cost
adds ~1-2s (per the existing `AnalyzerTripwire` precedent). Fits inside
`make q1-ci` ≤60s budget.

**Where it runs**:
- `dotnet build` everywhere (engineer's local dev loop, CI, all wave
  preflights). Per ADR-024 precedent: warn-only at first; ratchet to error
  after 2 wave cycles green.

**Red-team validation**:
1. On a throwaway branch, remove the `upstream-source:` comment block from
   `Sts2Headless.Domain/Content/Monsters/FatGremlin.cs` (currently lines 5-22).
2. Run `dotnet build`.
3. Confirm: analyzer emits `STS2_UPSTREAM_001` warning naming `FatGremlin`
   constructor and `MonsterMove[]` block as lacking upstream reference.
4. Confirm: `dotnet build` exit code is 0 (warn phase); after ratchet, exit
   code is non-zero.

### 2.5 Red-team validation requirements (cross-cut) `[PLANNED]`

Per commission §5 — load-bearing. R12 only DISCHARGES on all five red-team
validations completing successfully.

Each gate sub-stream above carries its red-team validation inline. Synthesizing:

| Gate | Injection | Expected detection | Operator effort |
|---|---|---|---|
| §2.1 Mid-combat probe | `+1` to `kIncantation` Ritual stacks in Q1 cultist | `encounter=CultistsNormal seed=42 turn=2 side=enemy-end field=...Ritual.Stacks` | ~5min on throwaway branch |
| §2.2 Per-encounter parity | `+1` to `Strength` in `LouseProgenitor.CURL_AND_GROW` | `encounter=LouseProgenitor turn=2 side=enemy-end field=...Strength.Stacks` | ~5min |
| §2.3 Q4 DSL drift | `+1` damage on upstream `StrikeSilent.OnPlay` | post-`make sync` registry diff visible in PR | ~10min (requires `make sync` cycle) |
| §2.4 Roslyn analyzer | Delete `upstream-source:` comment block from `FatGremlin.cs` | `STS2_UPSTREAM_001` on `FatGremlin` ctor | ~2min |
| §2.5a SyncStatePinGate (existing, hardening pass) | Mutate `upstream-pin.json:pinned_dll_sha256` to bogus hex | existing `DLL HASH DRIFT` failure | ~1min |

**Red-team validations execute in a single wave-2 sub-stream Q1-R1**. All five
injections + reverts are captured in a single PR (rejected from main — committed
to a `redteam/` branch for archival). Per-gate PASS evidence is committed to
`engine/headless/docs/specs/drift-gate-hardening-redteam-evidence.md` (this
plan's companion artifact). Without this artifact landed, R12 stays OPEN.

*Lead-with-negatives*: red-team injection on `~/development/projects/godot/sts2/src/`
is destructive — that tree is the upstream-source source-of-truth. Mitigation:
the red-team for §2.3 uses a **local rsync** of the upstream tree into a
disposable directory (`/tmp/sts2-redteam-upstream/`), with `STEAM_STS2_DIR`
env override per `UpstreamDriver.cs:103-107`. The actual Steam install tree
is read-only throughout.

### 2.6 Cross-cuts `[PLANNED]`

**Wall-clock budgets — explicit commitment per gate**:

| Gate | `q1-ci` ≤60s | Nightly | Pre-merge | Source |
|---|---|---|---|---|
| Mid-combat probe (cultist smoke) | ~8s | included | included | §2.1 |
| Mid-combat probe (full corpus) | NO | ~8 min | included | §2.1 |
| Per-encounter parity (6-encounter smoke) | ~6s | included | included | §2.2 |
| Per-encounter parity (full corpus) | NO | included in mid-combat full | included | §2.2 |
| Q4 DSL coherence (3 new tests) | ~1s | included | included | §2.3 |
| Q4 canonical-vs-fixture drift | ~0.2s | included | included | §2.3 |
| Roslyn analyzer | ~1-2s (in build) | included | included | §2.4 |
| **TOTAL `q1-ci` fast lane addition** | **≤17s** | n/a | n/a | sum |

Existing `make q1-ci` budget: ~30s for build + ~5s for tests = ~35s. +17s = ~52s.
**`q1-ci` ≤60s commitment holds with ~8s headroom.**

**State-storage implications**:
- Mid-combat goldens: ~3.5 MB total across 22 encounters × 10 seeds (per §1.5
  decision). Compared to existing `goldens-upstream/initial-state/` at ~2.2 MB.
  Repo growth: ~5.7 MB total. Within ADR-027 §Negatives "test wall-clock budget
  for make q1-ci grows" tolerance.
- DSL extractor output: not committed; regenerated on `make sync`.
- Red-team evidence artifact: ~10 KB markdown.

**Q4 lead coordination required** (commission §6):
1. Does Q4 lead agree the DSL extractor's pattern table (§2.3 sub-stream Q4-B1)
   covers enough of Silent's 98 cards to be useful? If <70% extractable, the
   DSL stays stubbed for the unsupportable subset and Q4-coherence gate
   `K_UNKNOWN_MAX` starts high — possibly higher than R12's mitigation needs.
   **Pre-execution survey by Q4 lead recommended before wave-2 dispatch.**
2. Does Q4 lead want the `card_dsl` schema bumped to v2 with a top-level
   `coverage: "extracted|hand|stub"` field? Architect recommends YES (honest
   coverage signaling), but this is Q4-spec scope and Q4 lead owns.
3. Q4-side ratification of the canonical-vs-fixture asymmetry policy (the
   `0 encounters in canonical, 22 in Q1 fixture` gap) — does Q4 want a per-
   wave ratchet (encounters land in canonical alongside Q1) or accept
   permanent asymmetry? Architect recommends per-wave ratchet (less drift
   surface long-term).

These are **questions for Q4 lead, surfaced to project-lead to relay**. They
do not block this plan's draft; they shape Q4 sub-stream scope at wave-2
dispatch.

## 3. Phase-1.5 resumption gate `[PLANNED]`

Per commission §7 acceptance criterion — explicit minimum set required before
Phase-1.5 encounter ports may resume:

**Wave-1 (single wave, 5 sub-streams)**:
- Q1-A1: §2.1 mid-combat probe extension to `UpstreamDriver` + cultist golden
  capture + cultist-only `make probe-upstream-mid-combat` PASS.
- Q1-A3: §2.4 Roslyn analyzer warn-mode landed.
- Q4-B1: §2.3 `extract_card_dsl.py` shipped + `phase1-silent.json` re-seeded
  with extracted DSL (stubs cleared for extractable cards).
- Q4-B2: §2.3 DSL-coherence + unknown-tolerance invariants landed in
  `validate_registry.py`.
- Q1-R1: §2.5 red-team validations executed; evidence artifact landed.

**Wave-1 acceptance**:
- `make q1-ci` ≤60s — verified.
- `make probe-upstream-mid-combat` on cultist + currently-ported encounters
  PASSES.
- `make phase0-gate` PASSES with new Q4 invariants.
- All 5 red-team injections fired and reported correctly per §2.5 table.
- ADR-035 (draft below) ratified.

**Wave-2 (Phase-1.5 resumption begins; first port wave)**:
- Per-encounter scope §2.2 fast subset + opt-in expansion for the encounter
  ported by wave-2.
- Q4 canonical-vs-fixture drift gate promotion (warn → fail) after the wave's
  fixture-growth landing.

**Until wave-1 lands, Phase-1.5 stays paused.**

## 4. Schema bumps predicted `[PLANNED]`

Per commission constraint "predict whether any scope item requires schema work":

- **Q1 wire schemas (M1/M2/M3/M4)**: no bump. Mid-combat probe operates
  on the existing state codec (StateCodec v4 per ADR-032) and `MonsterIntent`
  v4 wire format. No new fields added to wire; the probe's golden bytes are
  a **superset projection** of existing wire fields plus monster-intent and
  RNG-counter (both already wire-serialized in the M1 schema today).
- **Q4 registry schema (`schema.json`)**: bump v0 → v1 if Q4-B2's
  `coverage: "extracted|hand|stub"` field is approved. Forward-compatible
  (treat missing as `"stub"`). Per `bumping-a-schema-version` skill conventions:
  emit ADR with schema rev table.
- **`contracts/schemas/` protobuf**: no bump. None of the cross-quantum wire
  formats are touched. R12 mitigation is structural-internal-to-Q1+Q4, not
  cross-quantum.
- **`upstream-pin.json`**: schema additive — new optional field
  `mid_combat_goldens_captured_at_buildid` to track when the mid-combat goldens
  were re-captured (separate from the existing `pinned_buildid`). Not a breaking
  change; readers ignore unknown fields.

## 5. Open questions for project-lead review `[PLANNED]`

1. Q4-lead pre-execution survey on DSL extractor pattern coverage (§2.6 question
   1) — should this be a wave-0 sub-stream blocking wave-1 dispatch, or run
   in parallel with wave-1 Q1 streams?
2. Roslyn analyzer ratchet timeline (§2.4) — ADR-024 precedent is "2 wave cycles
   green"; for this analyzer, do we count wave-1's own cycle or only post-wave-1
   cycles? Architect recommends the latter (2 future cycles after wave-1 ship)
   for parity with ADR-024.
3. Mid-combat probe nightly target wiring — should `make probe-upstream-mid-combat`
   run on every GHA push, or only on `engine/headless/src/` touches? Architect
   recommends path-filtered on the `paths-include` GHA trigger; ~8min per run
   is not cheap.

These are project-lead asks; architect does not resolve.

## 6. Out of scope — explicit `[PLANNED]`

- Resurrecting ADR-002 option (a) or option (b). Closed by ADR-034 and wave-43
  spike respectively.
- Phase-2 / learned drift detection (embeddings, ML semantic similarity). R12
  mitigation is structural at this stage per commission directive.
- Engineer-subagent execution. This is a design artifact.
- Implementation code skeletons. Engineers write code in subsequent waves.
- Q2 oracle changes. Q2 Path A campaign (ADR-029) continues decoupled.

## 7. Cross-references

ADR-023 (per-section status badges), ADR-024 (`doc-only:` flag — this doc's
commit honors it), ADR-026 (upstream-sync pipeline; §2.3 sub-stream Q4-B4
wires into it), ADR-027 (Q4 fixture growth policy; §2.3 Q4-B3 ratifies it
gate-wise), ADR-028 (substrate baseline at v0.105.1; this plan's pin), ADR-029
(Path A engine-expansion campaign; orthogonal Q2 track), ADR-030 (OnDeath
hook protocol; current substrate state, no schema change here), ADR-032
(`MonsterIntent` v4; this plan inherits), ADR-034 (parallel-substrate
ratification; this plan is the §Future work "drift-gate hardening"
deliverable).

## 8. Origin

Project-lead commission 2026-05-21, post-ADR-034 close, R12 discharge plan.
Architect: quantum-architect persona. Draft: 2026-05-21.
