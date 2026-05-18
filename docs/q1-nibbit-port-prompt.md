# Q1 Nibbit Port — Engineer Prompt

**Originated**: 2026-05-18 (Q2 wave-24 K.0 cross-quantum coordination)
**Requesting party**: Q2 oracle (engine/cpp); next port wave targets Nibbit
**Q1 lead**: TBA by user
**Estimated scope**: ~400-600 LOC across Domain + Tests + fixture corpus

---

## Goal

Port the **Nibbit** monster and the two Nibbit encounters (**NibbitsWeak**, **NibbitsNormal**) from upstream STS2 (Godot/C#) into Q1 (`engine/headless/`). Generate state-blob fixtures at canonical seed 42, suitable for Q2 oracle adapter consumption.

Q2 wave-24 has 5 streams already specified (K.α substrate / K.β monster definition / K.γ adapter projection / K.δ docs / K.ε gate). Q2 K.γ_setup gates on Q1 fixture availability. Q2's K.α + K.β substrate work proceeds in parallel and DOES NOT block on Q1 work.

## Upstream source (authoritative reference)

**Monster**: `/home/clydew372/development/projects/godot/sts2/src/Core/Models/Monsters/Nibbit.cs`
**Encounters**:
- `/home/clydew372/development/projects/godot/sts2/src/Core/Models/Encounters/NibbitsWeak.cs`
- `/home/clydew372/development/projects/godot/sts2/src/Core/Models/Encounters/NibbitsNormal.cs`

**Ascension scope**: **A0 only** per Q2-ADR-002 Phase-1A. Use the 3rd argument to `AscensionHelper.GetValueIfAscension(level, A11_val, A0_val)` for damage / block / strength values.

## Mechanics spec (A0)

### Nibbit per-monster

| Field | A0 Value | Upstream cite |
|---|---|---|
| HP min | 42 | Nibbit.cs (verify line) |
| HP max | 46 | Nibbit.cs (verify line) |
| `BUTT_MOVE` damage | 12 | Nibbit.cs (verify line) |
| `SLICE_MOVE` damage | 6 | Nibbit.cs (verify line) |
| `SLICE_MOVE` block to self | 5 | Nibbit.cs (verify line) |
| `HISS_MOVE` self-Strength gain | +2 (permanent stack) | Nibbit.cs (verify line) |
| Spawn powers | NONE | (no spawn powers per audit) |

Engineer cross-checks each value by reading Nibbit.cs directly. Pattern observed in upstream: `AscensionHelper.GetValueIfAscension(AscensionLevel.<Tier>, A11_val, A0_val)` → use the A0 value (3rd argument).

### Move rotation

Strict 3-cycle: **BUTT → SLICE → HISS → BUTT → ...** (no RandomBranch; no CannotRepeat).

### Initial move (depends on encounter-supplied flag)

Per upstream `ConditionalBranchState` at Nibbit.cs:74-86:
- `IsAlone=true` (NibbitsWeak): starts on **BUTT_MOVE**.
- `IsFront=true` (NibbitsNormal, front slot): starts on **SLICE_MOVE**.
- `IsFront=false` (NibbitsNormal, back slot): starts on **HISS_MOVE**.

### NibbitsWeak encounter

- Composition: **1 Nibbit** with `IsAlone=true`.
- Initial move: BUTT_MOVE.
- Encounter RNG: NONE.

### NibbitsNormal encounter

- Composition: **2 Nibbits**.
  - Slot 0 (front): `IsFront=true` → initial move SLICE_MOVE.
  - Slot 1 (back): `IsFront=false` → initial move HISS_MOVE.
- Encounter RNG: NONE.
- **Critical: slot order is deterministic** — Q1 must emit slot 0 = front, slot 1 = back per upstream BuildMonster call order. Q2's adapter projection relies on this ordering.

## Q1 file paths to create / modify

### New files

- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/Nibbit.cs`
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/NibbitsWeak.cs`
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/NibbitsNormal.cs`
- `engine/headless/test/Sts2Headless.Tests.Domain/Content/Monsters/NibbitTests.cs`
- `engine/headless/test/Sts2Headless.Tests.Domain/Content/Encounters/NibbitsWeakTests.cs`
- `engine/headless/test/Sts2Headless.Tests.Domain/Content/Encounters/NibbitsNormalTests.cs`

### Modified files

- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/Phase1Monsters.cs` — register Nibbit in the Phase-1 monster catalog (mirror existing CalcifiedCultist / DampCultist / LeafSlimeS etc. registrations).
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/Phase1Encounters.cs` — register NibbitsWeak + NibbitsNormal in the Phase-1 encounter catalog (mirror existing CultistsNormal / SmallSlimes / MediumSlimes registrations).
- `engine/headless/test/Sts2Headless.Tests.Tools/Fixtures/StateBlobFixtureRecipe.cs` — add 2 new fixture recipes: `07-nibbits-weak-seed42` + `08-nibbits-normal-seed42` (mirror existing recipes for cultist + Louse). Seed 42 canonical.
- `engine/headless/test/Sts2Headless.Tests.Domain/Content/Monsters/Phase1MonsterTests.cs` — likely needs Nibbit cases added (mirror existing per-monster test cases).
- `engine/headless/test/Sts2Headless.Tests.Domain/Content/Monsters/Phase1MonsterRotationTests.cs` — likely needs Nibbit move-rotation cases (BUTT→SLICE→HISS verification).

### Generated artifacts (committed to repo)

- `engine/headless/test/fixtures/state-blobs/07-nibbits-weak-seed42/state.blob` (binary blob)
- `engine/headless/test/fixtures/state-blobs/07-nibbits-weak-seed42/README.md` (per existing fixture README convention)
- `engine/headless/test/fixtures/state-blobs/08-nibbits-normal-seed42/state.blob`
- `engine/headless/test/fixtures/state-blobs/08-nibbits-normal-seed42/README.md`

Run `StateBlobFixtureGenerator` (existing tool at `engine/headless/test/Sts2Headless.Tests.Tools/Fixtures/`) to produce the blobs. Existing CI test `StateBlobFixtureRegressionTests` verifies fixture stability.

## Wire format requirements (Q2 consumes these)

Per `engine/headless/src/Sts2Headless.Adapters/StateCodec/StateCodec.cs`:

- `Enemy.Name`: encoded as length-prefixed string. **Must be exactly `"Nibbit"`** for both encounters (matches upstream class name; Q2 adapter's `is_nibbits_weak` / `is_nibbits_normal` predicates pattern-match on this exact string).
- `MonsterIntent.MoveId`: encoded as length-prefixed string. **Use upstream MoveDecider canonical names**: `"BUTT_MOVE"`, `"SLICE_MOVE"`, `"HISS_MOVE"` (verify exact strings by reading Nibbit.cs's move-state construction).
- Encounter_id (in fixture metadata / README): `"NibbitsWeak"` and `"NibbitsNormal"` (exact class names; no transformation).

## Fixture capture conventions (Q2 consumes these)

Per Q2's adapter expectations + existing cultist/Louse fixture inspection:

- **Capture point**: turn 1 / start of player's first turn. Enemies have HP = initial-rolled, block = 0, no accumulated buffs, `current_move` = each Nibbit's IsAlone/IsFront-determined initial move.
- **Seed**: 42 (canonical; matches existing cultist + Louse + slime fixtures at seed 42).
- **Player state**: Silent starter (HP 70, energy 3, standard 12-card starter deck: 5 Strike + 5 Defend + 1 Neutralize + 1 Survivor).

## Verification (Q1-side gate criteria)

Before declaring the port done:
- All Nibbit unit tests PASS (mirror existing cultist + slime test density).
- `StateBlobFixtureRegressionTests` PASS — fixture blobs deterministic across re-generation.
- `q1-ci` (or equivalent Q1 CI target) GREEN.
- `dotnet test engine/headless/...` GREEN for relevant test projects.
- Fixture inspection: decode 07 + 08 fixtures via `StateBlobDumper` tool; confirm:
  - Enemy.Name == "Nibbit" for all slots.
  - MoveId strings match upstream names.
  - Capture point = turn 1 (enemies at initial HP, no buffs, block=0).

## Coordination with Q2

- **Q2 wave-24 K.γ_setup gates on Q1 fixture availability**. Once 07 + 08 fixtures land on main, Q2's K.γ_setup engineer can dispatch.
- Q2's adapter dispatch: `{"Nibbit"} → "NibbitsWeak"` and `{"Nibbit", "Nibbit"} → "NibbitsNormal"`. Both predicates check `enemy_count` + name. Mutually exclusive on count.
- If Q1's MoveId names differ from `"BUTT_MOVE"` / `"SLICE_MOVE"` / `"HISS_MOVE"`, surface immediately to Q2 wave-24 lead (project-lead handles cross-quantum reconciliation; Q2's `move_calc.cc` wire-name mapping may need adjustment).
- If Q1 ports Nibbit with semantic differences from upstream (e.g., different HP range or move ordering), surface — Q2's pin VALUES will diverge from upstream-derived projections, and the oracle-agreement gate (Q12) will surface the divergence.

## Tractability note (informational)

Q2 has audited both Nibbit encounters per the 4-criterion tractability screen (`[[project-encounter-tractability]]` memory + Q2-ADR-013 Amendment 4 §SmallSlimes-deprecation):

1. **Bounded combat duration**: Strength scaling forces offensive play; convergence ~9-11 rounds (NibbitsWeak) / ~12-18 rounds (NibbitsNormal). Well under Q2's `kSearchHorizonRounds = 25`.
2. **No unbounded status accumulation**: Nibbit moves don't inject any status cards.
3. **Block does NOT dominate damage**: Strength scaling pushes Nibbit damage past Silent's 15-block budget within horizon; expectimax abandons defensive trap quickly.
4. **Q1 fixture support**: this work item.

Q1's port should preserve these tractability properties — primarily: **don't accidentally remove Strength self-buff from HISS** (the offensive-pressure mechanism) or otherwise reduce damage below 15-block budget after horizon.

## Out of scope

- Ascension > A0.
- New encounters beyond NibbitsWeak + NibbitsNormal.
- Q2 work (handled in `engine/cpp/` wave-24 K.α onwards; independent of this port).

## Cross-references

- Q2 plan: `/home/clydew372/.claude/plans/plan-the-q2-oracle-glittery-pony.md` § "Wave-24: Nibbit port".
- Q2 tractability criteria: `[[project-encounter-tractability]]` memory.
- Q2 cultist BYTE rotation chain (informational): Q2-ADR-010 §Recovery; cultist Zobrist BYTE at `0x569115efa81a95dc / 0x9a06f1e505846a80` post-wave-23.

## Reporting back

When Q1 port completes + fixtures land on main, surface to Q2 wave-24 lead with:

- SHA of Q1 port commit(s) + fixture commit(s) on main.
- Output of `StateBlobDumper` for each fixture (showing decoded Enemy.Name + MoveId per slot).
- Confirmation that capture point = turn 1 / `current_move` matches IsAlone/IsFront expectations.
- Any deviation from this prompt + justification.
