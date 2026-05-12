# StateBlobDumper

Debugging companion for the Q1 M1 State Codec. Reads a `.blob` file produced
by `Sts2Headless.Adapters.StateCodec.StateCodec.Serialize` and emits a
human-readable and machine-parseable JSONL stream describing the envelope and
every section.

The dumper is the canonical way to diagnose Q2 oracle-adapter mismatches:
when a Q2 adapter's decode disagrees with Q1's bytes, diff this dumper's
output against the Q2 side's decode to localize the divergence.

## Usage

```
dotnet run --project tools/StateBlobDumper -- <path-to-state.blob>
```

Run from the headless solution root (`engine/headless/`). The single positional
argument is the path to a blob file (e.g., one of the
`test/fixtures/state-blobs/<slot>/state.blob` fixtures).

Exit codes:

| code | meaning                                                        |
|------|----------------------------------------------------------------|
| 0    | Decode + emit succeeded.                                       |
| 1    | Usage error (wrong arg count, missing file path).              |
| 2    | Decode error (corrupt blob, schema mismatch, trailer hash).    |

## Output format

The dumper emits one JSON object per line to stdout. Lines are emitted in
order:

1. One `envelope` line.
2. One `section` line per decoded section, in on-wire order.
3. One `canonical-hash` line — lowercase-hex SHA-256 over the full blob bytes
   (matches the recipe used by the Q2 handoff fixtures'
   `expected_canonical_hash_hex`).

Errors go to stderr as an `error` line with `code` + `message` keys.

### Envelope line

```jsonc
{
  "kind": "envelope",
  "path": "<file path>",
  "blob_bytes": <int>,
  "schema_version": <int>,
  "trailer_validated": true,
  "manifest_stamp": {
    "git_sha": "<utf8 string>",
    "build_id": "<utf8 string>",
    "content_hash_hex": "<64-char lowercase hex>"
  },
  "section_count": <int>
}
```

### Section line

Common keys: `kind = "section"`, numeric `id`, enum-string `name`,
`size_bytes`, and a section-specific `body` object.

For `Rng` (`id=0`):

```jsonc
{ "kind": "section", "id": 0, "name": "Rng", "size_bytes": <int>,
  "body": { "run_rng_seed": <int>, "player_rng_seed": <int> } }
```

The M5 RNG buckets are opaque from M1's view; the dumper surfaces the seed
identifiers so a human can sanity-check the boot at-a-glance.

For `Tokens` (`id=1`):

```jsonc
{ "kind": "section", "id": 1, "name": "Tokens", "size_bytes": <int>,
  "body": {
    "count": <int>,
    "preview": [{"token": "<id>", "id": <int>}, ...]    // first 8 entries
  } }
```

For `CombatState` (`id=2`):

```jsonc
{ "kind": "section", "id": 2, "name": "CombatState", "size_bytes": <int>,
  "body": {
    "turn_counter": <int>,
    "phase": "<enum string>",
    "energy": <int>, "base_energy_per_turn": <int>, "hand_draw_size": <int>,
    "player_rng_counter": <int>, "monster_rng_counter": <int>,
    "attacks_played_this_turn": <int>, "cards_drawn_this_combat": <int>,
    "last_spent_energy": <int>, "exhausted_shiv_count": <int>,
    "player": <creature-summary>,
    "enemy_count": <int>, "enemies": [<creature-summary>, ...],
    "pile_sizes": {"draw":N, "hand":N, "discard":N, "exhaust":N}
  } }
```

Creature summary:

```jsonc
{
  "id": <int>, "name": "<utf8 string>",
  "current_hp": <int>, "max_hp": <int>, "block": <int>,
  "is_player": <bool>, "power_count": <int>,
  "powers": [
    { "model_id": "<id>", "stacks": <int>,
      "source_creature_id": <int>, "just_applied": <bool> },
    ...
  ],
  "intent": {       // only for monsters with an intent
    "kind": "<enum string>",
    "damage_per_hit": <int>, "hit_count": <int>,
    "move_id": "<utf8 string>",
    "applies_powers": [{"power_id":"<id>", "stacks":<int>}, ...]
  }
}
```

### Canonical-hash line

```jsonc
{ "kind": "canonical-hash", "sha256_hex": "<64-char lowercase hex>" }
```

This SHA-256 matches `CanonicalHash.Sha256Hex(blob)`. For the Q1 handoff
fixtures it should equal each `metadata.json`'s `expected_canonical_hash_hex`.

### Unknown section ids

When the dumper encounters a section id it doesn't understand (Phase 2+
additions before this tool ships an update), it emits:

```jsonc
{ "kind": "section", "id": <int>, "name": "<int>", "size_bytes": <int>,
  "body": { "unknown": true, "size_bytes": <int> } }
```

The blob is still valid; the dumper just doesn't have a pretty-print yet.

### Decode errors per section

If the codec accepts the blob but the per-section reader fails (the section
header parsed correctly but the body is structurally inconsistent), the
`body` for that section is:

```jsonc
{ "decode_error": "<message>", "size_bytes": <int> }
```

Other sections continue to render normally. This makes the dumper useful as
a forensic tool even on partially-corrupt blobs.

## Pairing with the fixture corpus

The six Q2 handoff fixtures at
`test/fixtures/state-blobs/<slot>/state.blob` are the canonical inputs for
this tool. The fixtures' `metadata.json` carries the expected canonical
hash; running the dumper and grepping the last line should produce the
same hex string.

```sh
dotnet run --project tools/StateBlobDumper -- \
  test/fixtures/state-blobs/01-cultists-normal-seed42/state.blob \
  | tail -1
# {"kind":"canonical-hash","sha256_hex":"<expected_canonical_hash_hex>"}
```
