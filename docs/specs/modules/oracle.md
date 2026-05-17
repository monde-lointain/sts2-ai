---
quantum: Q2
substrate: engine/cpp/
---

> Status legend: see ADR-023. Section badges = `[SHIPPED]` / `[PHASE-N]` / `[ASPIRATION]`.

# Module: Oracle Verifier (Q2)

> C++ expectimax solver. Ground-truth labels for tractable states; engine→CompactState adapter (ADR-011); the gold standard the network is regression-tested against.

## Responsibilities [MIXED — see bullets]

- **[SHIPPED]** Run expectimax over `CompactState` to produce optimal `(value, action)` for any state small enough to fully expand. Existing capability today (`engine/cpp/src/ai/`); preserved per ADR-004.
- **[ASPIRATION (parked per project-lead grounding)]** Emit the **oracle-agreement signal**: for any state the oracle can solve in budget, compare the network's top-1 action and value with the oracle's. Drives prioritized labeling at Q10. (Sink writer SHIPPED at `engine/cpp/src/oracle/agreement/sink.cc` per Q2-ADR-004; *consumer pathway* awaits Q10 boot.)
- **[SHIPPED]** Maintain the **pinned-seed regression set** (`tools/seed-pinner` pattern, today at `tests/seeds/expected_values.h`). **[PHASE-1.5]** Generalize this from one encounter to a per-component battery.
- **[SHIPPED]** **Engine→CompactState adapter (ADR-011).** Consume Q1's versioned binary state and derive `CompactState` for verifier use. (`engine/cpp/src/oracle/adapter/` per Q2-ADR-001; Phase-1A scope = cultist + Q2-ADR-006 framework; per-encounter expansion per ADR-029 Path A campaign. Non-cultist encounter signatures still reject with `UnsupportedEncounter` diagnostic until their wave adds the corresponding `MonsterMoveTable` entry.)
- **[SHIPPED]** Expose a "verify this state" RPC for Q12 to invoke during evaluation. (`engine/cpp/tools/oracle-verify-server/` per Q2-ADR-003; Q12 consumer not yet booted, transport is forward-laid.)

Out of scope: anything network-side. Q2 does not call the inference server, does not load weights, does not produce trajectories.

## Data Ownership [MIXED — see bullets]

- **[SHIPPED]** **`CompactState` canonical type** — existing C++ POD in `include/sts2/ai/state.h`. Hashable; static_assert chains anchor card-counts indexing to `CardId` enum order.
- **[SHIPPED]** **Pinned regression seed set** — `(seed, encounter_id, expected_value, expected_top_action)` tuples. Versioned per content patch.
- **[SHIPPED]** **Oracle-agreement report rows** — `(state_hash, oracle_action, oracle_value, model_action, model_value, model_version, timestamp)`. Append-only. (15-column Parquet schema frozen S3 per Q2-ADR-004; row writer at `engine/cpp/src/oracle/agreement/sink.cc`; schema-freeze gtest gates the shape.)
- **[SHIPPED]** **Expectimax algorithm version manifest** — algorithm SHA, transposition table parameters, scoring rule version. Stamped on every report row. (`AlgorithmManifest` per Q2-ADR-005, `engine/cpp/src/oracle/adapter/manifest.cc`.)

## Communication [MIXED — see bullets]

- **[SHIPPED]** **Sync — RPC (cold path):** `verify(state_blob) → {value, action, expansion_complete}` with a budget; Q12 calls during evaluation. (JSON-over-Unix-socket per Q2-ADR-003; **[ASPIRATION (parked per project-lead grounding)]** Q12 consumer not booted — RPC is forward-laid.)
- **[ASPIRATION (parked per project-lead grounding)]** **Async — file/queue:** oracle-agreement report rows pushed to a Q3 sideband or a dedicated table consumable by Q10 for prioritization. (Writer SHIPPED to local Parquet at `data/oracle/agreement/` per Q2-ADR-004; Q3 sideband routing per ADR-020 awaits Q10 boot.)
- **[SHIPPED]** **Read — Q1 binary state:** Q2 deserializes Q1's versioned blob via the adapter (per ADR-011). (Hand-rolled proto3 + M1 reader per Q2-ADR-001 §4; SHA-256 trailer verified on every read.)
- **[SHIPPED]** **Read — Q4 tokens:** translates engine internal IDs to/from `CompactState` slots. (`engine/cpp/src/oracle/registry/`.)
- **[PHASE-1.5]** **Pull — Q7 metrics:** standard exposition. (No metrics endpoint shipped in `engine/cpp/` today.)

## Coupling [MIXED — see bullets]

- **[ASPIRATION (parked per project-lead grounding)]** **Afferent (in):** Q10 (consumes oracle-agreement signal for prioritized replay sampling); Q12 (uses verifier RPC for the `≥90% expectimax agreement` Phase 1 gate); **[SHIPPED]** CI (runs pinned regression set on every commit — `make q2-ci`, `sts2_oracle_tests`).
- **[SHIPPED]** **Efferent (out):** Q1 (consumes serialized state via M1 wire); Q4 (token IDs); **[PHASE-1.5]** Q7 (metrics).
- **[ASPIRATION (parked per project-lead grounding)]** **Indirect:** Q3 if oracle-agreement rows are routed through the experience store (per ADR-020, deferred until Q10 boots and consumes).

## Phase Expectations [MIXED — see bullets]

- **[SHIPPED]** **Phase 1.** Existing expectimax preserved as-is. Adapter (ADR-011) added when Q1 transitions from C++ prototype to C# headless. **[PHASE-1.5]** Pinned regression set extended to cover the Phase 1 encounter pool. (Phase-1A scope = cultist + Q2-ADR-006 framework; per-encounter coverage extends via Q2-ADR-007 data-table additions per the Path A campaign — ADR-029.)
- **[PHASE-2]** **Phase 2.** Pinned regression set covers card-pick decision points where the oracle can fully expand combat from each candidate offer.
- **[PHASE-3+]** **Phase 3+.** Oracle scope unchanged — it remains a *verifier on small states*. We do not try to expand the expectimax tractability frontier; we accept the network as the policy on large states and verify what we can.

## Open Risks

- **Tractability frontier.** Q2 only meaningfully labels states the oracle can fully expand. As deck/relic complexity grows, the fraction of states Q2 can verify shrinks. Mitigation: monitor coverage; do not let oracle-agreement metric become a low-information statistic.
- **Adapter version drift.** If Q1's binary schema changes faster than Q2 absorbs, agreement signal stalls. Mitigation: adapter version pinned in CI; Q1 schema changes block on Q2 adapter update.
- **Algorithm-version drift in the report rows.** A report row only makes sense relative to the oracle's algorithm version (scoring rule, TT shape). Mitigation: stamp algorithm SHA on every row; treat algorithm change as a regression-set rebuild.

## Wave-16 / Path A addendum (2026-05-17)

Q2-ADR-006/007/008 (ratified wave-16) generalize the substrate from cultist-hardcoded to a data-driven framework. Key impacts on this spec:

- **[SHIPPED]** Q2-ADR-002 Phase-1A scope (CULTISTS_NORMAL only) is superseded by Q2-ADR-006. The adapter now supports arbitrary `MonsterKind` entries; per-encounter coverage extends via Q2-ADR-007 data-table additions without new transition-code changes.
- **[SHIPPED]** Cultist oracle values are numerically identical pre/post refactor (`CultistSolveMatchesPreRefactor` regression test). `algorithm_sha` rotated (Q2-ADR-005); cultist values unchanged.
- **[PHASE-1.5]** ADR-029 (Path A campaign) is the pipeline-level roadmap for expanding encounter coverage. Wave-17 = LouseProgenitor; wave-18+ = remaining Phase-1 encounters. Each wave narrows the `encounter_not_in_cpp_engine` reject set.
- **[PHASE-1.5]** D3 fixture #5 (LouseProgenitorNormal) remains a reject-with-diagnostic until wave-17 lands.

No interface change to `verify()` RPC (Q2-ADR-003), oracle-agreement schema (Q2-ADR-004), or manifest (Q2-ADR-005). `CompactState` remains Q2-internal; no `contracts/schemas/` change.

## ADR-014..018 cascade addendum (2026-05-14)

The architecture-note cascade (ADR-009 amendment + ADR-014..018) does **not** change Q2's interface or scope:

- **[SHIPPED]** Q2's `verify()` RPC operates on `CompactState` and is **orthogonal to** `evaluate_combat()` — Q2 is the small-state verifier, not the run-conditioned combat policy. No interface change.
- **[ASPIRATION (parked per project-lead grounding)]** Oracle-agreement signal remains training-eligible per ADR-017 carve-out (it is a labeled comparison, not a path-counterfactual).
- **[SHIPPED]** Q2-ADR-002 Phase-1A scope (CULTISTS_NORMAL only) defines the agreement denominator; non-cultist states reject-with-diagnostic.
- **[PHASE-1.5]** The oracle-agreement row's `model_value` column needs a defined reduction policy now that the model's value head is `sample + summary` per ADR-014. **Phase-1A:** scalar `model_value` reduces from `summary.expected_hp_delta`. Full reduction policy (and possible additional columns to capture sample-level distributional comparison) deferred to Q10 boot, when Q2-ADR-004 schema promotes to `contracts/schemas/oracle-agreement/` (cross-quantum coordination event per ADR-001).
