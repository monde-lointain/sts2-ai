# Q1 GremlinMerc Port — Engineer Prompt

**Originated**: 2026-05-19 (Q2 wave-26 M.0 cross-quantum coordination)
**Requesting party**: Q2 oracle (engine/cpp); wave-26 ports GremlinMerc + Surprise OnDeath spawn
**Q1 lead**: TBA by user
**Estimated scope**: ~600-900 LOC across Domain + Powers + Tests + fixture corpus

---

## Goal

Port the **GremlinMerc**, **SneakyGremlin**, **FatGremlin** monsters + the **SurprisePower** + the **GremlinMercNormal** encounter from upstream STS2 (Godot/C#) into Q1 (`engine/headless/`). Generate state-blob fixture `09-gremlin-merc-normal-seed42` at canonical seed 42, suitable for Q2 oracle adapter consumption.

Q2 wave-26 has 6 streams already specified (M.0 ceremony / M.α substrate / M.β monsters / M.γ adapter+pin / M.δ docs / M.ε gate+tag). **Q2 M.γ gates on Q1 fixture 09 availability.** Q2's M.α + M.β substrate work proceeds in parallel and DOES NOT block on Q1 work.

## Upstream source (authoritative reference)

**Monsters**:
- `/home/clydew372/development/projects/godot/sts2/src/Core/Models/Monsters/GremlinMerc.cs`
- `/home/clydew372/development/projects/godot/sts2/src/Core/Models/Monsters/SneakyGremlin.cs`
- `/home/clydew372/development/projects/godot/sts2/src/Core/Models/Monsters/FatGremlin.cs`

**Power**:
- `/home/clydew372/development/projects/godot/sts2/src/Core/Models/Powers/SurprisePower.cs`

**Encounter**:
- `/home/clydew372/development/projects/godot/sts2/src/Core/Models/Encounters/GremlinMercNormal.cs`

**Ascension scope**: **A0 only** per Q2-ADR-002 Phase-1A. Use the 3rd argument to `AscensionHelper.GetValueIfAscension(level, A11_val, A0_val)` for damage / HP values.

## Mechanics spec (A0; line-cited)

### GremlinMerc per-monster

| Field | A0 Value | Upstream cite |
|---|---|---|
| HP min | 47 | GremlinMerc.cs:28 |
| HP max | 49 | GremlinMerc.cs:30 |
| `GIMME_MOVE` damage | 7 | GremlinMerc.cs:36 |
| `GIMME_MOVE` hit count | 2 | GremlinMerc.cs:38 |
| `DOUBLE_SMASH_MOVE` damage | 6 | GremlinMerc.cs:40 |
| `DOUBLE_SMASH_MOVE` hit count | 2 | GremlinMerc.cs:42 |
| `DOUBLE_SMASH_MOVE` applies Weak | 2 (to player) | GremlinMerc.cs:109 |
| `HEHE_MOVE` damage | 8 | GremlinMerc.cs:44 |
| `HEHE_MOVE` hit count | 1 | GremlinMerc.cs:63 (SingleAttackIntent) |
| `HEHE_MOVE` self-Strength gain | +2 (permanent stack) | GremlinMerc.cs:122 |
| Spawn powers (AfterAddedToRoom) | `SurprisePower(1)` + `ThieveryPower(20/player)` | GremlinMerc.cs:49,54 |

### Move rotation

Strict 3-cycle: **GIMME → DOUBLE_SMASH → HEHE → GIMME → ...** (no RandomBranch; no CannotRepeat).

Move construction at `GremlinMerc.cs:61-66`; initial move = GIMME at `GremlinMerc.cs:70`.

### SneakyGremlin per-monster

| Field | A0 Value | Upstream cite |
|---|---|---|
| HP min | 10 | SneakyGremlin.cs:21 |
| HP max | 14 | SneakyGremlin.cs:23 |
| `SPAWNED_MOVE` | StunIntent — NO damage (spawn-turn stun) | SneakyGremlin.cs:49 |
| `TACKLE_MOVE` damage | 9 | SneakyGremlin.cs:25 (A0 value; 3rd arg of `GetValueIfAscension(DeadlyEnemies, 10, 9)`) |
| `TACKLE_MOVE` hit count | 1 | SneakyGremlin.cs:50 (SingleAttackIntent) |
| Move rotation | SPAWNED (once) → TACKLE (self-loop) | SneakyGremlin.cs:51 |
| Initial move | SPAWNED | SneakyGremlin.cs:54 |
| Spawn powers | None | — |

### FatGremlin per-monster

| Field | A0 Value | Upstream cite |
|---|---|---|
| HP min | 13 | FatGremlin.cs:28 |
| HP max | 17 | FatGremlin.cs:30 |
| `SPAWNED_MOVE` | StunIntent — NO damage | FatGremlin.cs:52 |
| `FLEE_MOVE` | EscapeIntent — removes self from combat; NO damage | FatGremlin.cs:53,75 |
| Move rotation | SPAWNED (once) → FLEE (self-loop; flees once + removed) | FatGremlin.cs:54 |
| Initial move | SPAWNED | FatGremlin.cs:57 |
| Spawn powers | None | — |

### SurprisePower (OnDeath spawn mechanic)

| Field | Value | Upstream cite |
|---|---|---|
| Hook | `AfterDeath` | SurprisePower.cs:16 |
| Guard | `Owner == target` AND `!wasRemovalPrevented` | SurprisePower.cs:18 |
| Effect | `CreatureCmd.Add<SneakyGremlin>("sneaky")` + `CreatureCmd.Add<FatGremlin>("fat")` | SurprisePower.cs:22-23 |
| StackType | `Single` (one-shot per stack) | SurprisePower.cs:14 |
| `ShouldStopCombatFromEnding` | Returns `true` (delays victory until summons exist) | SurprisePower.cs:33-34 |
| Bonus (NOT MODELED in Q2 — combat-only oracle) | Transfers Thievery gold → Heist on FatGremlin | SurprisePower.cs:24-28 |

### GremlinMercNormal encounter

| Field | A0 Value | Upstream cite |
|---|---|---|
| Initial composition | **1 enemy — GremlinMerc only** ("merc" slot) | GremlinMercNormal.cs:28 |
| Slot constants | "merc", "sneaky", "fat" | GremlinMercNormal.cs:9-13 |
| `AllPossibleMonsters` | {GremlinMerc, FatGremlin, SneakyGremlin} | GremlinMercNormal.cs:19-24 |
| Encounter RNG | NONE | — |
| Surprise spawn timing | Deterministic; fires on GremlinMerc death | SurprisePower.cs:22-23 |

**Critical: at fight start, only ONE enemy exists** (GremlinMerc at "merc" slot). SneakyGremlin and FatGremlin appear ONLY after GremlinMerc dies, via SurprisePower.AfterDeath.

## Q1 file paths to create / modify

### New files

- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/GremlinMerc.cs`
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/SneakyGremlin.cs`
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/FatGremlin.cs`
- `engine/headless/src/Sts2Headless.Domain/Content/Powers/SurprisePower.cs`
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/GremlinMercNormal.cs`
- `engine/headless/test/Sts2Headless.Tests.Domain/Content/Monsters/GremlinMercTests.cs`
- `engine/headless/test/Sts2Headless.Tests.Domain/Content/Monsters/SneakyGremlinTests.cs`
- `engine/headless/test/Sts2Headless.Tests.Domain/Content/Monsters/FatGremlinTests.cs`
- `engine/headless/test/Sts2Headless.Tests.Domain/Content/Powers/SurprisePowerTests.cs`
- `engine/headless/test/Sts2Headless.Tests.Domain/Content/Encounters/GremlinMercNormalTests.cs`

### Modified files

- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/Phase1Monsters.cs` — register the 3 gremlin monsters in the Phase-1 monster catalog (mirror existing Nibbit / Cultist / Louse registrations).
- `engine/headless/src/Sts2Headless.Domain/Content/Powers/Phase1Powers.cs` — register SurprisePower (mirror existing Frail / Vulnerable / Strength / Weak registrations).
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/Phase1Encounters.cs` — register GremlinMercNormal in the Phase-1 encounter catalog (mirror existing CultistsNormal / NibbitsNormal registrations).
- `engine/headless/test/Sts2Headless.Tests.Tools/Fixtures/StateBlobFixtureRecipe.cs` — add NEW fixture recipe `09-gremlin-merc-normal-seed42` (mirror existing recipes at seed 42). **Capture at turn 1 / start of player's first turn.**
- `engine/headless/test/Sts2Headless.Tests.Domain/Content/Monsters/Phase1MonsterTests.cs` — likely needs GremlinMerc / SneakyGremlin / FatGremlin cases added (mirror existing per-monster test cases).
- `engine/headless/test/Sts2Headless.Tests.Domain/Content/Monsters/Phase1MonsterRotationTests.cs` — GremlinMerc rotation case (GIMME→DOUBLE_SMASH→HEHE verification).

### Generated artifacts (committed to repo)

- `engine/headless/test/fixtures/state-blobs/09-gremlin-merc-normal-seed42/state.blob` (binary blob)
- `engine/headless/test/fixtures/state-blobs/09-gremlin-merc-normal-seed42/README.md` (per existing fixture README convention)

Run `StateBlobFixtureGenerator` (existing tool at `engine/headless/test/Sts2Headless.Tests.Tools/Fixtures/`) to produce the blob. Existing CI test `StateBlobFixtureRegressionTests` verifies fixture stability.

## Wire format requirements (Q2 consumes these)

Per `engine/headless/src/Sts2Headless.Adapters/StateCodec/StateCodec.cs`:

- **`Enemy.Name`**: length-prefixed string. **Must be exactly** `"GremlinMerc"` / `"SneakyGremlin"` / `"FatGremlin"` for the respective monsters (matches upstream class names; Q2 adapter's `is_gremlin_merc_normal` predicate pattern-matches on the GremlinMerc name).
- **`MonsterIntent.MoveId`**: length-prefixed string. **Use upstream MoveDecider canonical names**: `"GIMME_MOVE"`, `"DOUBLE_SMASH_MOVE"`, `"HEHE_MOVE"`, `"SPAWNED_MOVE"`, `"TACKLE_MOVE"`, `"FLEE_MOVE"` (verify exact strings by reading the respective .cs files' MoveState constructions at the cite lines above).
- **`Power.Name`** for SurprisePower (encoded if the enemy has it): exactly `"SurprisePower"` (Q2 adapter projects this into `PowerKind::kSurprise`).
- **`Power.Name`** for ThieveryPower: exactly `"ThieveryPower"` (Q2 adapter silent-drops this per Q2-ADR-005 unknown-power infrastructure; encoding-side may emit or omit — Q2 handles both).
- **`Encounter_id`** (in fixture metadata / README): `"GremlinMercNormal"` (exact class name; no transformation).

## Fixture capture conventions (Q2 consumes these)

- **Capture point**: **turn 1 / start of player's first turn**. The single enemy is GremlinMerc; HP rolled in [47, 49]; block = 0; powers = {SurprisePower(1), ThieveryPower(20)}; current_move = GIMME_MOVE.
- **Seed**: 42 (canonical; matches existing cultist + Louse + slime + Nibbit fixtures at seed 42).
- **Player state**: Silent starter (HP 70, energy 3, standard 12-card starter deck: 5 Strike + 5 Defend + 1 Neutralize + 1 Survivor).

## `next_spawn_hps` metadata (OPTIONAL but PREFERRED for Q2-Q1 oracle-agreement bit-equality)

**Background**: when GremlinMerc dies, Q1's RNG determines the HP values for the spawned SneakyGremlin + FatGremlin (rolled in [10,14] and [13,17] respectively). Q2's expectimax search is deterministic and lacks the Q1 RNG, so Q2 falls back to deterministic median HPs (Sneaky=12, Fat=15) unless Q1 emits the pre-rolled values.

**Request**: If feasible, emit pre-rolled spawn HPs into the fixture's `metadata.json` at capture time:

```json
{
  "encounter_id": "GremlinMercNormal",
  "seed": 42,
  "capture_point": "turn_1_start",
  "next_spawn_hps": {
    "sneaky": 12,
    "fat": 15
  }
}
```

Q1's RNG can pre-roll the future Sneaky + Fat HP values at fixture-emission time (the RNG state at turn 1 deterministically produces them when the spawn happens later in the fight). Q2 adapter projection then reads these and routes through its `kSurpriseSpawnTable` for bit-equality between Q1 + Q2 oracle solves.

If Q1 cannot emit (cross-quantum schema change too invasive), Q2 falls back to B1 deterministic medians + accepts the pin-VALUE divergence at the oracle-agreement gate per Q2-ADR-029 Path A.

## Verification (Q1-side gate criteria)

Before declaring the port done:
- All GremlinMerc + SneakyGremlin + FatGremlin + SurprisePower unit tests PASS (mirror existing Nibbit + Cultist test density).
- `StateBlobFixtureRegressionTests` PASS — fixture 09 blob deterministic across re-generation.
- `q1-ci` (or equivalent Q1 CI target) GREEN.
- `dotnet test engine/headless/...` GREEN for relevant test projects.
- Fixture inspection: decode 09 fixture via `StateBlobDumper` tool; confirm:
  - Enemy.Name == "GremlinMerc" for the single slot.
  - MoveId == "GIMME_MOVE".
  - Powers list includes SurprisePower (1 stack) + ThieveryPower (20 stacks).
  - Capture point = turn 1 (enemy at initial HP, no buffs other than spawn powers, block=0).
  - If `next_spawn_hps` metadata implemented: Sneaky + Fat HPs present and within their A0 HP ranges.

## Coordination with Q2

- **Q2 wave-26 M.γ gates on Q1 fixture 09 availability**. Once fixture lands on main, Q2's M.γ engineer can dispatch.
- Q2's adapter dispatch: `{"GremlinMerc"} → "GremlinMercNormal"`. Single-enemy predicate keyed on `enemy_count == 1 && enemies[0].name == "GremlinMerc"`.
- If Q1's MoveId names differ from `"GIMME_MOVE"` / `"DOUBLE_SMASH_MOVE"` / `"HEHE_MOVE"` / `"SPAWNED_MOVE"` / `"TACKLE_MOVE"` / `"FLEE_MOVE"`, surface immediately to Q2 wave-26 lead (project-lead handles cross-quantum reconciliation; Q2's `move_calc.cc` wire-name mapping may need adjustment).
- If Q1 ports GremlinMerc with semantic differences from upstream (e.g., different HP range or move ordering), surface — Q2's pin VALUES will diverge from upstream-derived projections, and the oracle-agreement gate (Q12) will surface the divergence.
- **Note on SneakyGremlin TACKLE damage**: upstream `SneakyGremlin.cs:25` uses `AscensionHelper.GetValueIfAscension(DeadlyEnemies, 10, 9)`. The A0 value is the **3rd argument = 9**. Engineer verifies by reading the file directly; if interpretation is ambiguous, surface to Q2 lead.

## Tractability note (informational)

Q2 has audited the GremlinMerc encounter per the 4-criterion tractability screen (`[[project-encounter-tractability]]` memory + Q2-ADR-029 §Path A roadmap):

1. **Bounded combat duration**: GremlinMerc dies ~10 rounds (HP 48 / Silent ~5 dmg/turn net); Sneaky dies ~3 rounds (HP 12 / ~4 dmg/turn under TACKLE-induced Weak); Fat FLEEs at turn 2 post-spawn. Total combat ~13-17 rounds; well under Q2's `kSearchHorizonRounds = 25`.
2. **No unbounded status accumulation**: GremlinMerc applies Weak (bounded — ticks down 1/turn). No status cards injected. Surprise spawn is one-shot.
3. **Block does NOT dominate damage**: GremlinMerc Strength scaling (HEHE +2/cycle) pushes damage past Silent's 15-block budget by ~turn 6; optimal play offensive.
4. **Q1 fixture support**: this work item.

Q1's port should preserve these tractability properties — primarily: **don't accidentally remove Strength self-buff from HEHE** (the offensive-pressure mechanism) or otherwise reduce damage below 15-block budget after horizon.

## Out of scope

- Ascension > A0.
- HeistPower modeling (Q2 silent-drops; combat-only oracle).
- ThieveryPower gold-tracking (Q2 silent-drops; combat-only oracle).
- New encounters beyond GremlinMercNormal.
- Q2 work (handled in `engine/cpp/` wave-26 M.α onwards; independent of this port).

## Cross-references

- Q2 plan: `/home/clydew372/.claude/plans/plan-the-q2-oracle-glittery-pony.md` § "Wave-26: GremlinMerc port".
- Q2 tractability criteria: `[[project-encounter-tractability]]` memory.
- Q2 cultist BYTE rotation chain (informational): Q2-ADR-010 §Recovery; cultist Zobrist BYTE at `0x569115efa81a95dc / 0x9a06f1e505846a80` post-wave-25.
- Wave-24 Nibbit port prompt precedent: `/home/clydew372/development/projects/cpp/sts2-ai/docs/q1-nibbit-port-prompt.md`.

## Reporting back

When Q1 port completes + fixture lands on main, surface to Q2 wave-26 lead with:

- SHA of Q1 port commit(s) + fixture commit on main.
- Output of `StateBlobDumper` for fixture 09 (showing decoded Enemy.Name + MoveId + Powers list).
- Confirmation that capture point = turn 1 / `current_move = GIMME_MOVE`.
- Confirmation that SurprisePower wire-encoding name is exactly `"SurprisePower"`.
- B1-vs-B3 outcome: report whether `next_spawn_hps` metadata is emitted; if yes, the deterministic Sneaky + Fat HP values.
- Any deviation from this prompt + justification.
