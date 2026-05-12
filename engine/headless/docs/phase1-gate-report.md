# Phase-1 Gate Report

Q1 build: `200b51f8132458ca3309b0a822211345689ff083` (worktree branch `worktree-agent-a61387d77a8e24fdd`, parent of pending S13 merge into `main` HEAD `083c1d2`).

Probe corpus: 292 entries (22 structural + 220 initial-state @ 10 seeds × 22 encounters + 50 per-step smoke @ CultistsNormal). All entries PASS the self-consistency contract (Q1 build vs. captured-once goldens, byte-exact hash match).

## Gate Items

| Gate | Status | Evidence |
|---|---|---|
| Bit-identical roundtrip CI gate | PASS | S7; 65 BitIdenticalRoundtripTests pass on every `make ci` invocation (Sts2Headless.Tests.Adapters.StateCodec). |
| M2 latency p99 < 500µs | PASS | S9 measurement (10 000 roundtrips): p50 = 4.30 µs, p95 = 8.52 µs, **p99 = 14.24 µs**, p999 = 34.43 µs, max = 80.48 µs, 48 alloc-bytes/RT. ~35× margin under the 500 µs hard limit. |
| Probe Phase-1 corpus passes | PARTIAL | Self-consistency mode: 292 / 292 entries PASS (structural 22/22, initial-state 220/220, per-step smoke 50/50). Live-upstream comparison: deferred to Phase-1.5 — see Approach A blockers below. |
| Content-coverage gate green | PASS | S12 Phase1 catalogs registered for the Phase-1 manifest fixture (`test/fixtures/q4-manifest-phase1.json`); Q4ManifestLoaderTests gating. 96 cards / 58 relics / 45 powers / 32 monsters / 21 potions / 22 encounters. |
| Pinned multi-hook ordering | PASS | S4 Scenario1–6 (6 scenarios) pass — Sts2Headless.Tests.Domain.Actions.PinnedHookOrderingTests, all 6 tests green. |
| T3 Ledger ≲ 5 entries | PASS | 0 entries to date in `docs/specs/modules/engine-strip.md` § T3 Ledger; well under the ~5 budget. All Godot-surface replacements achieved via T2 (DI / stub registry) — no upstream-tree edits required. |
| Q2 Oracle adapter | PARKED | Per `docs/plans/q1-implementation-plan.md` Assumption §3: same-owner / context-switch task, parked behind a mark for solo-dev workflow. Not on the Phase-1 critical path. |

## Per-encounter probe results

All 22 encounters: structural PASS, initial-state (10 seeds each) PASS, per-step PASS only for the smoke encounter — by design per `s12-behaviors-deferred-to-s13` deferral (most non-smoke OnPlay / SubscribeHooks / power-trigger bodies are still empty stubs ported as metadata-only in S12). Per-step on non-smoke is Phase-1.5 scope (behavior fill-in).

| Encounter | Structural | Initial-state (10 seeds) | Per-step |
|---|---|---|---|
| CultistsNormal | PASS | PASS | PASS (50 seeds) |
| ChompersNormal | PASS | PASS | deferred (Phase-1.5) |
| ExoskeletonsNormal | PASS | PASS | deferred (Phase-1.5) |
| JawWormSolo | PASS | PASS | deferred (Phase-1.5) |
| TwoLouseNormal | PASS | PASS | deferred (Phase-1.5) |
| SmallSlimes | PASS | PASS | deferred (Phase-1.5) |
| MediumSlimes | PASS | PASS | deferred (Phase-1.5) |
| LargeSlimeBoss | PASS | PASS | deferred (Phase-1.5) |
| BowlbugsTrio | PASS | PASS | deferred (Phase-1.5) |
| FuzzyWurmCrawlerSolo | PASS | PASS | deferred (Phase-1.5) |
| FossilStalkerElite | PASS | PASS | deferred (Phase-1.5) |
| FrogKnightElite | PASS | PASS | deferred (Phase-1.5) |
| LagavulinElite | PASS | PASS | deferred (Phase-1.5) |
| SentryTrio | PASS | PASS | deferred (Phase-1.5) |
| HauntedShipSolo | PASS | PASS | deferred (Phase-1.5) |
| LivingFogSolo | PASS | PASS | deferred (Phase-1.5) |
| GremlinMercNormal | PASS | PASS | deferred (Phase-1.5) |
| SnakePlantSolo | PASS | PASS | deferred (Phase-1.5) |
| FungalBossEncounter | PASS | PASS | deferred (Phase-1.5) |
| CenturyGuardBoss | PASS | PASS | deferred (Phase-1.5) |
| KaiserCrabBoss | PASS | PASS | deferred (Phase-1.5) |
| CeremonialBeastBoss | PASS | PASS | deferred (Phase-1.5) |

Totals: 22/22 structural, 220/220 initial-state, 50/50 per-step (smoke only) — all PASS in Q1 self-consistency mode.

## Approach A (live Godot upstream comparison) — status

Live capture via the upstream Godot project at `~/development/projects/godot/sts2/` is the stronger validation target (Q1 ↔ upstream byte-exact per-step). Status:

- Godot 4.5.1-mono installed at `/home/clydew372/applications/godot/Godot_v4.5.1-stable_mono_linux_x86_64/godot` and on PATH.
- Upstream-side scaffolding driver committed at `~/development/projects/godot/sts2/q1_probe_driver.gd` (additive, does NOT modify any upstream scene/script). `godot --headless --path <upstream> --script res://q1_probe_driver.gd -- --seed=42 --encounter=cultists_normal --out=…` runs cleanly and exits with sentinel code 2 (not-yet-driving-combat).
- Upstream's `sts2.csproj` builds clean (0 errors, 7839 warnings) with .NET 9.0.116 + `rollForward: latestMajor` (workaround: invoke `dotnet build` from a temp cwd that owns its own global.json — upstream's pinned SDK 9.0.303 is not installed locally and modifying upstream's global.json would violate "do not touch upstream").
- `sts2.dll` from the Steam install at `~/snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64/` is available for linking.

**Blocker preventing live per-step capture in S13:** upstream's `CombatManager.StartCombatInternal` synchronously references ~12 Godot.SceneTree-coupled singletons (`NRunMusicController.Instance`, `NCombatRoom.Instance`, `NModalContainer.Instance`, `NCombatStartBanner.Create()`, `NCombatRulesFtue`, `Cmd.CustomScaledWait`, `SaveManager.Instance`, `RunManager.Instance.ActionExecutor`, `NetCombatCardDb.Instance`, and animation `await` points). Driving it headlessly requires either (a) full game scene mount (`scenes/game.tscn`) — currently fails on `.uid` resolution because the decompiled-retail tree was never built by the editor's "Build C# project" step that writes per-`.cs` `.uid` files — or (b) a console host that links `sts2.dll` and stubs all twelve coupled singletons. Both paths are 1–2 week efforts and outscope a single probe-stage.

**Cleaner deterministic surface for Phase-1.5:** `CombatManager.SetUpCombat` + `Player.PopulateCombatState` run RNG-driven shuffles with **no** scene-tree deps (the call chain ends at `DrawPile.RandomizeOrderInternal(this, rng, state)`). A future Phase-1.5 follow-up can drive those two methods alone via a small linked-DLL console host, then hash the resulting `CombatState` to capture upstream-derived initial-state goldens. That captures the highest-value golden surface (where R1 RNG drift would surface) without the StartCombat scene-tree gauntlet.

## Phase-1.5 deferrals

| Item | Why deferred | Path forward |
|---|---|---|
| Live-upstream initial-state goldens (22 encounters × 10 seeds) | Approach A blockers above. | Linked-DLL console host invoking `CombatManager.SetUpCombat` + `Player.PopulateCombatState` only. |
| Live-upstream per-step goldens (CultistsNormal × 50 seeds) | Approach A blockers above + `StartCombatInternal` scene-tree coupling. | Stub the 12 referenced Godot singletons in the console host, or `--import --headless` bootstrap upstream's `.uid` files then run combat via autoload. |
| Per-step coverage for 21 non-smoke encounters | S12 metadata-only port deferred behaviors to S13; CombatEngine/EffectDispatcher only handles the smoke set of effect actions. | Combat-behavior fill-in: wire OnPlay bodies for the remaining 87 cards, SubscribeHooks for 53 relics, power triggers for 40 powers, multi-state intent rotations for the remaining 30 monsters (single-state self-looping today). |
| X-cost / calculated-damage cards (Malaise, Skewer, Finisher, Murder, Mirage, KnifeTrap) | Shipped with base damage; formula evaluation deferred. | Implement X-cost evaluator in CombatContext + per-card formula handlers. |
| Lagavulin sleep/idol rotation, FungalBoss spore-cloud/regrow | Single-state intent rotations today. | Port upstream multi-state `IntentScript` rotation logic to Q1's `MonsterModel.Intent` selection. |

## Verdict

**PARTIAL.** All gates except "Probe Phase-1 corpus passes" are PASS. The probe self-consistency mode (Q1 vs. captured-once goldens) is byte-exact at 292/292 entries — meaning Q1 IS deterministic and any future regression will be caught at per-step hash granularity. The HARD GATE bar defined in the S13 prompt ("at least 5 seeds pass per-step end-to-end") is met by the 50-seed smoke per-step pass count.

The PARTIAL is solely on the stronger surface — live-upstream-Godot byte-exact comparison — which is genuinely a multi-week engineering effort (driving upstream's Godot-coupled CombatManager headlessly) and was reasonably outscope for a single probe stage. Advancement to S14 is justified: Q1's determinism contract is gated, the IPC latency budget is met with ~35× headroom, content coverage is registered for the Phase-1 manifest, and the deferred items are tracked with concrete paths forward.

**Recommendation:** proceed to S14 (M6b Run Domain). Track the Phase-1.5 deferrals as a dedicated content+probe-uplift sprint that runs in parallel with run-domain work, gated by re-running `make probe` once the live-upstream comparison harness lands.

---

## B.1-final Addendum (2026-05-12)

Appended on 2026-05-12 per project-lead direction. The original PARTIAL verdict above is preserved verbatim.

### Sub-gate flipped — initial-state-upstream ("M-Headless" plane)

Between S13-T5 (`ced8eb6`) and 2026-05-12, parallel work on the **initial-state-upstream** probe plane — captured-once Godot goldens compared byte-exact against Q1 outputs at combat-state entry — advanced as follows:

- **Stream-C** captured 220 upstream initial-state goldens (10 seeds × 22 encounters) via the linked-DLL approach against `CombatManager.SetUpCombat` + `Player.PopulateCombatState` (the "cleaner deterministic surface" called out under Approach A above).
- Stream-C canary FIRED — 10 Q1 encounters had no upstream STS2 equivalent (carry-overs from the upstream Slay-the-Spire 2 prototype's spawn lists that did not survive into shipped STS2 content).
- **B.1-α/β/γ/δ** reconciled content drift, RNG layering, monster intent rotations, and per-encounter port-or-delete decisions. See `docs/specs/02-encounter-port-decisions.md` § "B.1-final addendum" for the encounter-by-encounter table.
- **B.1-final** (merge `9d89f37`) executed plan (a)+(c) from that addendum: deleted 7 STS1-only encounters, ported `KaiserCrabBoss → Crusher + Rocket` (byte-faithful from upstream), added `LouseProgenitorNormal`. Encounter corpus reshape: 22 → 16; probe initial-state entries 220 → 160.
- **Post-merge measurement (2026-05-12):** `make probe-upstream-initial-state` → **140 PASS / 0 DIVR / 20 SKIP / 0 ERR** in 0.03s. The 20 SKIPs are `SmallSlimes` / `MediumSlimes` × 10 seeds, deferred to B.1-ε pending encounter-RNG plumbing.
- The "M-Headless" sub-gate (initial-state plane against upstream Godot byte-exact) is therefore **PASS** post-merge.

### What this addendum does NOT flip

- **Per-step Godot parity across non-smoke encounters.** The original report's "Per-step PASS only for the smoke encounter" deferral stands. 14 non-smoke encounters have stub `OnPlay` / `SubscribeHooks` bodies; multi-state intent rotations on most non-γ monsters are still single-state; X-cost / calculated-damage evaluator covers only the γ subset (Skewer/Malaise/KnifeTrap). **Phase-1.5 scope.**
- **Live-Godot per-step probe across the full corpus.** Approach A blockers (12 SceneTree singletons coupling `CombatManager.StartCombatInternal`) remain unresolved. **Phase-1.5 scope.** Approach decision (full Console host with stubbed singletons vs. `SetUpCombat`-only thin shim) pending project-lead direction.
- **B.1-ε encounter-RNG plumbing.** Architectural gap: Q1's `EncounterModel` resolves a static spawn list; upstream uses `base.Rng.NextItem(...)` for slime variant selection. `SmallSlimes` and `MediumSlimes` remain registered with placeholder spawn lists (`AcidSlimeS/M`, `SpikeSlimeS/M`) but tagged `MissingUpstream` in `EncounterCatalog`. **Phase-1.5 scope.** Estimated ~80–150 LOC + 4 new monster classes (`LeafSlimeS/M`, `TwigSlimeS/M`).

### Verdict — unchanged

**Overall S13 verdict remains PARTIAL pending Phase-1.5 close per project-lead direction 2026-05-12.**

- **Phase-1A** (infrastructure + smoke per-step Godot parity + 16-encounter initial-state Godot parity + 22-encounter self-consistency) — **ratified** by project lead on 2026-05-12.
- **Phase-1.5** (14-encounter per-step behavior fill-in + live-Godot per-step across full corpus + B.1-ε encounter-RNG) — **OPEN**, required for Q1 Phase-1 close per `docs/specs/modules/game-simulator.md` and `docs/plans/q1-implementation-plan.md` §6.1.

Risk-register impact: R4 (headless port ≤2 mo) is **SUBSTANTIALLY MITIGATED**, not DISCHARGED, as of 2026-05-12.

Pointers: `docs/specs/02-encounter-port-decisions.md` § "B.1-final addendum" (encounter-by-encounter rationale); `docs/q1-stage-manifest.md` (upstream genealogy SHAs).
