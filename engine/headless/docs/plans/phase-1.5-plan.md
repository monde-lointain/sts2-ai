# Phase-1.5 Plan

Drafted 2026-05-12 per project-lead direction. Required for Q1 Phase-1 close.

Phase-1A ratified 2026-05-12. R4 SUBSTANTIALLY MITIGATED, not DISCHARGED. R9 (this plan) IN PROGRESS.

## Scope (binding)

Three streams; all required for Phase-1 close per `docs/specs/modules/game-simulator.md` and `docs/plans/q1-implementation-plan.md` §6.1.

- **P-1.5-A**: 14-encounter per-step Godot parity (live-Godot probe across the full non-smoke corpus).
- **P-1.5-B**: 14-encounter behavior fill-in (`OnPlay` bodies, multi-state intent rotations, X-cost evaluator, SubscribeHooks for non-γ relics, non-γ power triggers).
- **P-1.5-C**: B.1-ε encounter-RNG plumbing (SmallSlimes / MediumSlimes go from `MissingUpstream` to `UpstreamComparable`).

Out of scope: Phase-2 run domain (S14+); Phase-3+ counterfactual rollout (S17+).

## Approach (per lead 2026-05-12)

**Live-Godot probe approach (c) hybrid, confirmed:**
- Initial-state surface — already de facto delivered via Stream-C's linked-DLL captures (140/0/20 goldens byte-exact). No further work.
- Per-step surface — **full upstream Console host** (approach (a)) with 12 SceneTree singletons stubbed. This is the load-bearing piece. (b) SetUpCombat-only does NOT cover per-step and is rejected as a Phase-1.5 replacement.

**Stub-pin-harness required upfront** per lead pushback 2. See `engine/headless/docs/specs/modules/stub-pin-harness.md`. Each of the 12 singleton stubs records call-count + arg-shape per probe run, pinned against initial-state Stream-C goldens + smoke-set per-step golden BEFORE any non-smoke encounter runs through the stub stack. Catches singleton-stub drift as a stub-pin failure, not as a downstream per-step divergence.

## Stage breakdown + estimates

| Stage | Description | Est. LOC | Est. wall | Risk |
|---|---|---|---|---|
| P-1.5-1 | Console host project + 12 SceneTree singleton stubs + stub-pin-harness | ~700 (stubs) + 200 (harness) + 300 (tests) | 3–4 wk | semantic-drift in stubs (mitigated by harness); upstream `.uid` resolution |
| P-1.5-2 | Per-step probe corpus capture (14 encounters × 10 seeds = 140 entries) via Console host | ~150 (driver) + 140 goldens | 1 wk | golden churn if probe formats shift |
| P-1.5-3 | Behavior fill-in — 14 encounters: ~3-5 monster rotations/encounter, ~30 non-smoke cards `OnPlay`, ~40 relics `SubscribeHooks`, ~25 non-γ powers, X-cost evaluator | ~1500–2000 + tests | 2 wk | upstream semantic detail drift; many small TDD cycles |
| P-1.5-4 | B.1-ε encounter-RNG plumbing — `EncounterModel.Generate(IRunState, Rng)` virtual + per-encounter RNG seed (`runState.Rng.Seed + totalFloor + hash(encounter.Id)`) + 4 new monster classes (`LeafSlimeS/M`, `TwigSlimeS/M`) | ~600 + tests | 5–7 days | upstream parity for RNG-driven spawn variants |
| P-1.5-5 | Final probe regression + goldens consolidation + R4 DISCHARGE evidence | ~50 (probe glue) + goldens regen | 3–5 days | none beyond what prior stages surface |

Total realistic: **7–9 wk**. Per lead's pushback 1, headline as stretch with explicit fallback.

## Target dates

- **Stretch:** 2026-07-07 (8 weeks). Holds only if console-host spike (P-1.5-1 first 2 weeks) sizes within estimate.
- **Realistic:** 2026-07-14 — 2026-07-21 (9–10 weeks). Adopt within 14 days if spike shows slip.
- **Re-surface trigger:** ≤2 weeks before target (2026-06-23 if stretch holds). Surface to lead with hard date + spike result.
- **Hard surface trigger:** if console-host LOC > 1500 OR a singleton stub takes >3 days, escalate immediately; do not absorb silently.

Lead's pushback 1 accepted: refining behaviors estimate to **2 wk** (was 1 wk), accepting 2026-07-07 as stretch with realistic fallback.

## Critical path

`P-1.5-1 → P-1.5-2 → P-1.5-3 || P-1.5-4 → P-1.5-5`

- P-1.5-3 and P-1.5-4 parallelize (file-disjoint per Q1-ADR-011).
- P-1.5-2 blocks on P-1.5-1 (need stubs before capture).
- P-1.5-5 blocks on all earlier stages.

## Console-host-spike sizing (initial estimate)

12 singletons + stubs (line counts ±50%):

| Singleton | Stub strategy | Est. LOC |
|---|---|---|
| `NRunMusicController.Instance` | No-op audio (silent) | 10 |
| `NCombatRoom.Instance` | Deterministic enemy-position table; no animation | 50 |
| `NModalContainer.Instance` | No-op modal stack | 20 |
| `NCombatStartBanner.Create()` | Factory returning sentinel | 10 |
| `NCombatRulesFtue` | Always-allow tutorial gates | 10 |
| `Cmd.CustomScaledWait` | Synchronous bypass (no actual wait) | 30 |
| `SaveManager.Instance` | No-op persistence | 50 |
| `RunManager.Instance.ActionExecutor` | Direct delegate to Q1's M6d action queue | 100 |
| `NetCombatCardDb.Instance` | Bind to existing `CardCatalog` | 30 |
| 3× `await` points in `StartCombatInternal` | Patched out via wrapping or Q1-T3 ledger entry | 20 |
| **Subtotal — stubs** | | **~330** |
| Console host project + composition root | | ~200 |
| Per-stub assertion harness (Pinned<TStub>) | | ~150 |
| Driver + probe wiring | | ~150 |
| Tests | | ~300 |
| **Total P-1.5-1** | | **~1130 LOC** |

Spike result refines this. If actual > 1500, halt-and-escalate.

## Risk register (R9 sub-decomposition)

| Sub-risk | Pre | Mitigation |
|---|---|---|
| R9.1 — singleton-stub semantic drift | IN PROGRESS | Stub-pin-harness up front (see spec) |
| R9.2 — upstream `.uid` resolution failure on `scenes/game.tscn` | IN PROGRESS | Console host bypasses scene mount; `SetUpCombat` + `Player.PopulateCombatState` already proven viable in Stream-C |
| R9.3 — behavior-fill-in scope creep | IN PROGRESS | TDD'd per-encounter; coverage gate enforces no silent skips |
| R9.4 — encounter-RNG seed derivation diverges from upstream | IN PROGRESS | Differential test against upstream `EncounterModel.cs:198`; hard gate |
| R9.5 — probe golden regeneration thrash if stub stack changes mid-stream | IN PROGRESS | Stub-pin failure catches stub change BEFORE goldens regenerate; freeze stub stack post-P-1.5-1 |

## Cross-quantum unlocks on Phase-1.5 close

- Pipeline Phase 1 gate (≥95% A0 normal pool win rate, ≥90% expectimax agreement, <100ms @64-sim).
- R4 DISCHARGE.
- M-Headless gate covers full corpus (not just smoke + initial-state).

## What is NOT in Phase-1.5

- S14 Run Domain (Phase-2 scope).
- Live-Godot probe for Phase-2 run-level decisions (different scene-tree coupling).
- Performance optimization (GC reduction, allocation cleanup beyond R7 monitoring) — R7 is a watch item, not a Phase-1.5 work item unless `gc_time_seconds` exceeds 5% of decision wall-clock.
