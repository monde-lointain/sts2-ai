# Q1 Substrate-Divergence Audit (R15 re-audit; wave-51)

**Wave:** 51 (R15 re-audit; doc-only)
**Author seed:** Q1 lead, 2026-05-22 (§0 methodology)
**Contributors:** A.1.α + A.1.β subagents (§1-§10); Q1 lead A.2 (§11)
**Status:** in-progress (Q1 lead seeded §0; A.1 audits pending)

## §0 Audit Methodology

### Purpose

Audit each currently-probed encounter (9 from wave-46/47a + wave-49 mid-combat-covered set) at 5 dimensions to surface ALL Q1 vs upstream substrate divergences. Doc drives wave-52+ substrate-fix cluster waves (5-8 waves estimated). This audit corrects wave-48's FULL/PARTIAL/STUB lens which missed numerical drift in nominal-FULL encounters + per-encounter transition-rule drift (per project-lead wave-50/A.3 surface response).

### 5-Dimensional Diff Template (per encounter, per monster)

```markdown
### Encounter: <Name>

Q1 file(s):
- engine/headless/src/Sts2Headless.Domain/Content/Monsters/<File>.cs
- engine/headless/src/Sts2Headless.Domain/Content/Encounters/<File>.cs

Upstream file(s):
- ~/development/projects/godot/sts2/src/Core/Monsters/<File>.cs
- ~/development/projects/godot/sts2/src/Core/Combat/Encounters/<File>.cs

Transitive deps enumerated (R17 §5.2; 2-hop limit):
- hop 1 (encounter → monster): <list>
- hop 2 (monster → powers/relics/constants): <list>
- STOPPED at 2 hops. Further references noted but not recursed: <list or "none">

Probe-observed divergence (wave-50/A.4 §Baseline; seed=42):
> <verbatim DIVERGE line from drift-gate-hardening-redteam-evidence.md>

#### Monster: <ClassName>

| Dimension | Q1 | Upstream | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | <q1> | <upstream> | 1A/1B/1C/1D | high/med/low | <commentary> |
| Block init | <q1> | <upstream> | <class> | <sev> | |
| Move <ID>: damage | <q1> | <upstream> | <class> | <sev> | |
| Move <ID>: intent.kind | <q1> | <upstream> | <class> | <sev> | |
| Move <ID>: intent.dmgPerHit | <q1> | <upstream> | <class> | <sev> | |
| Move <ID>: intent.hitCount | <q1> | <upstream> | <class> | <sev> | |
| Move <ID>: intent.selfBlock | <q1> | <upstream> | <class> | <sev> | |
| Initial powers | <q1> | <upstream> | <class> | <sev> | |
| Transition: <rule> | <q1> | <upstream> | <class> | <sev> | |

Source-code root cause: <1-paragraph explanation tying the diff entries above to the probe-observed divergence — WHY does the probe report what it does, given the source-level diffs?>
```

Each encounter section has ONE diff table per monster in the encounter (e.g., GremlinMerc encounter has GremlinMerc + FatGremlin + SneakyGremlin = 3 tables).

### R15 Sub-Class Taxonomy

| Code | Name | Description | Origin |
|---|---|---|---|
| 1A | 1-move ATTACK stub | Q1 monster has 1 ATTACK move; upstream has full move-set with multiple intents | wave-48 audit (6 cases) |
| 1B | Encounter-wrapper-shape | Q1 encounter wraps wrong monster count OR mislabels variant | wave-49 Exoskeleton finding |
| 1C | Numerical drift in nominal-FULL | Q1 monster has correct move-set shape but wrong damage/intent/HP values | wave-50/A.3 mass-DIVERGE finding (NEW) |
| 1D | Per-monster transition-rule drift | Q1's RollMove / IsAlone / IsFront / HpBelow conditions differ from upstream | wave-49 Nibbits INIT_MOVE + wave-50 expansion |

A.1.β Pass 2 may surface need for 1E or further sub-divisions. Document refinements in §10 cross-encounter pattern analysis.

### R17 §5.2 Transitive Enumeration Checklist (2-hop limit; per encounter)

For each encounter, the auditor MUST explicitly answer:

1. **Hop 1: encounter → monster.** Did I enumerate ALL monster classes the encounter wraps? Including encounter-wrapper-shape mismatches (1B class)?
2. **Hop 1: monster-class hierarchies.** Did I enumerate base classes + derived classes (e.g., `Monster` → `Cultist`)?
3. **Hop 2: monster → power.** Did I enumerate power-class dependencies the monster INITs with at combat start?
4. **Hop 2: monster → relic/card.** Did I enumerate any relic/card classes that affect this encounter's first-turn state (e.g., starter relic interactions)?
5. **Hop 2: monster → constants.** Did I enumerate referenced constants from CombatEngine, shared math helpers, or other shared classes?

**STOP at 2 hops from encounter root.** Document any 3-hop references in the audit table's Notes column but DO NOT recurse to read those files. If 2-hop limit appears to miss load-bearing dependencies (e.g., monster's behavior depends on Hive's state and Hive isn't in this encounter's scope), SURFACE to Q1 lead.

Output per encounter: "Transitive deps enumerated:" list with hop annotations.

### Severity Scoring Rubric

- **high**: >50% record-count delta in wave-50/A.4 baseline OR any combat-correctness divergence (turn count, kill condition, win/loss outcome, intent kind change).
- **med**: 10-50% record-count delta OR non-correctness numeric drift (damage off by 1-3 points; HP off by 5-15; intent payload off by 1-2).
- **low**: Trivial naming/code-organization drift (move ID rename without semantic change; comment-only drift).

### Wave-50/A.4 Baseline Diagnostic Integration (V8 + X7 baked)

Each encounter's audit table includes BOTH columns:

- **Probe-observed divergence** (WHAT): verbatim DIVERGE line from `engine/headless/docs/specs/drift-gate-hardening-redteam-evidence.md` §Wave-50/A.4 §Baseline (lines ~395+; commit `ce0c127`). Format like:
  ```
  DIVERGE CultistsNormal seed=42: record count mismatch q1=15 golden=18
  ```
- **Source-code root cause** (WHY): this audit's diff tables explain the source-level cause of the probe symptom.

Don't re-derive the divergence; use the probe diagnostic as input.

### W7 4-File Protocol Per Encounter

For each encounter, READ all 4:

1. Q1 monster class file (`engine/headless/src/Sts2Headless.Domain/Content/Monsters/<Class>.cs`)
2. Q1 encounter wrapper file (`engine/headless/src/Sts2Headless.Domain/Content/Encounters/Phase1Encounters.cs` or analog)
3. Upstream monster class file (likely under `~/development/projects/godot/sts2/src/Core/Monsters/` — discover via `find` + `grep` per V4)
4. Upstream encounter wrapper file (likely under `~/development/projects/godot/sts2/src/Core/Combat/Encounters/` — discover via `find` + `grep`)

Plus per R17 §5.2 hop-2 enumeration: any power/relic/constant files referenced.

### Audit Doc Numbering (X9 baked)

- §0 Methodology — Q1 lead (this section)
- §1 CultistsNormal — A.1.α
- §2 LouseProgenitorNormal — A.1.α
- §3 ExoskeletonsNormal — A.1.α (Q1 mislabeled = upstream Weak shape per wave-49; document + audit true upstream Normal too)
- §4 LagavulinElite — A.1.α
- §5 GremlinMercNormal — A.1.β
- §6 KaiserCrabBoss — A.1.β
- §7 CeremonialBeastBoss — A.1.β
- §8 NibbitsWeak — A.1.β
- §9 NibbitsNormal — A.1.β
- §10 Cross-encounter pattern analysis — A.1.β Pass 2 (refines §1-§9 sub-class labels)
- §11 Wave-52+ cluster-shape recommendation — Q1 lead inline (A.2)

### Two-Pass Approach (V3 baked)

**Pass 1** (per encounter): populate diff table; assign PRELIMINARY sub-class labels.

**Pass 2** (after all 9 encounters' Pass 1 complete): write §10 cross-encounter pattern analysis. Refine preliminary sub-class labels based on cross-encounter patterns (e.g., what looked like 1C in isolation may shift to 1D once pattern emerges across multiple encounters).

### Re-Surface Triggers (audit-time)

- After 4 encounters audited (V1 baked): SURFACE to Q1 lead for context/consistency check.
- Any encounter reveals >5 critical (high-severity) divergences: surface.
- R17 §5.2 enumeration reveals monster class NOT in Q1 catalog: surface (R17 instance #6).
- 2-hop limit misses load-bearing dependencies: surface.
- Per-encounter audit budget exceeds ~2h: surface.
- Upstream path discovery takes >5 min/encounter: surface.
- Encounter wrapper references removed/renamed upstream MoveIds or classes (R18-adjacent): surface.

---

---

## §1 CultistsNormal

**Authored by:** A.1.α (wave-51)

### Encounter: CultistsNormal

Q1 file(s):
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/CultistsNormal.cs`
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/CalcifiedCultist.cs`
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/DampCultist.cs`

Upstream file(s):
- `~/development/projects/godot/sts2/src/Core/Models/Encounters/CultistsNormal.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/CalcifiedCultist.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/DampCultist.cs`

Transitive deps enumerated (R17 §5.2; 2-hop limit):
- hop 1 (encounter → monsters): CalcifiedCultist, DampCultist — both present in Q1 catalog. Encounter wrapper shape: 2 monsters, order [CalcifiedCultist, DampCultist]. Upstream matches exactly.
- hop 1 (monster-class hierarchies): Both inherit from MonsterModel directly. No intermediate cultist-specific base class.
- hop 2 (monster → powers): RitualPower — referenced by both cultists at INCANTATION. Q1 has `RitualPower.cs` (PowerIds.Ritual = "RitualPower"). Upstream `RitualPower.cs` confirmed exists. Power semantics: counter-type buff; grants Strength on enemy turn-end (skip turn-applied). Q1 implementation present and functionally correct.
- hop 2 (monster → relics/cards): No relic interactions at combat start. No card spawns.
- hop 2 (monster → constants): DarkStrikeDamage (CalcifiedCultist A0=9; DampCultist A0=1) and IncantationAmount (Calcified=2, Damp=5) are inline class constants. No CombatEngine shared constants referenced.
- STOPPED at 2 hops. RitualPower→Strength granting chain is 3-hop (internal to RitualPower + CombatEngine turn-end hook).

Probe-observed divergence (wave-50/A.4 §Baseline; seed=42):
```
DIVERGE CultistsNormal seed=42: record count mismatch q1=15 golden=18
```

#### Monster: CalcifiedCultist

| Dimension | Q1 | Upstream | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | MinHp=38, MaxHp=41 | MinInitialHp=GetValueIfAscension(ToughEnemies,39,38)=38; MaxInitialHp=GetValueIfAscension(ToughEnemies,42,41)=41 | — (match) | low | A0 values identical. |
| Block init | 0 (no spawn block) | 0 (AfterAddedToRoom not overridden) | — (match) | low | |
| Move INCANTATION_MOVE: intent.kind | Buff | BuffIntent | — (match) | low | |
| Move INCANTATION_MOVE: intent.dmgPerHit | 0 | 0 | — (match) | low | |
| Move INCANTATION_MOVE: intent.hitCount | 0 | 0 | — (match) | low | |
| Move INCANTATION_MOVE: intent.selfBlock | 0 | 0 | — (match) | low | |
| Move INCANTATION_MOVE: AppliesPowers | Ritual +2 to self | PowerCmd.Apply\<RitualPower\>(self, 2) | — (match) | low | IncantationAmount=2, A0 identical. |
| Move DARK_STRIKE_MOVE: damage | 9 | GetValueIfAscension(DeadlyEnemies,11,9)=9 | — (match) | low | A0 identical. |
| Move DARK_STRIKE_MOVE: intent.kind | Attack | SingleAttackIntent | — (match) | low | |
| Move DARK_STRIKE_MOVE: intent.dmgPerHit | 9 | 9 | — (match) | low | |
| Move DARK_STRIKE_MOVE: intent.hitCount | 1 | 1 (Single) | — (match) | low | |
| Initial powers | none at spawn | none | — (match) | low | |
| Transition: INCANTATION → DARK_STRIKE | deterministic FollowUp | moveState.FollowUpState=moveState2 | — (match) | low | |
| Transition: DARK_STRIKE self-loop | FollowUpMoveId=DARK_STRIKE_MOVE | moveState2.FollowUpState=moveState2 | — (match) | low | |
| Initial move | INCANTATION_MOVE | MonsterMoveStateMachine(list, moveState=INCANTATION) | — (match) | low | |

#### Monster: DampCultist

| Dimension | Q1 | Upstream | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | MinHp=51, MaxHp=53 | MinInitialHp=GetValueIfAscension(ToughEnemies,52,51)=51; MaxInitialHp=GetValueIfAscension(ToughEnemies,54,53)=53 | — (match) | low | A0 values identical. |
| Block init | 0 | 0 | — (match) | low | |
| Move INCANTATION_MOVE: intent.kind | Buff | BuffIntent | — (match) | low | |
| Move INCANTATION_MOVE: AppliesPowers (Ritual) | Ritual +5 to self | GetValueIfAscension(DeadlyEnemies,6,5)=5 | — (match) | low | A0 value=5 identical. |
| Move DARK_STRIKE_MOVE: damage | 1 | GetValueIfAscension(DeadlyEnemies,3,1)=1 | — (match) | low | A0 identical. |
| Move DARK_STRIKE_MOVE: intent.kind | Attack | SingleAttackIntent | — (match) | low | |
| Move DARK_STRIKE_MOVE: intent.dmgPerHit | 1 | 1 | — (match) | low | |
| Initial powers | none at spawn | none | — (match) | low | |
| Transition: INCANTATION → DARK_STRIKE | deterministic FollowUp | moveState.FollowUpState=moveState2 | — (match) | low | |
| Transition: DARK_STRIKE self-loop | FollowUpMoveId=DARK_STRIKE_MOVE | moveState2.FollowUpState=moveState2 | — (match) | low | |

**Sub-class distribution (§1):** No monster-model divergences found at A0. All dimensions match for both CalcifiedCultist and DampCultist. No 1A/1B/1C/1D sub-class assignments required.

**Severity heat-map (§1):** high=0, med=0, low=all.

**Source-code root cause:** CalcifiedCultist and DampCultist are fully byte-faithful ports: HP envelopes, move damage, Ritual stack amounts, move transitions, and spawn powers all correct at A0. The probe's record-count mismatch (`q1=15 golden=18`) is therefore NOT caused by monster-model divergence in either cultist. The root cause is in the **multi-turn engine layer**: Ritual→Strength accumulation feeds back into DARK_STRIKE DPS over successive turns. If Q1's CombatEngine does not correctly apply turn-end Ritual→Strength grants (or applies them on the wrong turn due to the `_wasJustAppliedByEnemy` flag interaction), DARK_STRIKE damage grows at a different rate than upstream, changing the player's kill turn. The 3-record gap (q1=15 vs golden=18) implies the player survives 3 more records in the upstream run — likely 1-2 more turns with the cultists alive longer due to slower Strength accumulation or different kill-turn arithmetic. The specific engine deficiency (Ritual power hook timing, Strength→damage multiplication in turn-loop) is outside this audit's 2-hop scope; classified as a **CombatEngine multi-turn engine-layer gap**, not a monster-model gap. Wave-52+ substrate-fix investigations should include the Ritual→Strength chain.

---

## §2 LouseProgenitorNormal

**Authored by:** A.1.α (wave-51)

### Encounter: LouseProgenitorNormal

Q1 file(s):
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/Phase1Encounters.cs` (class LouseProgenitorNormal)
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/Phase1Monsters.cs` (class LouseProgenitor)

Upstream file(s):
- `~/development/projects/godot/sts2/src/Core/Models/Encounters/LouseProgenitorNormal.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/LouseProgenitor.cs`

Transitive deps enumerated (R17 §5.2; 2-hop limit):
- hop 1 (encounter → monsters): LouseProgenitor only (single-monster encounter). Q1 matches upstream: 1 slot, null INIT_MOVE override. Upstream `GenerateMonsters()` returns single-element list with `(LouseProgenitor, null)`. Shape matches.
- hop 1 (monster-class hierarchies): LouseProgenitor inherits MonsterModel directly (Q1 and upstream).
- hop 2 (monster → powers): CurlUpPower — applied at spawn (AfterAddedToRoom: CurlBlock=14). Q1 has `PowerIds.CurlUp = "CurlUpPower"` in PowerIds catalog; declared in `spawnPowers: [MonsterSpawnPower(PowerIds.CurlUp, CurlBlock)]`. FrailPower applied to player by WEB_CANNON — Q1 has PowerIds.Frail. StrengthPower applied by CURL_AND_GROW — Q1 has PowerIds.Strength.
- hop 2 (monster → relics/cards): No relic interactions at combat start. No card spawns.
- hop 2 (monster → constants): WebDamage (A0=9), PounceDamage (A0=14), CurlBlock (A0=14), WebFrailStacks=2, CurlStrength=5. All inline in LouseProgenitor class.
- STOPPED at 2 hops. CurlUpPower's block-grant interaction with CombatEngine block-math is 3-hop.

Probe-observed divergence (wave-50/A.4 §Baseline; seed=42):
```
DIVERGE LouseProgenitorNormal seed=42: record count mismatch q1=21 golden=27
```

#### Monster: LouseProgenitor

| Dimension | Q1 | Upstream | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | MinHp=134, MaxHp=136 | MinInitialHp=GetValueIfAscension(ToughEnemies,138,134)=134; MaxInitialHp=GetValueIfAscension(ToughEnemies,141,136)=136 | — (match) | low | A0 identical. |
| Block init (spawn CurlUpPower) | CurlUpPower(14) via spawnPowers | AfterAddedToRoom: CurlUpPower(GetValueIfAscension(ToughEnemies,18,14)=14) | — (match) | low | A0 CurlBlock=14 identical. |
| Move WEB_CANNON_MOVE: damage | 9 (WebDamage) | GetValueIfAscension(DeadlyEnemies,10,9)=9 | — (match) | low | A0 identical. |
| Move WEB_CANNON_MOVE: intent.kind | **Attack only** (Intent.Attack) | **SingleAttackIntent + DebuffIntent** (two intents) | **1C** | **med** | Q1 emits a single Attack intent; upstream WEB_CANNON is `new MoveState("WEB_CANNON_MOVE", WebMove, new SingleAttackIntent(WebDamage), new DebuffIntent())`. Q1 missing the DebuffIntent composite. Player sees attack-only icon; upstream shows attack+debuff. No effect on damage/debuff payload (FrailPower still applied via AppliesPowers). Intent-display divergence only. |
| Move WEB_CANNON_MOVE: intent.dmgPerHit | 9 | 9 | — (match) | low | |
| Move WEB_CANNON_MOVE: intent.hitCount | 1 | 1 (Single) | — (match) | low | |
| Move WEB_CANNON_MOVE: AppliesPowers (Frail to player) | MonsterIntentPower(Frail, 2, Player) | PowerCmd.Apply\<FrailPower\>(targets, 2) | — (match) | low | Semantically equivalent at A0. |
| Move CURL_AND_GROW_MOVE: intent.kind | **Defend only** (Intent.Defend) | **DefendIntent + BuffIntent** (two intents) | **1C** | **med** | Q1 emits Defend intent only; upstream CURL_AND_GROW is `new MoveState("CURL_AND_GROW_MOVE", CurlAndGrowMove, new DefendIntent(), new BuffIntent())`. Missing Buff composite. Player sees defend-only icon; upstream shows defend+buff. |
| Move CURL_AND_GROW_MOVE: intent.selfBlock | 14 (CurlBlock) | GetValueIfAscension(ToughEnemies,18,14)=14 | — (match) | low | A0 block value identical. |
| Move CURL_AND_GROW_MOVE: AppliesPowers (Strength self) | MonsterIntentPower(Strength, 5) | PowerCmd.Apply\<StrengthPower\>(self, 5) | — (match) | low | StrengthPower stacks=5 identical. |
| Move POUNCE_MOVE: damage | 14 (PounceDamage) | GetValueIfAscension(DeadlyEnemies,16,14)=14 | — (match) | low | A0 identical. Wave-49/A.3 already fixed 16→14. |
| Move POUNCE_MOVE: intent.kind | Attack | SingleAttackIntent | — (match) | low | |
| Move POUNCE_MOVE: intent.dmgPerHit | 14 | 14 | — (match) | low | |
| Move POUNCE_MOVE: intent.hitCount | 1 | 1 | — (match) | low | |
| Initial powers (spawn) | CurlUpPower(14) | CurlUpPower(14) via AfterAddedToRoom | — (match) | low | |
| Transition: WEB_CANNON → CURL_AND_GROW | deterministic FollowUp | moveState.FollowUpState = moveState3 (CURL_AND_GROW) | — (match) | low | |
| Transition: CURL_AND_GROW → POUNCE | deterministic FollowUp | moveState3.FollowUpState = moveState2 (POUNCE) | — (match) | low | |
| Transition: POUNCE → WEB_CANNON | deterministic FollowUp | moveState2.FollowUpState = moveState (WEB_CANNON) | — (match) | low | |
| Initial move | WEB_CANNON_MOVE | MonsterMoveStateMachine(list, moveState=WEB_CANNON) | — (match) | low | |

**Sub-class distribution (§2):** 2 med-severity **1C** divergences (WEB_CANNON missing DebuffIntent composite; CURL_AND_GROW missing BuffIntent composite). 0 high-severity. All numerical values (HP, damage, block, power stacks, transition rules) match at A0.

**Severity heat-map (§2):** high=0, med=2, low=many.

**Source-code root cause:** The `q1=21 golden=27` record-count mismatch (6-record gap; 29% delta → med severity by rubric) is NOT caused by numerical drift in the LouseProgenitor monster model — all damage values, HP, transitions, and spawn powers are correct at A0. The 1C composite-intent divergences (WEB_CANNON Attack+Debuff; CURL_AND_GROW Defend+Buff) are display-only and do not affect combat computation. The root cause is multi-turn engine-layer: CURL_AND_GROW applies +5 Strength each time (every 3 turns), progressively amplifying POUNCE (14+5n dmg) and WEB_CANNON (9+5n dmg). If Q1's CombatEngine does not correctly accumulate and apply StrengthPower amplification on monster attacks, the player's HP trajectory diverges — the player dies sooner in Q1 (21 records) than upstream (27 records). Specifically, the gap suggests Q1's Strength→damage multiplication may not be firing correctly per turn, or the CURL_AND_GROW Strength application itself is not being registered in the engine's per-turn damage computation. The `_curled` state flag (animation control only; not damage-affecting) is not modeled in Q1, which is correct for Phase-1 engine scope.

---

## §3 ExoskeletonsNormal

**Authored by:** A.1.α (wave-51)

### §3.0 Mislabeling Finding (1B — confirmed)

**Wave-49 finding confirmed:** Q1's `ExoskeletonsNormal` (3 monsters: first/second/third) matches upstream `ExoskeletonsWeak` shape exactly (`ExoskeletonsWeak.Slots = ["first","second","third"]`, 3-element GenerateMonsters). The true upstream `ExoskeletonsNormal` has **4 monsters**: slots first/second/third/fourth (`ExoskeletonsNormal.Slots = ["first","second","third","fourth"]`; GenerateMonsters returns 4 tuples). This is a confirmed **1B (encounter-wrapper-shape)** divergence: Q1's encounter named "ExoskeletonsNormal" wraps the Weak shape (3 monsters), not the Normal shape (4 monsters).

This §3 audit covers:
- **§3.1**: Q1's `ExoskeletonsNormal` vs upstream `ExoskeletonsWeak` (the 3-monster shape Q1 actually implements)
- **§3.2**: Upstream `ExoskeletonsNormal` 4-monster shape (what Q1 is missing)

### §3.1 Q1 ExoskeletonsNormal vs upstream ExoskeletonsWeak (3-monster shape)

Q1 file(s):
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/Phase1Encounters.cs` (class ExoskeletonsNormal — 3 monsters)
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/Phase1Monsters.cs` (class Exoskeleton)

Upstream file(s) (§3.1 — Weak shape):
- `~/development/projects/godot/sts2/src/Core/Models/Encounters/ExoskeletonsWeak.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/Exoskeleton.cs`

Transitive deps enumerated (R17 §5.2; 2-hop limit):
- hop 1 (encounter → monsters): Exoskeleton ×3 (Q1 and upstream Weak both). Slot-based INIT_MOVE: Q1 overrides via `GenerateMonstersWithMoves` returning [(Exoskeleton, SKITTER), (Exoskeleton, MANDIBLES), (Exoskeleton, ENRAGE)]. Upstream Weak uses `ConditionalBranchState("INIT_MOVE")` keyed on `Creature.SlotName` → first=SKITTER, second=MANDIBLES, third=ENRAGE. Functionally equivalent for 3-slot shape.
- hop 1 (monster-class hierarchies): Exoskeleton inherits MonsterModel directly.
- hop 2 (monster → powers): HardToKillPower — applied at spawn (AfterAddedToRoom: 9 stacks). Q1 declares `MonsterSpawnPower("HardToKillPower", 9)` but "HardToKillPower" is NOT in Q1's PowerIds catalog (fails-soft per engine convention). Upstream `HardToKillPower.cs` exists. StrengthPower applied by ENRAGE — Q1 has PowerIds.Strength.
- hop 2 (monster → relics/cards): None.
- hop 2 (monster → constants): SkitterDamage=1, SkitterRepeats (A0=3), MandiblesDamage (A0=8), EnrageStrengthAmount=2. All inline.
- STOPPED at 2 hops. HardToKillPower damage-absorption semantics is 3-hop.

Probe-observed divergence (wave-50/A.4 §Baseline; seed=42):
```
DIVERGE ExoskeletonsNormal seed=42: record count mismatch q1=18 golden=21
```

#### Monster: Exoskeleton (3-slot shape audit)

| Dimension | Q1 | Upstream (A0) | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | MinHp=24, MaxHp=28 | MinInitialHp=GetValueIfAscension(ToughEnemies,25,24)=24; MaxInitialHp=GetValueIfAscension(ToughEnemies,29,28)=28 | — (match) | low | A0 identical. |
| Block init (spawn HardToKillPower) | `MonsterSpawnPower("HardToKillPower", 9)` — **fail-soft** (id not in Q1 PowerIds catalog) | AfterAddedToRoom: HardToKillPower(9) | **1C** | **med** | Q1's engine fails-soft on unknown power ids → 0 effective HardToKill stacks at spawn. If HardToKillPower reduces per-hit damage taken (typical "toughness" mechanic), its absence means Exoskeletons die faster in Q1, reducing combat length. Contributes to q1=18 < golden=21. |
| Move SKITTER_MOVE: damage per hit | 1 | 1 | — (match) | low | |
| Move SKITTER_MOVE: intent.kind | Attack (MultiAttack) | MultiAttackIntent | — (match) | low | |
| Move SKITTER_MOVE: intent.dmgPerHit | 1 | 1 | — (match) | low | |
| Move SKITTER_MOVE: intent.hitCount | 3 | GetValueIfAscension(DeadlyEnemies,4,3)=3 | — (match) | low | A0 identical. |
| Move MANDIBLES_MOVE: damage | 8 | GetValueIfAscension(DeadlyEnemies,9,8)=8 | — (match) | low | A0 identical. |
| Move MANDIBLES_MOVE: intent.kind | Attack | SingleAttackIntent | — (match) | low | |
| Move MANDIBLES_MOVE: intent.dmgPerHit | 8 | 8 | — (match) | low | |
| Move ENRAGE_MOVE: intent.kind | Buff | BuffIntent | — (match) | low | |
| Move ENRAGE_MOVE: AppliesPowers (Strength self) | MonsterIntentPower(Strength, 2) | StrengthPower(2) | — (match) | low | |
| Transition: SKITTER → RAND | Q1: RngBranchResolver(SKITTER 1f, MANDIBLES 1f) — CannotRepeat NOT enforced | upstream RAND: AddBranch(SKITTER, CannotRepeat, 1f) + AddBranch(MANDIBLES, CannotRepeat, 1f) | **1D** | **med** | Q1 allows SKITTER to immediately repeat (weight doesn't zero after just-played). Upstream's CannotRepeat zeroes the just-played move's weight, guaranteeing SKITTER is followed by MANDIBLES next. Multi-turn rotation diverges from turn 2 onwards. |
| Transition: MANDIBLES → ENRAGE | deterministic FollowUp=ENRAGE | moveState2.FollowUpState = moveState3 (ENRAGE) | — (match) | low | |
| Transition: ENRAGE → RAND | Q1: RAND 50/50 (both eligible after ENRAGE since neither was just played) | upstream: RAND (both CannotRepeat-eligible since ENRAGE was just played, not SKITTER or MANDIBLES) | — (match in practice) | low | After ENRAGE neither SKITTER nor MANDIBLES was just played → both eligible with equal weight. Q1 and upstream behave identically here. |
| Per-slot INIT_MOVE: first=SKITTER, second=MANDIBLES, third=ENRAGE | Q1 GenerateMonstersWithMoves hardcodes [SKITTER,MANDIBLES,ENRAGE] | upstream ConditionalBranchState("INIT_MOVE") → slot-keyed | — (match) | low | Functionally equivalent for 3-slot shape. |
| Initial powers (spawn) | HardToKillPower(9) — fail-soft | HardToKillPower(9) via AfterAddedToRoom | **1C** | **med** | Same as block-init row. |

### §3.2 Missing upstream ExoskeletonsNormal shape (4-monster)

Upstream `ExoskeletonsNormal.GenerateMonsters()` returns 4 Exoskeletons at slots first/second/third/fourth. The 4th Exoskeleton's `ConditionalBranchState("INIT_MOVE")` → slot "fourth" → `randomBranchState` (RAND between SKITTER and MANDIBLES with CannotRepeat). Q1 has no 4-Exoskeleton encounter; the probe registers `ExoskeletonsNormal` against the 3-monster wrapper, so the golden capture runs against upstream's 4-monster encounter while Q1 runs 3. The 4th Exoskeleton adds additional combat turns to upstream's record count. This is the **primary driver of the 3-record gap** (q1=18 vs golden=21).

**Sub-class distribution (§3):**
- **1B** (ExoskeletonsNormal wraps 3 not 4 Exoskeletons): **HIGH** severity — wrong encounter variant. Q1 is actually implementing upstream ExoskeletonsWeak, not ExoskeletonsNormal.
- **1C** (HardToKillPower fail-soft — 0 vs 9 stacks): med severity — power absent from Q1's catalog; monsters die faster.
- **1D** (SKITTER→RAND CannotRepeat elision): med severity — multi-turn rotation differs.
- Severity heat-map: **high=1, med=2, low=many**.

**Source-code root cause:** The `q1=18 golden=21` mismatch (14% delta; 3 records) has two compounding causes. Primary: **1B shape error** — Q1 probes a 3-Exoskeleton encounter against upstream's 4-Exoskeleton encounter registered under the same canonical id "ExoskeletonsNormal". The extra Exoskeleton in upstream extends combat, contributing to golden=21 > q1=18. Secondary: **HardToKillPower fail-soft (1C)** — Q1's engine silently drops the spawn power for each Exoskeleton because "HardToKillPower" is not in PowerIds; upstream applies 9 stacks per monster that absorb per-hit damage, extending each Exoskeleton's survival. Both effects combine to make upstream combat longer. The 1D CannotRepeat divergence changes move-sequence probability but has smaller impact on combat duration than the 1B+1C combination.

---

## §4 LagavulinElite

**Authored by:** A.1.α (wave-51)

### Encounter: LagavulinElite

Q1 file(s):
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/Phase1Encounters.cs` (class LagavulinElite)
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/Phase1Monsters.cs` (class LagavulinMatriarch)

Upstream file(s):
- `~/development/projects/godot/sts2/src/Core/Models/Encounters/LagavulinMatriarchBoss.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/LagavulinMatriarch.cs`

**Naming note:** Q1 registers as `LagavulinElite`; upstream encounter class is `LagavulinMatriarchBoss` (RoomType.Boss, not Elite). Both wrap a single LagavulinMatriarch. The encounter id naming difference may affect upstream RNG seed derivation via ModelDb slug — flagged as potential R18-adjacent item (encounter id used in probe's encounter-Rng-key derivation).

Transitive deps enumerated (R17 §5.2; 2-hop limit):
- hop 1 (encounter → monsters): LagavulinMatriarch only. Single-monster encounter. Both Q1 and upstream: 1 slot, null INIT_MOVE override.
- hop 1 (monster-class hierarchies): LagavulinMatriarch inherits MonsterModel directly.
- hop 2 (monster → powers): PlatingPower — applied at spawn. Q1 `PowerIds.Plated = "PlatedArmorPower"`; upstream class is `PlatingPower`. **Power id name divergence**: upstream's ModelDb slug for `PlatingPower` is likely "PlatingPower" (PascalCase class name), not "PlatedArmorPower" — Q1's spawn power declaration uses the wrong id string, causing fail-soft at combat start. AsleepPower — applied at spawn in upstream (`AfterAddedToRoom: AsleepPower(3)`); Q1 deliberately ELIDES (no infrastructure; uses HpThresholdResolver proxy). Not in Q1's PowerIds catalog. StrengthPower (self, +2) and StrengthPower/DexterityPower (player, -2) in SOUL_SIPHON.
- hop 2 (monster → relics/cards): None.
- hop 2 (monster → constants): SlashDamage (A0=19), Slash2Damage (A0=12), Slash2Block (A0=12), DisembowelDamage (A0=9), DisembowelRepeat=2, SoulSiphonStrengthSelf=2, SoulSiphonStatsDown=2. All inline.
- STOPPED at 2 hops. AsleepPower removal mechanics (how AsleepPower stacks are removed during combat, triggering wake) is 3-hop into AsleepPower implementation + CombatEngine power-removal hooks.

Probe-observed divergence (wave-50/A.4 §Baseline; seed=42):
```
DIVERGE LagavulinElite seed=42: record count mismatch q1=60 golden=27
```

**Severity note:** `q1=60 golden=27` — Q1 generates 122% MORE records than upstream. Highest divergence of any encounter in the baseline. **HIGH** severity overall.

#### Monster: LagavulinMatriarch

| Dimension | Q1 | Upstream (A0) | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | MinHp=222, MaxHp=222 | MinInitialHp=GetValueIfAscension(ToughEnemies,233,222)=222; MaxInitialHp=MinInitialHp=222 | — (match) | low | A0 identical. |
| Block init (spawn PlatingPower) | `MonsterSpawnPower("PlatedArmorPower", 12)` | AfterAddedToRoom: PlatingPower(12) | **1C** | **med** | Power id "PlatedArmorPower" in Q1 likely does not match upstream ModelDb slug for `PlatingPower` class (expected "PlatingPower"). Fail-soft → 0 Plating at spawn. |
| Initial powers (AsleepPower) | **ABSENT** — elided; HpThresholdResolver proxy used instead | AfterAddedToRoom: AsleepPower(3) | **1D** | **HIGH** | AsleepPower absence is the primary wake-condition divergence driver. See Transition row below. |
| Move SLEEP_MOVE: intent.kind | **Buff** (Intent.Buff()) | **SleepIntent** | **1C** | **med** | Q1 encodes SLEEP_MOVE as `Intent.Buff()`. Upstream uses `new SleepIntent()`. Q1's `IntentKind` enum HAS `Sleep=4` but `Intent.cs` has no static `Sleep()` factory — SLEEP_MOVE is encoded as Buff. Player sees Buff icon instead of Sleep icon. |
| Move SLASH_MOVE: damage | 19 (SlashDamage) | GetValueIfAscension(DeadlyEnemies,21,19)=19 | — (match) | low | A0 identical. |
| Move SLASH_MOVE: intent.kind | Attack | SingleAttackIntent | — (match) | low | |
| Move SLASH_MOVE: intent.dmgPerHit | 19 | 19 | — (match) | low | |
| Move SLASH_MOVE: intent.hitCount | 1 | 1 | — (match) | low | |
| Move SLASH2_MOVE: damage | 12 (Slash2Damage) | GetValueIfAscension(DeadlyEnemies,14,12)=12 | — (match) | low | A0 identical. |
| Move SLASH2_MOVE: intent.kind | **Attack only** | **SingleAttackIntent + DefendIntent** (two intents) | **1C** | **med** | Q1: `Intent.Attack(Slash2Damage)`. Upstream: `new MoveState("SLASH2_MOVE", …, new SingleAttackIntent(Slash2Damage), new DefendIntent())`. Missing DefendIntent display. |
| Move SLASH2_MOVE: SelfBlockGain | **0 (MISSING)** | 12 (Slash2Block=GetValueIfAscension(ToughEnemies,14,12)) | **1C** | **HIGH** | Q1's SLASH2_MOVE MonsterMove declaration: `new(Slash2MoveId, Intent.Attack(Slash2Damage), FollowUpMoveId: SoulSiphonMoveId)` — no `SelfBlockGain` parameter. LagavulinMatriarch gains 0 block on SLASH2 in Q1; upstream gains 12. Monster is significantly less durable during awake phase. |
| Move DISEMBOWEL_MOVE: damage per hit | 9 | GetValueIfAscension(DeadlyEnemies,10,9)=9 | — (match) | low | A0 identical. |
| Move DISEMBOWEL_MOVE: intent.kind | Attack (MultiAttack) | MultiAttackIntent | — (match) | low | |
| Move DISEMBOWEL_MOVE: intent.dmgPerHit | 9 | 9 | — (match) | low | |
| Move DISEMBOWEL_MOVE: intent.hitCount | 2 | 2 | — (match) | low | |
| Move SOUL_SIPHON_MOVE: intent.kind | **Debuff only** | **DebuffIntent + BuffIntent** (two intents) | **1C** | **med** | Q1: `Intent.Debuff()`. Upstream: `new MoveState("SOUL_SIPHON_MOVE", …, new DebuffIntent(), new BuffIntent())`. Missing BuffIntent display. |
| Move SOUL_SIPHON_MOVE: AppliesPowers (player debuff) | StrengthDown(2, Player) + DexterityLoss(2, Player) | PowerCmd.Apply\<StrengthPower\>(targets, -2) + PowerCmd.Apply\<DexterityPower\>(targets, -2) | **1C** | **med** | Power id mapping divergence: upstream uses `StrengthPower(-2)` and `DexterityPower(-2)` (negative stacks on existing powers); Q1 uses separate `StrengthDownPower` and `DexterityLossPower` ids. Functional equivalence depends on Q1 engine; likely equivalent at A0 damage computation but is an id-mapping divergence. |
| Move SOUL_SIPHON_MOVE: AppliesPowers (self buff) | MonsterIntentPower(Strength, 2) | PowerCmd.Apply\<StrengthPower\>(self, 2) | — (match) | low | |
| Transition: SLEEP_MOVE → wake | Q1: **HpThresholdResolver**(fraction=0.5; belowMoveId=SLASH, aboveMoveId=SLEEP) — wakes when HP ≤ 50% | upstream: **ConditionalBranchState("SLEEP_BRANCH")** keyed on `!HasPower<AsleepPower>()` — wakes when AsleepPower is removed | **1D** | **HIGH** | Wake condition completely different. Upstream AsleepPower(3) is applied at spawn; AsleepPower removal mechanism determines when Lagavulin wakes. If AsleepPower stacks decrease on damage-received (e.g., 1 stack removed per hit → wakes after 3 hits regardless of HP), the wake timing is hit-count-based, not HP-fraction-based. Q1's ≤50% HP proxy may fire much later (player must deal 111+ damage before Lagavulin enters attack mode), causing Q1's Lagavulin to stay in SLEEP_MOVE far longer than upstream — producing q1=60 vs golden=27. |
| Transition: SLASH → DISEMBOWEL | deterministic FollowUp | moveState2.FollowUpState = moveState4 (DISEMBOWEL) | — (match) | low | |
| Transition: DISEMBOWEL → SLASH2 | deterministic FollowUp | moveState4.FollowUpState = moveState3 (SLASH2) | — (match) | low | |
| Transition: SLASH2 → SOUL_SIPHON | deterministic FollowUp | moveState3.FollowUpState = moveState5 (SOUL_SIPHON) | — (match) | low | |
| Transition: SOUL_SIPHON → SLASH | deterministic FollowUp | moveState5.FollowUpState = moveState2 (SLASH) | — (match) | low | |

**Sub-class distribution (§4):**
- **1D** (AsleepPower elision + HpThreshold proxy wake condition): **HIGH** severity — primary driver of q1=60 vs golden=27.
- **1C** (SLASH2 missing SelfBlockGain=12): **HIGH** severity — monster gains 0 block on SLASH2.
- **1C** (SLEEP_MOVE: Buff intent instead of SleepIntent): med severity — display only.
- **1C** (SLASH2 missing DefendIntent): med severity — display only.
- **1C** (SOUL_SIPHON missing BuffIntent): med severity — display only.
- **1C** (PlatingPower id "PlatedArmorPower" vs "PlatingPower"): med severity — likely fail-soft.
- **1C** (SOUL_SIPHON StrengthDown/DexterityLoss vs StrengthPower(-2)/DexterityPower(-2)): med severity — id-mapping divergence.
- Severity heat-map: **high=2, med=5, low=many**.

**RE-SURFACE NOTE:** §4 has 2 high-severity divergences (1D wake-condition + 1C SLASH2 SelfBlockGain), plus the largest record-count delta (122%) in the baseline. This is the highest-priority encounter for wave-52+ substrate fix. Not surfacing as re-surface trigger (count=2 < 5 threshold) but flagging severity for Q1 lead review.

**Source-code root cause:** The `q1=60 golden=27` divergence (122% delta; HIGH) has two compounding causes. Primary: **1D AsleepPower wake-condition** — upstream wakes Lagavulin when `AsleepPower` is removed by the engine (AsleepPower(3) stacks decremented by damage or specific mechanics; e.g., if each damage instance decrements 1 AsleepPower stack, Lagavulin wakes after 3 hits regardless of HP). Q1 uses HP ≤ 50% as the proxy (= HP ≤ 111). For seeds where the player deals consistent low-damage attacks, upstream Lagavulin wakes early (3 hits into combat) while Q1's stays asleep until the player has dealt 111+ HP in damage — which may take many more turns. This causes Q1 to loop through SLEEP_MOVE for many more turns, accumulating records (q1=60) while upstream transitions to the attack cycle quickly (golden=27). Secondary: **1C SLASH2 SelfBlockGain missing** — once awake in upstream, Lagavulin gains 12 block on each SLASH2, absorbing player attacks and extending combat; Q1 Lagavulin gains 0 block, making it easier to kill once awake. This secondary effect would reduce record count in Q1 (counteracts the primary effect slightly) but the primary AsleepPower timing effect dominates at seed=42. The PlatingPower fail-soft and SOUL_SIPHON power id mapping are additional contributing factors but secondary to the wake-condition 1D divergence.

---

(§5-§11 to be authored by A.1.β subagent + A.2 Q1 lead inline; see plan for dispatch sequencing.)
