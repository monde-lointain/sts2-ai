# Module: Content Registry (Q4)

> Stable identity for every game token across patches. The patch-adaptation lever (ADR-003); packaged as a versioned file inside every model artifact (ADR-010).

## Responsibilities

- **Token IDs.** Assign and preserve a stable integer ID for every card (per upgrade variant), relic, enemy archetype, buff/debuff, potion, event ID, and special token (`[CLS]`, `[CHAR_*]`, `[ACT_*]`, `[MASK]`, etc.).
- **Stability rules.** Unchanged content keeps its ID across patches. New content gets a new ID. Removed content goes to the deprecation log; its ID is *never* reused.
- **Card-text DSL.** Structured records describing each card's cost, type, and effects in a normalized form. Consumed by the card-description subnetwork (`scaling-strategy.md` §2.7) to bootstrap embeddings for new cards.
- **Versioned releases.** A registry version is a single signed artifact: `(version, token_table, deprecation_log, card_dsl, schema_version)`. Tagged in Git; published to the model artifact store (Q5) bundle.
- **Token-coherence regression battery.** A Q4-specific test suite that checks: no duplicate IDs, all referenced tokens exist, deprecation log monotonic, card DSL parses, embedding-init code can synthesize embeddings for every card from its DSL alone.

Out of scope at runtime: registry is read-only after model load. There is no "live registry service" to query during inference.

## Data Ownership

- **Token table** — `(token_id, kind, name, content_hash, since_version, deprecated_in?)`. Append-mostly; entries can be marked deprecated but never removed.
- **Deprecation log** — ordered list of `(token_id, deprecated_in_version, reason)`.
- **Card-text DSL records** — per-card structured records (cost, type, target type, effect chain). Versioned alongside the token table.
- **Registry-version manifest** — `(version, schema_version, parent_version, content_hash, signing_key)`. Becomes part of every model's provenance.
- **Token-coherence regression set** — fixed test cases checked by CI on every registry change.

No other quantum writes any of these. Updates flow through a release workflow (next §).

## Communication

- **Read — at-load:** every quantum that consumes the registry loads it from a model artifact path at startup (per ADR-010). No runtime RPCs.
- **Out-of-band — release workflow:**
  - Author edits the registry file under a PR.
  - CI runs the token-coherence regression battery.
  - On green, a signed registry artifact is built and uploaded to Q5.
  - The next training run that picks this artifact stamps it into model provenance.
- **No direct edges** to other quanta at runtime.

## Coupling

- **Afferent (in):** essentially every quantum at load time — Q1 (cross-references token IDs in state schemas), Q2, Q3 (trajectory schema references token IDs), Q5 (bundles the file), Q8, Q9, Q10, Q11, Q12.
- **Efferent (out):** none.
- **Indirect:** Git (source of truth); Q5 (delivery mechanism).

## Phase Expectations

- **Phase 1.** Minimal: cards/relics/enemies/buffs in the lead-character A0 encounter pool. Card-text DSL stubbed; embeddings learned from scratch. Token-coherence regression battery in CI from day one.
- **Phase 2.** DSL fleshed out for the full card pool of the lead character. Deprecation log scaffolded but unused.
- **Phase 3+.** First real patch test: a synthetic patch that adds and removes content; verify fine-tune from registry-only changes recovers the agent within bounded compute.

## Open Risks

- **Misnumbered token corrupts every model.** Mitigation: token-coherence battery; signed artifacts; reviewer sign-off on every registry release.
- **Forgetting to update the registry before a new training run** silently misaligns embeddings. Mitigation: training run config must reference a specific registry version SHA; mismatch is a startup-time fatal error.
- **DSL schema rot.** As STS2 introduces effect kinds we did not anticipate (e.g., new keyword classes), the DSL needs extension. Mitigation: DSL version is its own field; old training runs can be replayed against pinned DSL versions.
- **Single source of truth = single point of wrong** (re-stated from ADR-003).
