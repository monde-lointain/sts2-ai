# Module: Oracle Verifier (Q2)

> C++ expectimax solver. Ground-truth labels for tractable states; engine→CompactState adapter (ADR-011); the gold standard the network is regression-tested against.

## Responsibilities

- Run expectimax over `CompactState` to produce optimal `(value, action)` for any state small enough to fully expand. Existing capability today; preserved per ADR-004.
- Emit the **oracle-agreement signal**: for any state the oracle can solve in budget, compare the network's top-1 action and value with the oracle's. Drives prioritized labeling at Q10.
- Maintain the **pinned-seed regression set** (`tools/seed-pinner` pattern, today at `tests/seeds/expected_values.h`). Generalize this from one encounter to a per-component battery.
- **Engine→CompactState adapter (ADR-011).** Consume Q1's versioned binary state and derive `CompactState` for verifier use.
- Expose a "verify this state" RPC for Q12 to invoke during evaluation.

Out of scope: anything network-side. Q2 does not call the inference server, does not load weights, does not produce trajectories.

## Data Ownership

- **`CompactState` canonical type** — existing C++ POD in `include/sts2/ai/state.h`. Hashable; static_assert chains anchor card-counts indexing to `CardId` enum order.
- **Pinned regression seed set** — `(seed, encounter_id, expected_value, expected_top_action)` tuples. Versioned per content patch.
- **Oracle-agreement report rows** — `(state_hash, oracle_action, oracle_value, model_action, model_value, model_version, timestamp)`. Append-only.
- **Expectimax algorithm version manifest** — algorithm SHA, transposition table parameters, scoring rule version. Stamped on every report row.

## Communication

- **Sync — RPC (cold path):** `verify(state_blob) → {value, action, expansion_complete}` with a budget; Q12 calls during evaluation.
- **Async — file/queue:** oracle-agreement report rows pushed to a Q3 sideband or a dedicated table consumable by Q10 for prioritization.
- **Read — Q1 binary state:** Q2 deserializes Q1's versioned blob via the adapter (per ADR-011).
- **Read — Q4 tokens:** translates engine internal IDs to/from `CompactState` slots.
- **Pull — Q7 metrics:** standard exposition.

## Coupling

- **Afferent (in):** Q10 (consumes oracle-agreement signal for prioritized replay sampling); Q12 (uses verifier RPC for the `≥90% expectimax agreement` Phase 1 gate); CI (runs pinned regression set on every commit).
- **Efferent (out):** Q1 (consumes serialized state); Q4 (token IDs); Q7 (metrics).
- **Indirect:** Q3 if oracle-agreement rows are routed through the experience store.

## Phase Expectations

- **Phase 1.** Existing expectimax preserved as-is. Adapter (ADR-011) added when Q1 transitions from C++ prototype to C# headless. Pinned regression set extended to cover the Phase 1 encounter pool.
- **Phase 2.** Pinned regression set covers card-pick decision points where the oracle can fully expand combat from each candidate offer.
- **Phase 3+.** Oracle scope unchanged — it remains a *verifier on small states*. We do not try to expand the expectimax tractability frontier; we accept the network as the policy on large states and verify what we can.

## Open Risks

- **Tractability frontier.** Q2 only meaningfully labels states the oracle can fully expand. As deck/relic complexity grows, the fraction of states Q2 can verify shrinks. Mitigation: monitor coverage; do not let oracle-agreement metric become a low-information statistic.
- **Adapter version drift.** If Q1's binary schema changes faster than Q2 absorbs, agreement signal stalls. Mitigation: adapter version pinned in CI; Q1 schema changes block on Q2 adapter update.
- **Algorithm-version drift in the report rows.** A report row only makes sense relative to the oracle's algorithm version (scoring rule, TT shape). Mitigation: stamp algorithm SHA on every row; treat algorithm change as a regression-set rebuild.

## ADR-014..018 cascade addendum (2026-05-14)

The architecture-note cascade (ADR-009 amendment + ADR-014..018) does **not** change Q2's interface or scope:

- Q2's `verify()` RPC operates on `CompactState` and is **orthogonal to** `evaluate_combat()` — Q2 is the small-state verifier, not the run-conditioned combat policy. No interface change.
- Oracle-agreement signal remains training-eligible per ADR-017 carve-out (it is a labeled comparison, not a path-counterfactual).
- Q2-ADR-002 Phase-1A scope (CULTISTS_NORMAL only) defines the agreement denominator; non-cultist states reject-with-diagnostic.
- The oracle-agreement row's `model_value` column needs a defined reduction policy now that the model's value head is `sample + summary` per ADR-014. **Phase-1A:** scalar `model_value` reduces from `summary.expected_hp_delta`. Full reduction policy (and possible additional columns to capture sample-level distributional comparison) deferred to Q10 boot, when Q2-ADR-004 schema promotes to `contracts/schemas/oracle-agreement/` (cross-quantum coordination event per ADR-001).
