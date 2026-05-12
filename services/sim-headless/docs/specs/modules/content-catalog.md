# Module: Content Catalog (M7)

> Static content registry. Enumeration of all cards, relics, powers, monsters, encounters; pool / rarity tables; internal-ID ↔ Q4-token-ID mapping. Read-only at runtime. Built at process init from M6c class metadata.

## Responsibilities

- Enumerate all content known to Q1: cards (`CardModel` subclasses), relics (`RelicModel`), powers (`PowerModel`), monsters (`MonsterModel`), encounters (`EncounterModel`), events (`EventModel`), acts (`ActModel`).
- Provide lookup by internal ID: `GetCard(CardId) → CardModel`, `GetRelic(RelicId) → RelicModel`, etc. Stable IDs across patches per pipeline ADR-003.
- Provide pool and rarity tables: card-reward pools per character / rarity / floor; relic-reward pools per ascension / drop tier; encounter pools per act / floor; event pools per act.
- Map internal IDs to Q4 token IDs and back: `InternalToToken(internal_id) → token_id`, `TokenToInternal(token_id) → internal_id`. Q4 manifest loaded at init (per pipeline ADR-010, Q4 ships with model artifact).
- Validate content coverage at init: every concrete `CardModel` / `RelicModel` / etc. subclass under M6c has a registry entry. Missing entries fail process startup.
- Validate Q4 mapping coverage at init: every internal ID has a Q4 token; every Q4 token has an internal ID (or is explicitly deprecated in Q4's deprecation log). Mismatch fails startup.
- Provide content metadata: tags, keywords, character ownership, rarity, target type. Used by M6a / M6b / M6c for content-aware decisions (e.g., card-reward filtering by character).

`[Phase 1 scope]` — registry built for the Phase-1 reference encounter (Silent + Ring of the Snake vs CULTISTS_NORMAL): ~10-20 cards, ~5 relics, ~5 powers, 2 monsters. Q4 manifest minimal.

`[Phase 2]` — full Silent character × Act 1.

`[Phase 3+]` — full content for all characters × all acts, with Q4 manifest stable across patches.

Out of scope: content *behaviors* (M6c — M7 is the index, M6c is the implementation); per-instance content state (M6a / M6b / M6c); writing to Q4 (Q4 is read-only here, owned by the model-registry pipeline).

## Data Ownership

M7 owns in-process content tables, not external schemas. Q4 manifest is read at init but its schema is owned by Q4 (pipeline-level Content Registry).

- **`ContentTable<T>`** — generic registry: `Dictionary<TId, T>` for lookup, plus iteration order pinned to declaration order.
- **`CardCatalog : ContentTable<CardId, CardModel>`** — all cards.
- **`RelicCatalog`**, **`PowerCatalog`**, **`MonsterCatalog`**, **`EncounterCatalog`**, **`EventCatalog`**, **`ActCatalog`** — analogous.
- **`CardPoolTable`** — card-reward pools indexed by `(CharacterId, Rarity, Floor)` → list of `CardId` with weights.
- **`RelicPoolTable`** — relic drop pools indexed by `(Tier, Ascension)` → list of `RelicId` with weights.
- **`EncounterPoolTable`** — per-act monster encounter pools indexed by `(ActId, Floor, EncounterType)` → list of `EncounterId`.
- **`TokenMap`** — `InternalId ↔ TokenId` bidirectional mapping. Loaded from Q4 manifest at init.
- **`TokenManifest`** — Q4 schema version, token-registry SHA, load-time integrity hash. Stamped into M1's Game Version Manifest.

All tables are constructed once at process init, then frozen. Mutation after init throws — caught by tests.

## Communication

### Synchronous (in-process calls)

- **Inbound:** lookup queries from M6a / M6b / M6c / M6d at runtime. All read-only.
- **Inbound:** Q4 manifest path from M9 at init.
- **Outbound:** none at runtime. At init, M7 reflects over M6c class hierarchy to enumerate content (one-time reflection cost).

### Asynchronous

- None. Static and read-only.

### Events emitted

- None.

## Coupling

- **Afferent (in):** M6a, M6b, M6c, M6d (lookup queries); M9 Process Host (init wiring, Q4 manifest path).
- **Efferent (out):** M6c Content Behaviors (read class metadata at init via reflection).
- **Indirect:** Q4 (Content Registry) — read at init via the bundled manifest file (pipeline ADR-010); not called at runtime.

Aim: zero runtime efferent dependencies. M7 is a frozen lookup table after init.

## Testing Strategy

### Unit Tests

Mock M9 (config), filesystem (Q4 manifest read). Focus on lookup correctness and table integrity:

- **Lookup correctness:** `GetCard(StrikeCardId) → StrikeIronclad` instance; `GetCard(unknown_id) → throws`.
- **Pool table integrity:** for `(Silent, Common, Floor=1)`, returned card list matches expected golden manifest.
- **ID-mapping injectivity:** `InternalToToken` and `TokenToInternal` are inverse functions; no duplicates; no orphans.
- **Coverage gate:** at init, assert every concrete `CardModel` subclass under M6c has a `CardCatalog` entry. Missing entry fails init with explicit list of missing classes.
- **Q4 mapping coverage:** every `CardId` in `CardCatalog` has a Q4 token; every Q4 card-token has a `CardId` or appears in Q4's deprecation log.
- **Frozen-after-init:** mutating `CardCatalog` post-init throws.
- **Iteration order determinism:** iterating `CardCatalog` produces a stable order across process restarts (declaration order, not hash order).
- **Token manifest stamping:** the loaded Q4 manifest's SHA is stamped into M1's `GameVersionManifest` for round-trip provenance.

### Integration Tests

Verify M7's quantum boundaries:

- **Q4 manifest contract:** load a fixture Q4 manifest; verify token mapping populated; load a fixture Q4 manifest with a deprecated entry; verify deprecation handled (no missing-token error for deprecated tokens).
- **Q4 schema-version mismatch:** load a fixture Q4 manifest with a schema version Q1 doesn't support; verify init fails with explicit error, not silent acceptance.
- **Content-coverage CI gate:** the unit test above (every concrete subclass registered) runs as a CI gate. PRs that add a new `CardModel` subclass without registering it fail the build.
- **Pool-content integrity vs Godot:** for the Phase-1 reference run, encounter pool selection through M7 produces the same encounter sequence as upstream Godot's `RunManager` for the same seed.
- **State-codec roundtrip:** `GameVersionManifest` (which includes Q4 token-registry SHA per pipeline ADR-010) roundtrips through M1 and reproduces identically. A different Q4 SHA at restore fails with explicit version-mismatch error.
