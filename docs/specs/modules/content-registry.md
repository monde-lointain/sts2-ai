# Module: Content Registry (Q4)

> Stable identity for every game token across patches. The patch-adaptation lever (ADR-003); packaged as a versioned file inside every model artifact (ADR-010).

## Responsibilities [MIXED — see bullets]

- **[SHIPPED]** **Token IDs.** Assign and preserve a stable integer ID for every card (per upgrade variant), relic, enemy archetype, buff/debuff, potion, event ID, and special token (`[CLS]`, `[CHAR_*]`, `[ACT_*]`, `[MASK]`, etc.). Phase-1 seed: 96 cards / 58 relics / 45 powers / 32 monsters / 21 potions / 22 encounters registered in `contracts/registry/phase1-silent.json`; duplicate-ID rejection enforced by `tools/content/validate_registry.py`.
- **[SHIPPED]** **Stability rules.** Unchanged content keeps its ID across patches. New content gets a new ID. Removed content goes to the deprecation log; its ID is *never* reused. (Reuse-rejection + deprecation-log monotonicity both enforced by `validate_registry.py` and exercised by `tools/tests/content/test_registry.py`.)
- **[SHIPPED (stubbed payload)]** **Card-text DSL.** Structured records describing each card's cost, type, and effects in a normalized form. Consumed by the card-description subnetwork (`scaling-strategy.md` §2.7) to bootstrap embeddings for new cards. (Record shape `(token, cost, type, target, effects[])` is shipped and schema-validated; **[PHASE-2]** Phase-1 seed fills `cost/type/target = "unknown"` and `effects = [{op: "stub", source: "phase1-seed"}]` per `seed_phase1_registry.py` — semantic DSL population deferred to Phase 2 alongside lead-character card-pool buildout.)
- **[ASPIRATION (pre-implementation)]** **Versioned releases.** A registry version is a single signed artifact: `(version, token_table, deprecation_log, card_dsl, schema_version)`. Tagged in Git; published to the model artifact store (Q5) bundle. (`phase1-silent.json` ships unsigned today — no signing-key ceremony, no signature field; Q5 model-artifact bundling pathway awaits Q5 boot.) [NOTE: contradicts code at `contracts/registry/phase1-silent.json` — no signature field; tracking via ADR-010 Q5-boot follow-up.]
- **[SHIPPED]** **Token-coherence regression battery.** A Q4-specific test suite that checks: no duplicate IDs, all referenced tokens exist, deprecation log monotonic, card DSL parses. (`make content-test` → `tools/tests/content/test_registry.py`, wired into `make phase0-gate`; 7 invariant test cases pass on `phase1-silent.json`.) **[ASPIRATION (pre-implementation)]** Embedding-init code that synthesizes embeddings for every card from its DSL alone — no such embedding-init exists today; depends on Phase-2 semantic DSL.

Out of scope at runtime: registry is read-only after model load. There is no "live registry service" to query during inference.

## Data Ownership [MIXED — see bullets]

- **[SHIPPED]** **Token table** — `(token_id, kind, name, content_hash, since_version, deprecated_in?)`. Append-mostly; entries can be marked deprecated but never removed. (Shipped in `phase1-silent.json` `tokens[]`; `deprecated_in` field honored by `validate_registry.py`.)
- **[SHIPPED]** **Deprecation log** — ordered list of `(token_id, deprecated_in_version, reason)`. (Empty in Phase-1 seed; schema validated and monotonicity test guards the append path.)
- **[SHIPPED (stubbed payload)]** **Card-text DSL records** — per-card structured records (cost, type, target type, effect chain). Versioned alongside the token table. (Record shape shipped; **[PHASE-2]** semantic content deferred — see Responsibilities §.)
- **[ASPIRATION (pre-implementation)]** **Registry-version manifest** — `(version, schema_version, parent_version, content_hash, signing_key)`. Becomes part of every model's provenance. (Today `phase1-silent.json` carries only a `manifest: {}` object; `parent_version`, `content_hash`, `signing_key` not populated; provenance plumbing awaits Q5 boot.)
- **[SHIPPED]** **Token-coherence regression set** — fixed test cases checked by CI on every registry change. (`tools/tests/content/test_registry.py`, 7 cases; runs in `make phase0-gate`.)

No other quantum writes any of these. Updates flow through a release workflow (next §).

## Communication [MIXED — see bullets]

- **[SHIPPED]** **Read — at-load:** every quantum that consumes the registry loads it from a model artifact path at startup (per ADR-010). No runtime RPCs. (Q1 reader shipped at `engine/headless/src/Sts2Headless.Domain/Content/Q4ManifestLoader.cs` — strict JSON parser; unknown-root-key rejection; `Q4ManifestFormatException` on every schema mismatch. Q10 reader shipped at `pipeline/trainer/content_registry.py`.)
- **Release workflow:**
  - **[SHIPPED]** Author edits the registry file under a PR.
  - **[SHIPPED]** CI runs the token-coherence regression battery. (`make content-test` in `make phase0-gate`.)
  - **[ASPIRATION (pre-implementation)]** On green, a signed registry artifact is built and uploaded to Q5. (No signing step or Q5 upload exists today.)
  - **[ASPIRATION (pre-implementation)]** The next training run that picks this artifact stamps it into model provenance. (Provenance plumbing awaits Q5 boot.)
- **[SHIPPED]** **No direct edges** to other quanta at runtime.

## Coupling [MIXED — see bullets]

- **Afferent (in):** essentially every quantum at load time — **[SHIPPED]** Q1 (`Q4ManifestLoader` consumes `phase1-silent.json` tokens); **[SHIPPED]** Q10 (`pipeline/trainer/content_registry.py` reader); **[ASPIRATION (pre-implementation)]** Q2, Q3 (trajectory schema references token IDs), Q5 (bundles the file), Q8, Q9, Q11, Q12 — afferent quanta are scaffolded only or dormant; cross-references are forward-laid.
- **[SHIPPED]** **Efferent (out):** none.
- **[SHIPPED]** **Indirect:** Git (source of truth — `contracts/registry/` checked in). **[ASPIRATION (pre-implementation)]** Q5 (delivery mechanism — awaits Q5 boot).

## Phase Expectations [MIXED — see bullets]

- **[SHIPPED]** **Phase 1.** Minimal: cards/relics/enemies/buffs in the lead-character A0 encounter pool. Card-text DSL stubbed; embeddings learned from scratch. Token-coherence regression battery in CI from day one. (Seed registered via `tools/content/seed_phase1_registry.py`; regression battery active in `make phase0-gate`; DSL stubs explicit per `seed_phase1_registry.py`.)
- **[PHASE-2]** **Phase 2.** DSL fleshed out for the full card pool of the lead character. Deprecation log scaffolded but unused. (Deprecation log already scaffolded today; DSL semantic-population is the open Phase-2 work.)
- **[PHASE-3+]** **Phase 3+.** First real patch test: a synthetic patch that adds and removes content; verify fine-tune from registry-only changes recovers the agent within bounded compute.

## Open Risks

- **Misnumbered token corrupts every model.** Mitigation: token-coherence battery; signed artifacts; reviewer sign-off on every registry release.
- **Forgetting to update the registry before a new training run** silently misaligns embeddings. Mitigation: training run config must reference a specific registry version SHA; mismatch is a startup-time fatal error.
- **DSL schema rot.** As STS2 introduces effect kinds we did not anticipate (e.g., new keyword classes), the DSL needs extension. Mitigation: DSL version is its own field; old training runs can be replayed against pinned DSL versions.
- **Single source of truth = single point of wrong** (re-stated from ADR-003).
