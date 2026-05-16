---
name: bumping-a-schema-version
description: Use when bumping a protobuf schema version in contracts/schemas/. Codifies proto edit → codegen → fixture sweep → multi-version reader test → ADR delta — the full sequence so v1.0-pinned tests don't silently break.
---

# Bumping a Schema Version

Schema changes are **cross-quantum coordination events** (ADR-001). Flag to project lead before starting if any other quantum reads the schema you're changing. Never merge a schema bump silently.

The `.claude/hooks/proto-edit-tracker.py` hook (Wave 1.4) writes to `.claude/state/proto-edits-pending-adr.json` on any proto edit. The pre-push gate blocks main-bound push until an ADR references the edit.

## Step 1 — Edit the proto

File location: `contracts/schemas/<schema-name>/<schema-name>.proto`

Rules:
- **Additive only** for backward-compatible bumps (new fields, new tag numbers). Never reuse tag numbers.
- **Breaking changes** (rename, remove, reorder) require a new major version and a migration plan.
- Add a comment naming the version and ADR: `// v1.1 — ADR-NNN: <rationale>`

Exemplar: ADR-019 added `gold_shadow_price` and `max_hp_shadow_price` to `macro_context` as new tag numbers (v1.0 → v1.1, additive). ADR-022 moved the trajectory binding home to `pipeline/common/`.

## Step 2 — Regenerate bindings

```bash
make schema-codegen
```

This regenerates under `contracts/generated/{cpp,csharp,python}/`. These files are git-tracked — commit the regenerated output alongside the `.proto` change.

## Step 3 — Sweep v(N-1)-pinned fixtures

Find all test fixtures that are pinned to the old version:

```bash
grep -r "v1\.0\|version.*pinned\|_v1_" pipeline/ engine/ --include="*.py" --include="*.json" --include="*.bin" -l
```

For each fixture:
1. **Update** the fixture to the new schema version (add new fields with default/sentinel values).
2. **Retain a v(N-1) copy** named `<fixture>_v1_pinned.<ext>` for the multi-version reader test (Step 4). Do not delete old fixtures — they are the back-compat regression harness.

## Step 4 — Multi-version reader test

Ensure the reader can parse both vN and v(N-1) fixtures:

```python
# Pattern: parameterize over old + new fixtures
@pytest.mark.parametrize("fixture", ["episode_v1_pinned.bin", "episode_v1.1.bin"])
def test_reader_parses(fixture):
    ...
```

Run the relevant gate:

```bash
make q3-ci    # if trajectory schema (Q3 is the reader for Experience Store)
make q10-ci   # if the Q10 trainer reader is affected
```

Both must be green before proceeding.

## Step 5 — Update module specs

Any module spec under `docs/specs/modules/<quantum-slug>.md` that references the schema must be updated. Search for the old version string and replace:

```bash
grep -r "v1\.0\|macro_context v1" docs/specs/modules/
```

## Step 6 — ADR

Every proto version bump requires an ADR. Invoke [[creating-an-adr]]:

- Title: `<Schema Name> vN → vN.1 Bump` (or similar)
- Decision section: list the new fields, tag numbers, and the transition convention for in-flight rows
- Consequences: call out downstream consumers (quanta) that must handle defaults for old rows
- Set `adr_ref` in `.claude/state/proto-edits-pending-adr.json` once the ADR is written (clears the pre-push block)

## Step 7 — Commit order

Preferred commit order within the PR:
1. `.proto` edit + `contracts/generated/` regeneration (one commit)
2. Fixture sweep + retained v(N-1) copies (one commit)
3. Module spec + ADR update (one commit)

## Common mistakes

- **Regenerating without committing** — generated files silently diverge; always commit `contracts/generated/` with the `.proto` change.
- **Deleting old fixtures** — breaks the multi-version reader test.
- **No ADR** — pre-push hook blocks main-bound push; `.claude/state/proto-edits-pending-adr.json` will have a pending entry.
- **Forgetting Q10** — trajectory schema changes affect both Q3 (writer) and Q10 (reader); test both.

## Cross-references

- [[creating-an-adr]] — mandatory for every schema bump
- ADR-019 — `macro_context` v1.0 → v1.1 exemplar (additive fields)
- ADR-022 — trajectory binding move exemplar (cross-quantum coordination)
- `contracts/schemas/trajectory/trajectory.proto` — primary schema
- `pipeline/common/trajectory_proto.py` — generated binding (ADR-022 home)
