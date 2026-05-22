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

---

## §5 GremlinMercNormal

**Authored by:** A.1.β (wave-51)

### Encounter: GremlinMercNormal

Q1 file(s):
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/Phase1Encounters.cs` (class GremlinMercNormal)
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/Phase1Monsters.cs` (class GremlinMerc)
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/FatGremlin.cs` (class FatGremlin)
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/SneakyGremlin.cs` (class SneakyGremlin)

Upstream file(s):
- `~/development/projects/godot/sts2/src/Core/Models/Encounters/GremlinMercNormal.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/GremlinMerc.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/FatGremlin.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/SneakyGremlin.cs`

Transitive deps enumerated (R17 §5.2; 2-hop limit):
- hop 1 (encounter → monsters): GremlinMerc only at spawn (upstream `GenerateMonsters()` returns single-element list with `(GremlinMerc.ToMutable(), "merc")`). FatGremlin + SneakyGremlin listed in upstream's `AllPossibleMonsters` but spawned mid-combat by SurprisePower's AfterDeath hook — NOT in initial encounter shape. Q1 models both FatGremlin + SneakyGremlin in separate files; they are in the monster catalog.
- hop 1 (monster-class hierarchies): All three inherit MonsterModel directly.
- hop 2 (monster → powers): SurprisePower — applied to GremlinMerc at spawn. Q1 has `PowerIds.Surprise = "SurprisePower"`. ThieveryPower — applied per player at spawn. Q1 has `PowerIds.Thievery = "ThieveryPower"`. WeakPower — applied to player by DOUBLE_SMASH (in catalog). StrengthPower — applied to self by HEHE (in catalog).
- hop 2 (monster → relics/cards): No relic interactions at combat start. No card spawns at encounter init.
- hop 2 (monster → constants): GimmeDamage=7, GimmeRepeat=2, DoubleSmashDamage=6, DoubleSmashRepeat=2, HeheDamage=8, HeheStrengthStacks=2, SurprisePowerStacks=1, ThieveryPowerGold=20. All inline in GremlinMerc class.
- STOPPED at 2 hops. SurprisePower's AfterDeath subscription (spawn hook for FatGremlin + SneakyGremlin) is 3-hop into SurprisePower implementation.

Probe-observed divergence (wave-50/A.4 §Baseline; seed=42):
```
DIVERGE GremlinMercNormal seed=42: record count mismatch q1=21 golden=13
```

**Severity note:** `q1=21 golden=13` — Q1 generates **62% MORE** records than upstream. HIGH severity. Unusual direction: Q1 combat is LONGER than upstream.

#### Monster: GremlinMerc

| Dimension | Q1 | Upstream (A0) | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | MinHp=47, MaxHp=49 | MinInitialHp=GetValueIfAscension(ToughEnemies,51,47)=47; MaxInitialHp=GetValueIfAscension(ToughEnemies,53,49)=49 | — (match) | low | A0 identical. |
| Block init | 0 | 0 | — (match) | low | |
| Move GIMME_MOVE: intent.kind | MultiAttack | MultiAttackIntent | — (match) | low | |
| Move GIMME_MOVE: intent.dmgPerHit | 7 | GetValueIfAscension(ToughEnemies,8,7)=7 | — (match) | low | A0 identical. |
| Move GIMME_MOVE: intent.hitCount | 2 | GimmeRepeat=2 | — (match) | low | |
| Move GIMME_MOVE: gold steal payload | ThieveryPower.Steal() per turn (stub; ADR-030) | `foreach ThieveryPower powerInstance → powerInstance.Steal()` in GimmeMove body | **1C** | **med** | Q1 ThieveryPower.Steal() is Phase-2-deferred stub; no gold actually stolen. Behavioral deviation but no damage impact on record count. |
| Move DOUBLE_SMASH_MOVE: intent.kind | **MultiAttack only** | **MultiAttackIntent + DebuffIntent** (two intents) | **1C** | **med** | Q1: `Intent.MultiAttack(DoubleSmashDamage, DoubleSmashHitCount)`. Upstream: `new MoveState(…, new MultiAttackIntent(DoubleSmashDamage, DoubleSmashRepeat), new DebuffIntent())`. Missing DebuffIntent composite. Display-only. |
| Move DOUBLE_SMASH_MOVE: intent.dmgPerHit | 6 | GetValueIfAscension(ToughEnemies,7,6)=6 | — (match) | low | A0 identical. |
| Move DOUBLE_SMASH_MOVE: intent.hitCount | 2 | DoubleSmashRepeat=2 | — (match) | low | |
| Move DOUBLE_SMASH_MOVE: AppliesPowers (Weak to player) | MonsterIntentPower(Weak, 2, Player) | PowerCmd.Apply\<WeakPower\>(targets, 2) | — (match) | low | |
| Move HEHE_MOVE: intent.kind | **Attack only** | **SingleAttackIntent + BuffIntent** (two intents) | **1C** | **med** | Q1: `Intent.Attack(HeheDamage)`. Upstream: `new MoveState(…, new SingleAttackIntent(HeheDamage), new BuffIntent())`. Missing BuffIntent composite. Display-only. |
| Move HEHE_MOVE: intent.dmgPerHit | 8 | GetValueIfAscension(ToughEnemies,9,8)=8 | — (match) | low | A0 identical. |
| Move HEHE_MOVE: AppliesPowers (Strength self) | MonsterIntentPower(Strength, 2) | PowerCmd.Apply\<StrengthPower\>(self, 2) | — (match) | low | Stacks=2 identical. |
| Initial powers (spawn) | SurprisePower(1) + ThieveryPower(20) | SurprisePower(1) + ThieveryPower(20) per player via AfterAddedToRoom | — (match) | low | Both powers declared. SurprisePower AfterDeath spawn hook active. |
| Transition: GIMME → DOUBLE_SMASH | deterministic FollowUp | moveState.FollowUpState=moveState2 | — (match) | low | |
| Transition: DOUBLE_SMASH → HEHE | deterministic FollowUp | moveState2.FollowUpState=moveState3 | — (match) | low | |
| Transition: HEHE → GIMME | deterministic FollowUp (loop) | moveState3.FollowUpState=moveState | — (match) | low | |
| Initial move | GIMME_MOVE | MonsterMoveStateMachine(list, moveState=GIMME) | — (match) | low | |
| SurprisePower: AfterDeath spawn + escape | FatGremlin spawned; FLEE_MOVE = Intent.Unknown (no escape) | SurprisePower.AfterDeath spawns FatGremlin (slot "fat") + SneakyGremlin (slot "sneaky"); FatGremlin FLEE_MOVE calls `CreatureCmd.Escape()` | **1D** | **HIGH** | Primary driver of q1=21 vs golden=13. In upstream FatGremlin FLEE_MOVE removes itself via `CreatureCmd.Escape()`. In Q1, FLEE_MOVE maps to Intent.Unknown/self-loop — FatGremlin stays alive until killed. Q1 must kill FatGremlin (13-17 HP) + SneakyGremlin (10-14 HP) adding ~8 extra records. |

#### Monster: FatGremlin

| Dimension | Q1 | Upstream (A0) | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | MinHp=13, MaxHp=17 | MinInitialHp=GetValueIfAscension(ToughEnemies,14,13)=13; MaxInitialHp=GetValueIfAscension(ToughEnemies,18,17)=17 | — (match) | low | A0 identical. |
| Move SPAWNED_MOVE: intent.kind | Stun (IntentKind.Stun) | StunIntent | — (match) | low | Correct. |
| Move SPAWNED_MOVE: transition | → FLEE_MOVE (deterministic) | moveState.FollowUpState=moveState2 (FLEE) | — (match) | low | |
| Move FLEE_MOVE: intent.kind | **Unknown (IntentKind.Unknown)** | **EscapeIntent** | **1C** | **HIGH** | Q1 maps EscapeIntent → Unknown. No engine Escape mechanic at Phase-1. FLEE_MOVE self-loops; FatGremlin stays alive. In upstream `CreatureCmd.Escape(base.Creature)` removes monster. Directly enables q1=21 vs golden=13 extension. |
| Move FLEE_MOVE: self-loop | FLEE_MOVE → FLEE_MOVE | moveState2.FollowUpState=moveState2 | — (match) | low | |
| Initial move | SPAWNED_MOVE | MonsterMoveStateMachine(list, moveState=SPAWNED_MOVE) | — (match) | low | |

#### Monster: SneakyGremlin

| Dimension | Q1 | Upstream (A0) | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | MinHp=10, MaxHp=14 | MinInitialHp=GetValueIfAscension(ToughEnemies,11,10)=10; MaxInitialHp=GetValueIfAscension(ToughEnemies,15,14)=14 | — (match) | low | A0 identical. |
| Move SPAWNED_MOVE: intent.kind | Stun (IntentKind.Stun) | StunIntent | — (match) | low | Correct. |
| Move SPAWNED_MOVE: transition | → TACKLE_MOVE | moveState.FollowUpState=moveState2 (TACKLE) | — (match) | low | |
| Move TACKLE_MOVE: intent.dmgPerHit | 9 | GetValueIfAscension(DeadlyEnemies,10,9)=9 | — (match) | low | A0 identical. |
| Move TACKLE_MOVE: intent.kind | Attack | SingleAttackIntent | — (match) | low | |
| Move TACKLE_MOVE: self-loop | TACKLE_MOVE → TACKLE_MOVE | moveState2.FollowUpState=moveState2 | — (match) | low | |
| Initial move | SPAWNED_MOVE | MonsterMoveStateMachine(list, moveState=SPAWNED_MOVE) | — (match) | low | |

**Sub-class distribution (§5):**
- **1D** (SurprisePower AfterDeath escape mechanic — FatGremlin doesn't flee): **HIGH** — primary driver of q1=21 vs golden=13.
- **1C** (FatGremlin FLEE_MOVE: EscapeIntent → Unknown): **HIGH** — enables the 1D extension.
- **1C** (DOUBLE_SMASH missing DebuffIntent composite): med — display only.
- **1C** (HEHE missing BuffIntent composite): med — display only.
- **1C** (GIMME gold-steal ThieveryPower stub): med — behavioral, no damage impact.
- Severity heat-map: **high=2, med=3, low=many**.

**Source-code root cause:** The `q1=21 golden=13` mismatch (62% delta; HIGH) is driven by the incomplete escape-mechanic port. In upstream, after GremlinMerc dies, SurprisePower spawns FatGremlin + SneakyGremlin. FatGremlin plays SPAWNED_MOVE (stun/no-op), then FLEE_MOVE which calls `CreatureCmd.Escape()` — removing itself from combat. In Q1, FLEE_MOVE maps to Intent.Unknown (no escape engine support) and self-loops — FatGremlin never escapes. Q1 must kill FatGremlin (13-17 HP) + SneakyGremlin (10-14 HP) after GremlinMerc dies, adding many extra records. The composite-intent mismatches on DOUBLE_SMASH and HEHE are display-only and do not contribute to the record-count divergence.

---

## §6 KaiserCrabBoss

**Authored by:** A.1.β (wave-51)

### Encounter: KaiserCrabBoss

Q1 file(s):
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/Phase1Encounters.cs` (class KaiserCrabBoss)
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/Phase1Monsters.cs` (classes Crusher, Rocket)

Upstream file(s):
- `~/development/projects/godot/sts2/src/Core/Models/Encounters/KaiserCrabBoss.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/Crusher.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/Rocket.cs`

Transitive deps enumerated (R17 §5.2; 2-hop limit):
- hop 1 (encounter → monsters): Crusher ("crusher" slot) + Rocket ("rocket" slot). Upstream `GenerateMonsters()` returns `[(Crusher.ToMutable(), "crusher"), (Rocket.ToMutable(), "rocket")]`. Q1 base constructor: `new[] { Crusher.CanonicalId, Rocket.CanonicalId }`. Shape matches: 2 monsters.
- hop 1 (monster-class hierarchies): Both inherit MonsterModel directly.
- hop 2 (monster → powers): BackAttackLeftPower — applied to Crusher at spawn. CrabRagePower — applied to Crusher at spawn. BackAttackRightPower — applied to Rocket at spawn. SurroundedPower — applied to Rocket's opponents at spawn. All 4 NOT in Q1's PowerIds catalog — engine fails-soft. Q1 declares via string literals: `"BackAttackLeftPower"`, `"CrabRagePower"`, `"BackAttackRightPower"`, `"SurroundedPower"`.
- hop 2 (monster → relics/cards): No relic interactions. No card spawns.
- hop 2 (monster → constants): Crusher: ThrashDamage=12, EnlargingStrikeDamage=4, BugStingDamage=6, BugStingRepeats=2, BugStingWeak/FrailStacks=2, AdaptStrengthGain=2, GuardedStrikeDamage=12, GuardedStrikeBlock=18. Rocket: TargetingReticleDamage=3, PrecisionBeamDamage=18, LaserDamage=31, ChargeUpStrengthGain=2. All inline.
- STOPPED at 2 hops. BackAttackLeft/Right and CrabRage power semantics are 3-hop into their respective Power classes. (3-hop note: CrabRagePower likely grants progressive armor/rage — investigate CrabRagePower.cs for wave-55 fix.)

Probe-observed divergence (wave-50/A.4 §Baseline; seed=42):
```
DIVERGE KaiserCrabBoss seed=42: record count mismatch q1=15 golden=60
```

**Severity note:** `q1=15 golden=60` — upstream generates **300% MORE** records than Q1. HIGH severity overall. Extreme ratio suggests Q1 bosses are dramatically less durable without their spawn powers.

#### Monster: Crusher

| Dimension | Q1 | Upstream (A0) | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | MinHp=209, MaxHp=209 | MinInitialHp=GetValueIfAscension(ToughEnemies,219,209)=209; MaxInitialHp=MinInitialHp=209 | — (match) | low | A0 single-value envelope identical. |
| Block init (spawn powers) | BackAttackLeftPower(1) + CrabRagePower(1) — fail-soft | AfterAddedToRoom: BackAttackLeft(1) + CrabRage(1) | **1C** | **HIGH** | Both power ids absent from Q1's catalog → 0 effective stacks. CrabRagePower likely provides progressive rage/armor (combat-duration impact); BackAttackLeft likely grants positional defense. Absence makes Crusher dramatically more fragile. (Pass-2 reclassification: med → HIGH based on 300% delta.) |
| Move THRASH_MOVE: intent.kind | Attack | SingleAttackIntent | — (match) | low | |
| Move THRASH_MOVE: intent.dmgPerHit | 12 | GetValueIfAscension(DeadlyEnemies,14,12)=12 | — (match) | low | A0 identical. |
| Move ENLARGING_STRIKE_MOVE: intent.kind | Attack | SingleAttackIntent | — (match) | low | |
| Move ENLARGING_STRIKE_MOVE: intent.dmgPerHit | 4 | GetValueIfAscension(DeadlyEnemies,4,4)=4 | — (match) | low | A0 identical. |
| Move BUG_STING_MOVE: intent.kind | **MultiAttack only** | **MultiAttackIntent + DebuffIntent** (two intents) | **1C** | **med** | Q1: `Intent.MultiAttack(BugStingDamage, BugStingRepeats)`. Upstream: `new MoveState(…, new MultiAttackIntent(BugStingDamage, BugStingTimes), new DebuffIntent())`. Missing DebuffIntent. Display-only. |
| Move BUG_STING_MOVE: intent.dmgPerHit | 6 | GetValueIfAscension(DeadlyEnemies,7,6)=6 | — (match) | low | A0 identical. |
| Move BUG_STING_MOVE: intent.hitCount | 2 | BugStingTimes=2 | — (match) | low | |
| Move BUG_STING_MOVE: AppliesPowers | Weak(2, Player) + Frail(2, Player) | WeakPower(2) + FrailPower(2) on targets | — (match) | low | Both in catalog. |
| Move ADAPT_MOVE: intent.kind | Buff | BuffIntent | — (match) | low | |
| Move ADAPT_MOVE: AppliesPowers | Strength(2, self) | StrengthPower(GetValueIfAscension(DeadlyEnemies,3,2)=2) | — (match) | low | A0 value=2 identical. |
| Move GUARDED_STRIKE_MOVE: intent.kind | **Attack only** | **SingleAttackIntent + DefendIntent** (two intents) | **1C** | **med** | Q1: `Intent.Attack(GuardedStrikeDamage)`. Upstream: `new MoveState(…, new SingleAttackIntent(GuardedStrikeDamage), new DefendIntent())`. Missing DefendIntent. Display-only. |
| Move GUARDED_STRIKE_MOVE: intent.dmgPerHit | 12 | GetValueIfAscension(DeadlyEnemies,14,12)=12 | — (match) | low | A0 identical. |
| Move GUARDED_STRIKE_MOVE: SelfBlockGain | 18 (GuardedStrikeBlock) | `CreatureCmd.GainBlock(self, 18m, ValueProp.Move, null)` | — (match) | low | Correct. Q1 carries SelfBlockGain=18 in MonsterMove declaration. |
| Transition: THRASH → ENLARGING_STRIKE | deterministic FollowUp | moveState.FollowUpState=moveState2 | — (match) | low | |
| Transition: ENLARGING_STRIKE → BUG_STING | deterministic FollowUp | moveState2.FollowUpState=moveState3 | — (match) | low | |
| Transition: BUG_STING → ADAPT | deterministic FollowUp | moveState3.FollowUpState=moveState4 | — (match) | low | |
| Transition: ADAPT → GUARDED_STRIKE | deterministic FollowUp | moveState4.FollowUpState=moveState5 | — (match) | low | |
| Transition: GUARDED_STRIKE → THRASH | deterministic FollowUp (loop) | moveState5.FollowUpState=moveState | — (match) | low | |
| Initial move | THRASH_MOVE | MonsterMoveStateMachine(list, moveState=THRASH_MOVE) | — (match) | low | |

#### Monster: Rocket

| Dimension | Q1 | Upstream (A0) | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | MinHp=199, MaxHp=199 | MinInitialHp=GetValueIfAscension(ToughEnemies,209,199)=199; MaxInitialHp=MinInitialHp=199 | — (match) | low | A0 single-value envelope identical. |
| Block init (spawn powers) | BackAttackRightPower(1) + CrabRagePower(1) + SurroundedPower(1) — fail-soft | AfterAddedToRoom: Surrounded(1) on opponents + BackAttackRight(1) + CrabRage(1) on self | **1C** | **HIGH** | All 3 power ids absent from Q1's catalog → fail-soft. SurroundedPower in upstream applies to player (reducing player damage output); BackAttackRight + CrabRage affect Rocket's combat durability. Combined absence makes Rocket dramatically more fragile. (Pass-2 reclassification: med → HIGH.) |
| Move TARGETING_RETICLE_MOVE: intent.kind | Attack | SingleAttackIntent | — (match) | low | |
| Move TARGETING_RETICLE_MOVE: intent.dmgPerHit | 3 | GetValueIfAscension(DeadlyEnemies,4,3)=3 | — (match) | low | A0 identical. |
| Move PRECISION_BEAM_MOVE: intent.kind | Attack | SingleAttackIntent | — (match) | low | |
| Move PRECISION_BEAM_MOVE: intent.dmgPerHit | 18 | GetValueIfAscension(DeadlyEnemies,20,18)=18 | — (match) | low | A0 identical. |
| Move CHARGE_UP_MOVE: intent.kind | Buff | BuffIntent | — (match) | low | |
| Move CHARGE_UP_MOVE: AppliesPowers | Strength(2, self) | StrengthPower(GetValueIfAscension(DeadlyEnemies,3,2)=2) | — (match) | low | A0 value=2 identical. |
| Move LASER_MOVE: intent.kind | Attack | SingleAttackIntent | — (match) | low | |
| Move LASER_MOVE: intent.dmgPerHit | 31 | GetValueIfAscension(DeadlyEnemies,35,31)=31 | — (match) | low | A0 identical. |
| Move RECHARGE_MOVE: intent.kind | **Buff (Intent.Buff())** | **SleepIntent** | **1C** | **med** | Q1: `Intent.Buff()`. Upstream: `new MoveState("RECHARGE_MOVE", …, new SleepIntent())`. Same gap as §4 Lagavulin SLEEP_MOVE: `Intent.cs` has no `Sleep()` factory despite `IntentKind.Sleep=4` existing. Display-only at combat-record level. |
| Transition: TARGETING_RETICLE → PRECISION_BEAM | deterministic FollowUp | moveState.FollowUpState=moveState2 | — (match) | low | |
| Transition: PRECISION_BEAM → CHARGE_UP | deterministic FollowUp | moveState2.FollowUpState=moveState3 | — (match) | low | |
| Transition: CHARGE_UP → LASER | deterministic FollowUp | moveState3.FollowUpState=moveState4 | — (match) | low | |
| Transition: LASER → RECHARGE | deterministic FollowUp | moveState4.FollowUpState=moveState5 | — (match) | low | |
| Transition: RECHARGE → TARGETING_RETICLE | deterministic FollowUp (loop) | moveState5.FollowUpState=moveState | — (match) | low | |
| Initial move | TARGETING_RETICLE_MOVE | MonsterMoveStateMachine(list, moveState=TARGETING_RETICLE_MOVE) | — (match) | low | |

**Sub-class distribution (§6):**
- **1C** (Crusher spawn powers BackAttackLeft+CrabRage fail-soft): **HIGH** — primary driver of q1=15 vs golden=60 (Pass-2 reclassification).
- **1C** (Rocket spawn powers BackAttackRight+CrabRage+Surrounded fail-soft): **HIGH** (Pass-2 reclassification).
- **1C** (Rocket RECHARGE_MOVE: Buff instead of SleepIntent): med — display only (same Intent.Sleep() gap as §4).
- **1C** (BUG_STING missing DebuffIntent composite): med — display only.
- **1C** (GUARDED_STRIKE missing DefendIntent composite): med — display only.
- No 1A / 1B / 1D.
- Severity heat-map: **high=2, med=3, low=many**.

**Source-code root cause:** The `q1=15 golden=60` divergence (300% MORE upstream records; HIGH) is NOT caused by monster-model numerical drift — damage values, HP, transitions, self-block, and power stack amounts all match at A0. The root cause is **fail-soft of 4 spawn powers** (CrabRagePower, BackAttackLeftPower, BackAttackRightPower, SurroundedPower). CrabRagePower's exact semantics require 3-hop analysis, but the extreme delta indicates progressive armor or counter-attack stacks that dramatically extend the boss fight in upstream. BackAttack powers likely grant positional attack bonuses. SurroundedPower on players may reduce player damage output. Combined, Q1's Crusher and Rocket die in a fraction of the turns. The composite-intent gaps on BUG_STING and GUARDED_STRIKE are display-only.

---

## §7 CeremonialBeastBoss

**Authored by:** A.1.β (wave-51)

### Encounter: CeremonialBeastBoss

Q1 file(s):
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/Phase1Encounters.cs` (class CeremonialBeastBoss)
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/Phase1Monsters.cs` (class CeremonialBeast)

Upstream file(s):
- `~/development/projects/godot/sts2/src/Core/Models/Encounters/CeremonialBeastBoss.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/CeremonialBeast.cs`

Transitive deps enumerated (R17 §5.2; 2-hop limit):
- hop 1 (encounter → monsters): CeremonialBeast only. Upstream `GenerateMonsters()` returns `(CeremonialBeast.ToMutable(), null)`. Q1: `new[] { CeremonialBeast.CanonicalId }`. Shape matches: 1 monster.
- hop 1 (monster-class hierarchies): CeremonialBeast inherits MonsterModel directly.
- hop 2 (monster → powers): PlowPower — applied by STAMP_MOVE (upstream: `PowerCmd.Apply\<PlowPower\>(self, PlowAmount=150)`). NOT in Q1's PowerIds catalog; Q1 STAMP_MOVE is `Intent.Buff()` placeholder — PlowPower entirely elided. StrengthPower — applied by PLOW_MOVE (+2) and CRUSH_MOVE (+3); in catalog. RingingPower — applied by BEAST_CRY_MOVE (+1 to player); NOT in catalog.
- hop 2 (monster → relics/cards): No relic interactions. No card spawns.
- hop 2 (monster → constants): PlowAmount=150 (A0; GetValueIfAscension(DeadlyEnemies,160,150)), PlowDamage=18 (A0; GetValueIfAscension(DeadlyEnemies,20,18)), PlowStrength=2, StompDamage=15 (A0; GetValueIfAscension(DeadlyEnemies,17,15)), CrushDamage=17 (A0; GetValueIfAscension(DeadlyEnemies,19,17)), CrushStrength=3 (A0; GetValueIfAscension(DeadlyEnemies,4,3)). All confirmed in Q1's CeremonialBeast class.
- STOPPED at 2 hops. PlowPower's charge-and-stun mechanics are 3-hop into PlowPower implementation + CombatEngine power-removal hooks. (3-hop note: investigate PlowPower.cs + power-removal event hooks for wave-53-B fix.)

Probe-observed divergence (wave-50/A.4 §Baseline; seed=42):
```
DIVERGE CeremonialBeastBoss seed=42 turn=1 side=player-pre field=Energy q1=4 golden=3
```

**Notable observation:** CeremonialBeastBoss is the **only encounter with a field-specific diagnostic** (not a record-count mismatch). Q1 and upstream generate the same number of records; the first divergence is `Energy q1=4 golden=3` at turn=1 player-pre. This is NOT a monster-model issue.

#### Monster: CeremonialBeast

| Dimension | Q1 | Upstream (A0) | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | MinHp=252, MaxHp=252 | MinInitialHp=GetValueIfAscension(ToughEnemies,262,252)=252; MaxInitialHp=MinInitialHp=252 | — (match) | low | A0 single-value envelope identical. |
| Block init | 0 | 0 | — (match) | low | |
| Move STAMP_MOVE: intent.kind | Buff | BuffIntent | — (match) | low | |
| Move STAMP_MOVE: PlowPower application | **ABSENT** — Q1 STAMP_MOVE has no AppliesPowers | Upstream: `PowerCmd.Apply\<PlowPower\>(self, PlowAmount=150)` | **1C** | **med** | Q1 STAMP_MOVE emits Buff but applies no PlowPower. PlowPower governs stun-transition to Phase 2. Since record counts match golden at seed=42, stun doesn't fire within this probed run. |
| Move PLOW_MOVE: intent.kind | **Attack only** | **SingleAttackIntent + BuffIntent** (two intents) | **1C** | **med** | Q1: `Intent.Attack(PlowDamage)`. Upstream: `new MoveState(…, new SingleAttackIntent(PlowDamage), new BuffIntent())`. Missing BuffIntent. Display-only. |
| Move PLOW_MOVE: intent.dmgPerHit | 18 | GetValueIfAscension(DeadlyEnemies,20,18)=18 | — (match) | low | A0 identical. |
| Move PLOW_MOVE: AppliesPowers (Strength self) | MonsterIntentPower(Strength, 2) | PowerCmd.Apply\<StrengthPower\>(self, PlowStrength=2) | — (match) | low | A0 identical. |
| Move PLOW_MOVE: self-loop | PLOW_MOVE → PLOW_MOVE | moveState2.FollowUpState=moveState2 | — (match) | low | |
| Move STUN_MOVE: reachability | Dead code (PlowPower never applied; stun never triggered) | Upstream STUN_MOVE reached when PlowPower stripped (MustPerformOnceBeforeTransitioning=true) | **1D** | **med** | Q1 declares STUN_MOVE in list but unreachable since PlowPower never applied. Upstream's stun-transition is power-removal-keyed; Q1 lacks PlowPower infrastructure. |
| Move BEAST_CRY_MOVE: intent.kind | Debuff | DebuffIntent | — (match) | low | Encoded correctly; unreachable in Q1 practice. |
| Move BEAST_CRY_MOVE: AppliesPowers | **ABSENT** — no AppliesPowers | Upstream: `PowerCmd.Apply\<RingingPower\>(targets, 1)` | **1C** | **med** | RingingPower not in Q1's PowerIds catalog. BEAST_CRY applies no powers. Moot until Phase 2 reachable. |
| Move CRUSH_MOVE: intent.kind | **Attack only** | **SingleAttackIntent + BuffIntent** | **1C** | **med** | Q1: `Intent.Attack(CrushDamage)`. Upstream: `new MoveState(…, new SingleAttackIntent(CrushDamage), new BuffIntent())`. Missing BuffIntent. Display-only; unreachable in Q1 practice. |
| Move CRUSH_MOVE: intent.dmgPerHit | 17 | GetValueIfAscension(DeadlyEnemies,19,17)=17 | — (match) | low | A0 identical. |
| Move CRUSH_MOVE: AppliesPowers (Strength self) | MonsterIntentPower(Strength, 3) | PowerCmd.Apply\<StrengthPower\>(self, CrushStrength=3) | — (match) | low | A0 value=3 identical. |
| Move STOMP_MOVE: intent.dmgPerHit | 15 | GetValueIfAscension(DeadlyEnemies,17,15)=15 | — (match) | low | A0 identical. |
| Transition: STAMP → PLOW | deterministic FollowUp | moveState.FollowUpState=moveState2 | — (match) | low | |
| Transition: PLOW → PLOW (self-loop) | deterministic self-loop | moveState2.FollowUpState=moveState2 | — (match) | low | |
| Transition: STUN → BEAST_CRY | deterministic FollowUp | moveState3.FollowUpState=BeastCryState | — (match) | low | Correct; unreachable. |
| Transition: BEAST_CRY → STOMP | deterministic FollowUp | BeastCryState.FollowUpState=moveState4 | — (match) | low | |
| Transition: STOMP → CRUSH | deterministic FollowUp | moveState4.FollowUpState=moveState5 | — (match) | low | |
| Transition: CRUSH → BEAST_CRY | deterministic Phase 2 loop | moveState5.FollowUpState=BeastCryState | — (match) | low | |
| Initial move | STAMP_MOVE | MonsterMoveStateMachine(list, moveState=STAMP_MOVE) | — (match) | low | |
| **Player Energy (CombatEngine constant)** | **q1=4 (BaseEnergyPerTurnSilent=4)** | **golden=3 (upstream Silent player starts with 3 energy)** | **1E** | **med** | The actual probed divergence. NOT a monster-model issue. Q1's `CombatEngine.BaseEnergyPerTurnSilent = 4` vs upstream's 3. First confirmed **1E** (CombatEngine player-state divergence) in the audit. Fix: change constant to 3 (~1 LOC). |

**Sub-class distribution (§7):**
- **1E** (Energy q1=4 golden=3 — CombatEngine BaseEnergyPerTurnSilent=4 vs 3): **med** — the actual probe divergence; NOT a monster-model issue. First confirmed 1E instance.
- **1C** (STAMP_MOVE PlowPower absent): med — Phase-1 scope limitation.
- **1C** (PLOW_MOVE missing BuffIntent composite): med — display only.
- **1C** (CRUSH_MOVE missing BuffIntent composite): med — display only; unreachable.
- **1C** (BEAST_CRY_MOVE RingingPower absent): med — unreachable.
- **1D** (STUN_MOVE: PlowPower-removal transition → dead code): med.
- Severity heat-map: **high=0, med=6, low=many**.

**Source-code root cause:** The `Energy q1=4 golden=3` divergence (field-specific diagnostic; matching record counts) has a clean root cause: Q1's `CombatEngine.BaseEnergyPerTurnSilent = 4` vs upstream's 3. This is a CombatEngine-layer divergence (1E), not a CeremonialBeast monster-model issue. The matching record counts (combat ends at same turn) while only Energy diverges confirms the probe's field-specific diagnostic capability. The CeremonialBeast model divergences (PlowPower elision, missing composite intents, BEAST_CRY_MOVE absent RingingPower) are Phase-1 scope limitations that don't affect the probed combat outcome at seed=42. This encounter first confirms **1E** as a real sub-class.

---

## §8 NibbitsWeak

**Authored by:** A.1.β (wave-51)

### Encounter: NibbitsWeak

Q1 file(s):
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/NibbitsWeak.cs` (class NibbitsWeak)
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/Nibbit.cs` (class Nibbit)

Upstream file(s):
- `~/development/projects/godot/sts2/src/Core/Models/Encounters/NibbitsWeak.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/Nibbit.cs`

Transitive deps enumerated (R17 §5.2; 2-hop limit):
- hop 1 (encounter → monsters): Nibbit ×1 (IsAlone). Upstream `GenerateMonsters()`: sets `nibbit.IsAlone = true`, returns `[(nibbit, null)]`. Q1: `base(CanonicalId, new[] { Nibbit.CanonicalId })` — spawns 1 Nibbit with default `initialMoveId = ButtMoveId`. Q1 does NOT set IsAlone=true (Q1 Nibbit has no mutable IsAlone field; immutable model).
- hop 1 (monster-class hierarchies): Nibbit inherits MonsterModel directly.
- hop 2 (monster → powers): StrengthPower — applied by HISS_MOVE; in catalog. No spawn powers.
- hop 2 (monster → relics/cards): No relic interactions. No card spawns.
- hop 2 (monster → constants): ButtDamage=12, SliceDamage=6, SliceSelfBlock=5, HissStrengthStacks=2, MinHp=42, MaxHp=46. All inline.
- STOPPED at 2 hops. ConditionalBranchState runtime condition evaluation (IsAlone/IsFront flag reading) is 3-hop. (3-hop note: investigate Nibbit.GenerateMoveStateMachine() flag-propagation timing for wave-53-C fix.)

Probe-observed divergence (wave-50/A.4 §Baseline; seed=42):
```
DIVERGE NibbitsWeak seed=42: record count mismatch q1=22 golden=13
```

**Wave-49/A.4 E6 finding:** NibbitsWeak Q1=BUTT_MOVE, golden=HISS_MOVE at Turn=0. This audit provides root-cause analysis.

#### Monster: Nibbit (NibbitsWeak — IsAlone mode)

| Dimension | Q1 | Upstream (A0) | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp | MinHp=42, MaxHp=46 | MinInitialHp=GetValueIfAscension(ToughEnemies,44,42)=42; MaxInitialHp=GetValueIfAscension(ToughEnemies,48,46)=46 | — (match) | low | A0 identical. |
| Block init | 0 | 0 | — (match) | low | |
| Move BUTT_MOVE: intent.dmgPerHit | 12 | GetValueIfAscension(DeadlyEnemies,13,12)=12 | — (match) | low | A0 identical. |
| Move BUTT_MOVE: intent.kind | Attack | SingleAttackIntent | — (match) | low | |
| Move SLICE_MOVE: intent.kind | **Attack only** | **SingleAttackIntent + DefendIntent** (two intents) | **1C** | **med** | Q1: `Intent.Attack(SliceDamage)`. Upstream: `new MoveState(…, new SingleAttackIntent(SliceDamage), new DefendIntent())`. Missing DefendIntent. Display-only. |
| Move SLICE_MOVE: intent.dmgPerHit | 6 | GetValueIfAscension(DeadlyEnemies,7,6)=6 | — (match) | low | A0 identical. |
| Move SLICE_MOVE: SelfBlockGain | 5 | `CreatureCmd.GainBlock(self, GetValueIfAscension(ToughEnemies,6,5)=5)` | — (match) | low | A0 value=5 identical. Correct. |
| Move HISS_MOVE: intent.kind | Buff | BuffIntent | — (match) | low | |
| Move HISS_MOVE: AppliesPowers | Strength(2, self) | StrengthPower(GetValueIfAscension(DeadlyEnemies,3,2)=2) | — (match) | low | A0 value=2 identical. |
| Transition: BUTT → SLICE | deterministic FollowUp | moveState.FollowUpState=moveState2 | — (match) | low | |
| Transition: SLICE → HISS | deterministic FollowUp | moveState2.FollowUpState=moveState3 | — (match) | low | |
| Transition: HISS → BUTT | deterministic FollowUp (loop) | moveState3.FollowUpState=moveState | — (match) | low | |
| **INIT_MOVE routing (NibbitsWeak — IsAlone)** | **Q1: starts at BUTT_MOVE** (default initialMoveId) | **Upstream source: ConditionalBranchState("INIT_MOVE"), IsAlone=true → BUTT_MOVE branch. Upstream golden: HISS_MOVE** | **1D** | **HIGH** | Wave-49/E6 probe: Q1=BUTT_MOVE, golden=HISS_MOVE. Despite upstream source code routing `IsAlone=true → BUTT_MOVE`, the upstream runtime golden captured HISS_MOVE. This indicates `IsAlone` flag may NOT propagate to the runtime Nibbit instance when GenerateMoveStateMachine() evaluates the ConditionalBranchState condition. Without IsAlone=true, the `!IsFront` branch (IsFront=false by default → `!IsFront=true`) routes to HISS_MOVE. Q1 defaults to BUTT_MOVE (initialMoveId); upstream runtime resolves HISS_MOVE. Both diverge from the golden=HISS_MOVE ground truth. |

**Sub-class distribution (§8):**
- **1D** (INIT_MOVE routing — IsAlone flag propagation issue; Q1=BUTT vs golden=HISS): **HIGH** — primary driver of q1=22 vs golden=13.
- **1C** (SLICE_MOVE missing DefendIntent composite): med — display only.
- Severity heat-map: **high=1, med=1, low=many**.

**Source-code root cause:** The `q1=22 golden=13` mismatch (69% delta; HIGH) is primarily driven by the **INIT_MOVE divergence**. Q1 starts NibbitsWeak Nibbit at BUTT_MOVE; upstream golden captured HISS_MOVE (wave-49/E6). Root cause: upstream NibbitsWeak sets `nibbit.IsAlone = true` on the mutable copy, but the ConditionalBranchState condition `(() => ((Nibbit)…).IsAlone)` may be evaluated on the immutable model reference rather than the mutable instance — causing IsAlone to read false at evaluation time, routing to `!IsFront=true → HISS_MOVE`. Q1 lacks the runtime-flag infrastructure; defaults to BUTT_MOVE. Both miss the golden=HISS_MOVE opening. The 9-record gap is consistent with different rotation starting points.

---

## §9 NibbitsNormal

**Authored by:** A.1.β (wave-51)

### Encounter: NibbitsNormal

Q1 file(s):
- `engine/headless/src/Sts2Headless.Domain/Content/Encounters/NibbitsNormal.cs` (class NibbitsNormal)
- `engine/headless/src/Sts2Headless.Domain/Content/Monsters/Nibbit.cs` (class Nibbit)

Upstream file(s):
- `~/development/projects/godot/sts2/src/Core/Models/Encounters/NibbitsNormal.cs`
- `~/development/projects/godot/sts2/src/Core/Models/Monsters/Nibbit.cs`

Transitive deps enumerated (R17 §5.2; 2-hop limit):
- hop 1 (encounter → monsters): Nibbit ×2 ("front" + "back"). Upstream `GenerateMonsters()`: sets `nibbit.IsFront = true` on slot 0 (front); slot 1 has IsFront=false, IsAlone=false (default). Returns `[(nibbit_front, "front"), (nibbit_back, "back")]`. Q1 `NibbitsNormal.GenerateMonstersWithMoves()`: returns `[(Nibbit, SLICE_MOVE), (Nibbit, HISS_MOVE)]`.
- hop 1 (monster-class hierarchies): Nibbit inherits MonsterModel directly.
- hop 2 (monster → powers): StrengthPower — applied by HISS_MOVE; in catalog. No spawn powers.
- hop 2 (monster → constants): Same Nibbit A0 constants as §8.
- STOPPED at 2 hops. ConditionalBranchState IsFront/IsAlone runtime evaluation is 3-hop. (3-hop note: same flag-propagation issue as §8 — investigate for wave-53-C fix.)

Probe-observed divergence (wave-50/A.4 §Baseline; seed=42):
```
DIVERGE NibbitsNormal seed=42: record count mismatch q1=18 golden=21
```

**Wave-49/A.4 E6 finding:** NibbitsNormal Q1=SLICE_MOVE, golden=HISS_MOVE for slot 0 at Turn=0.

#### Monster: Nibbit (NibbitsNormal — IsFront=true slot 0, IsFront=false slot 1)

| Dimension | Q1 | Upstream (A0) | Sub-class | Severity | Notes |
|---|---|---|---|---|---|
| HP / MaxHp (both Nibbits) | MinHp=42, MaxHp=46 | Same A0 values | — (match) | low | A0 identical for both slots. |
| Block init | 0 | 0 | — (match) | low | |
| Move damage values | ButtDamage=12, SliceDamage=6, SliceSelfBlock=5, HissStrengthStacks=2 | Same A0 values | — (match) | low | All A0 identical. |
| Move SLICE_MOVE: intent.kind | **Attack only** | **SingleAttackIntent + DefendIntent** | **1C** | **med** | Missing DefendIntent. Display-only. Same as §8. |
| **INIT_MOVE: slot 0 (front Nibbit)** | **Q1: SLICE_MOVE** | **Upstream source: IsFront=true → SLICE_MOVE. Upstream golden: HISS_MOVE** | **1D** | **HIGH** | Wave-49/E6: Q1=SLICE_MOVE, golden=HISS_MOVE for slot 0. Same IsFront flag-propagation issue as §8: `IsFront=true` set in NibbitsNormal.GenerateMonsters() may not survive through mutable-copy chain when GenerateMoveStateMachine() evaluates ConditionalBranchState condition. If IsFront reads false at evaluation, `!IsFront=true` routes slot 0 to HISS_MOVE. Q1's SLICE_MOVE override is wrong relative to upstream runtime golden. |
| **INIT_MOVE: slot 1 (back Nibbit)** | **Q1: HISS_MOVE** | **Upstream: IsFront=false → !IsFront=true → HISS_MOVE** | — (match expected) | low | Source code and Q1 both route slot 1 to HISS_MOVE. Consistent with flag=false default. |
| Transition: all moves | BUTT→SLICE→HISS→BUTT (loop) | Same deterministic loop | — (match) | low | |

**Sub-class distribution (§9):**
- **1D** (INIT_MOVE slot-0 assignment — Q1 SLICE_MOVE; upstream runtime HISS_MOVE): **HIGH** — primary driver of q1=18 vs golden=21.
- **1C** (SLICE_MOVE missing DefendIntent composite): med — display only.
- Severity heat-map: **high=1, med=1, low=many**.

**Source-code root cause:** The `q1=18 golden=21` mismatch (17% delta; med/high border) is driven by the **slot-0 initial-move divergence**. Q1 starts slot 0 at SLICE_MOVE; upstream golden captured HISS_MOVE for slot 0 (wave-49/E6). Root cause: same IsFront flag-propagation issue as §8 — NibbitsNormal.GenerateMonsters() sets `nibbit.IsFront = true` on the mutable copy, but ConditionalBranchState condition evaluation may read IsFront=false at machine-construction time, routing slot 0 to `!IsFront=true → HISS_MOVE`. Q1's explicit `[SLICE, HISS]` override has SLICE for slot 0, but upstream runtime resolves HISS for slot 0. Fix: change Q1's slot-0 override from SLICE_MOVE to HISS_MOVE to match upstream runtime golden.

---

## §10 Cross-Encounter Pattern Analysis (Pass 2)

**Authored by:** A.1.β (wave-51) — Pass 2 synthesis after all 9 encounters' Pass 1 complete.

### §10.1 Sub-Class Tally Across §1-§9

| Sub-class | §1 Cultists | §2 Louse | §3 Exoskeletons | §4 Lagavulin | §5 GremlinMerc | §6 KaiserCrab | §7 CeremonialBeast | §8 NibbitsWeak | §9 NibbitsNormal | Total |
|---|---|---|---|---|---|---|---|---|---|---|
| 1A (1-move stub) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | **0** |
| 1B (encounter shape) | 0 | 0 | 1 HIGH | 0 | 0 | 0 | 0 | 0 | 0 | **1** |
| 1C (numerical/composite drift) | 0 | 2 med | 2 med | 7 (2H+5M) | 5 (2H+3M) | 5 (2H+3M) | 5 med | 2 med | 2 med | **30** |
| 1D (transition-rule drift) | 0 | 0 | 1 med | 1 HIGH | 1 HIGH | 0 | 1 med | 1 HIGH | 1 HIGH | **6** |
| 1E (CombatEngine layer) | 1 implied | 1 implied | — | — | — | 1 implied | 1 confirmed | — | — | **4** |

**Total non-trivial sub-class instances: 1B=1, 1C=30, 1D=6, 1E=4.** (1A=0 — no 1-move stub encounters remain in the probed set.)

### §10.2 Dominant Sub-Class: 1C (Numerical/Composite Drift)

**1C is the dominant sub-class**, appearing in every encounter. Two sub-patterns:

**Pattern 1C-α: Missing composite intents (13+ move-level gaps; 7 of 9 encounters).** Upstream MoveState constructors accept variadic intent lists; Q1's MonsterMove encodes a single Intent value. Affected moves:
- §2: WEB_CANNON (missing DebuffIntent), CURL_AND_GROW (missing BuffIntent)
- §4: SLASH2 (missing DefendIntent), SOUL_SIPHON (missing BuffIntent), SLEEP_MOVE (Buff→SleepIntent)
- §5: DOUBLE_SMASH (missing DebuffIntent), HEHE (missing BuffIntent)
- §6: BUG_STING (missing DebuffIntent), GUARDED_STRIKE (missing DefendIntent), RECHARGE_MOVE (Buff→SleepIntent)
- §7: PLOW_MOVE (missing BuffIntent), CRUSH_MOVE (missing BuffIntent)
- §8 + §9: SLICE_MOVE (missing DefendIntent)

Root cause: Q1's MonsterMove data model supports exactly 1 intent kind; upstream's MoveState takes variadic params (1-3 intents). This is a **Q1 data-model limitation**, not a per-encounter porting error. Fix: add SecondaryIntents collection to MonsterMove (Wave-54-C).

**Pattern 1C-β: Fail-soft spawn powers (combat-correctness impact).** Powers not in Q1's PowerIds catalog → engine fails-soft → 0 effective stacks:
- §3: HardToKillPower (9 stacks; string literal correct; no catalog entry)
- §4: PlatingPower ("PlatedArmorPower" id mismatch — only naming-convention error in all 9 encounters)
- §6: CrabRagePower + BackAttackLeftPower + BackAttackRightPower + SurroundedPower (4 powers; HIGH; root cause of 300% delta)
- §7: PlowPower (structural Phase-1 elision), RingingPower (absent from catalog)

### §10.3 Second-Dominant Sub-Class: 1D (Transition-Rule Drift)

**1D appears in 5 of 9 encounters** and drives the highest-impact record-count deltas:

- §3 ExoskeletonsNormal: SKITTER→RAND CannotRepeat elision (RngBranchResolver doesn't zero just-played weight). med.
- §4 LagavulinElite: AsleepPower wake-condition replaced by HpThreshold proxy. **HIGH** — primary driver of 122% delta.
- §5 GremlinMercNormal: Escape mechanic missing (FatGremlin FLEE_MOVE stays alive). **HIGH** — primary driver of 62% delta.
- §8 NibbitsWeak: IsAlone flag not set; INIT_MOVE routes to BUTT vs golden HISS. **HIGH** — primary driver of 69% delta.
- §9 NibbitsNormal: Slot-0 IsFront flag propagation; INIT_MOVE routes to SLICE vs golden HISS. **HIGH**.

**1D divergences are the primary drivers of record-count deltas.** All 4 HIGH-severity encounters have 1D as root cause.

### §10.4 New Sub-Class: 1E — CombatEngine Layer Gap

**A.1.β Pass 2 introduces sub-class 1E** to the R15 taxonomy:

| Code | Name | Description | Origin |
|---|---|---|---|
| **1E** | **CombatEngine layer gap** | Q1 monster substrate is byte-faithful but probe DIVERGES due to CombatEngine multi-turn behavior or player-state constants. Monster-model fix alone cannot close the divergence. | §1 Cultists (implied Ritual→Strength multi-turn); §2 Louse (implied Strength accumulation); §7 CeremonialBeast (confirmed: BaseEnergyPerTurnSilent=4 vs 3) |

**1E instances:**
- **§1 (implied):** Ritual→Strength multi-turn accumulation in CombatEngine turn-end hook. Medium confidence (inferred from byte-faithful substrate + residual record-count delta).
- **§2 (implied):** StrengthPower amplification multi-turn CombatEngine gap. Medium confidence (same inference).
- **§7 (confirmed):** `BaseEnergyPerTurnSilent=4` vs upstream 3 — explicit field diagnostic (`Energy q1=4 golden=3`). HIGH confidence. Fix: ~1 LOC constant change.

### §10.5 Encounter-Level Severity Summary

| Encounter | Probe diagnostic (seed=42) | High-sev items | Dominant sub-class | Primary root cause |
|---|---|---|---|---|
| §1 CultistsNormal | q1=15, golden=18 (−17%) | 0 | 1E (implied) | Ritual→Strength multi-turn CombatEngine gap |
| §2 LouseProgenitorNormal | q1=21, golden=27 (−22%) | 0 | 1E (implied) | StrengthPower accumulation CombatEngine gap |
| §3 ExoskeletonsNormal | q1=18, golden=21 (−14%) | 1 | **1B** | Wrong encounter variant (3 vs 4 Exoskeletons) |
| §4 LagavulinElite | q1=60, golden=27 (+122%) | 2 | **1D** | AsleepPower wake-condition (HP proxy) |
| §5 GremlinMercNormal | q1=21, golden=13 (+62%) | 2 | **1D** | Escape mechanic missing (FatGremlin stays alive) |
| §6 KaiserCrabBoss | q1=15, golden=60 (−75%) | 2 | **1C** (spawn powers) | CrabRage+BackAttack power fail-soft |
| §7 CeremonialBeastBoss | Energy q1=4 golden=3 | 0 | **1E** + 1C | BaseEnergyPerTurnSilent=4 vs 3 |
| §8 NibbitsWeak | q1=22, golden=13 (+69%) | 1 | **1D** | IsAlone INIT_MOVE routing (BUTT vs HISS) |
| §9 NibbitsNormal | q1=18, golden=21 (−14%) | 1 | **1D** | Slot-0 IsFront INIT_MOVE routing (SLICE vs HISS) |

**Aggregate high-severity across §5-§9:** 6 (§5: 2, §6: 2, §8: 1, §9: 1).
**Total high-severity across ALL 9 encounters (§1-§9):** 9 (A.1.α's 3 from §3-§4 + A.1.β's 6 from §5-§9).

### §10.6 Shared Root Causes and Recommended Fix Clusters for Wave-52+

| Cluster | Sub-class | Encounters | Fix description | Scope |
|---|---|---|---|---|
| Wave-52-A | **1E: BaseEnergyPerTurnSilent** | All (player-state; affects every encounter) | `CombatEngine.BaseEnergyPerTurnSilent = 3` (4→3) | ~1 LOC; immediate probe improvement for §7 |
| Wave-52-B | **1E: Ritual/Strength multi-turn engine** | §1, §2 | Verify+fix Ritual→Strength turn-end hook timing; StrengthPower→attack multiplication | ~medium engine work |
| Wave-53-A | **1D: Escape mechanic (FatGremlin)** | §5 GremlinMerc | Implement `CreatureCmd.Escape()` + wire FatGremlin FLEE_MOVE | ~medium engine + monster |
| Wave-53-B | **1D: AsleepPower + power-removal event** | §4 Lagavulin | Implement AsleepPower + power-removal subscription in CombatEngine | ~medium engine + power |
| Wave-53-C | **1D: Nibbits INIT_MOVE routing** | §8, §9 Nibbits | Change NibbitsWeak initialMoveId → HISS_MOVE; change NibbitsNormal slot-0 override → HISS_MOVE | ~small encounter fix |
| Wave-54-A | **1B: ExoskeletonsNormal 4-monster shape** | §3 | Add 4th Exoskeleton slot; clarify encounter naming | ~small |
| Wave-54-B | **1C: Intent.Sleep() factory** | §4, §6 | Add `public static Intent Sleep()` to `Intent.cs`; update 2 callers | ~2 LOC factory + 2 callers |
| Wave-54-C | **1C-α: Composite intents** | §2, §4, §5, §6, §7, §8, §9 (13 moves) | Add SecondaryIntents collection to MonsterMove; re-port 13 missing composite intents | ~medium data-model |
| Wave-55 | **1C-β: Spawn-power catalog** | §3, §4, §6, §7 | Add ids + stub/implement 8 missing powers; fix PlatingPower id string | ~medium power-catalog |

**Priority: Wave-52-A → Wave-53-A/B/C → Wave-54-A/B → Wave-52-B → Wave-55.** Wave-52-A has the highest ROI (1 LOC, closes §7 Energy divergence).

### §10.7 A.1.α 3-Flagged Cross-Tabulation Items — Explicit Resolution

**Item 1: CombatEngine multi-turn gap — did the pattern recur beyond §1?**
**YES, confirmed in §2 and implied in §6.** §2 LouseProgenitorNormal: byte-faithful monster substrate + record-count delta traced to StrengthPower multi-turn CombatEngine gap. §6 KaiserCrabBoss: fail-soft spawn powers with large delta (300%) suggests CombatEngine-layer impact from missing CrabRage/BackAttack powers (classified as 1C fail-soft, not 1E, since the spawn-power declarations are present in Q1 substrate). **New sub-class 1E formally introduced in §10.4.** CombatEngine multi-turn gap is a confirmed recurring class.

**Item 2: Intent.Sleep() factory missing — did the pattern recur beyond §4?**
**YES, confirmed in §6 Rocket.** Rocket's RECHARGE_MOVE uses `new SleepIntent()` in upstream; Q1 encodes as `Intent.Buff()`. Two confirmed cases: §4 LagavulinMatriarch.SleepMoveId + §6 Rocket.RechargeMoveId. Fix: add `public static Intent Sleep() => new Intent(IntentKind.Sleep, 0)` to `Intent.cs` (~1 LOC; Wave-54-B).

**Item 3: PowerIds naming convention gap — did the pattern recur beyond §4?**
**NO additional naming-convention mismatches.** §4 PlatingPower (`PowerIds.Plated = "PlatedArmorPower"` vs upstream class `PlatingPower`) is the only naming error. All §5-§9 fail-soft powers use correct class-name string literals (`"BackAttackLeftPower"`, `"CrabRagePower"`, etc.) — those are catalog-gap cases (correct strings, no PowerIds entry), not naming-convention errors. **Conclusion: §4 Plated is an isolated naming error; all other fail-soft cases are catalog-gap.**

### §10.8 Pass-1 → Pass-2 Classification Refinements

**§1 CultistsNormal and §2 LouseProgenitorNormal:** Pass 1 concluded "no sub-class assignments." Pass 2 assigns **1E (implied)** for the residual record-count delta unexplained by byte-faithful monster substrate. The §1/§2 monster-model diff tables remain correct; 1E is engine-layer attribution for the probe symptom.

**§6 KaiserCrabBoss spawn-power rows:** Pass 1 assigned med severity. Pass 2 reclassifies **both Crusher and Rocket spawn-power rows to HIGH** based on the 300% record-count delta — consistent with combat-correctness-level impact (bosses die 4x faster). The §6 diff table above reflects these HIGH labels (corrected inline during §6 authoring).

**No other Pass-1 sub-class labels require change.** 1A/1B/1C/1D assignments in §3, §4, §5, §7, §8, §9 are stable across Pass 2.

### §10.9 R17 §5.2 Self-Assessment

1. **Was the 2-hop limit effective?** Generally yes. All monster-model divergences (HP, damage values, composite intents, spawn powers, transition rules) were discoverable within 2 hops. CombatEngine internals (3-hop) correctly deferred — those are classified as 1E.

2. **Did 2-hop miss load-bearing dependencies?** Partially: (a) CrabRagePower/BackAttack power semantics (§6) — we identify the fail-soft and estimate HIGH impact but cannot characterize the exact mechanics without 3-hop reads. (b) AsleepPower removal mechanics (§4) — symptom diagnosed correctly; fix requires AsleepPower.cs reading. Both are documented with 3-hop notes in the transitive-deps sections; no re-surface triggered.

3. **Did 2-hop miss any monster class NOT in Q1 catalog?** No. All encountered monsters present in Q1: GremlinMerc, FatGremlin, SneakyGremlin, Crusher, Rocket, CeremonialBeast, Nibbit. No R17 instance #6 trigger.

4. **Suggested refinements:** For encounters with power-removal-keyed transitions or complex spawn-power semantics, add the specific 3-hop class name explicitly in the "STOPPED at 2 hops" line (already done in §6 and §7 in this audit). This costs nothing at audit time and significantly accelerates wave-52+ fixers.

(§11 to be authored by Q1 lead A.2 inline.)
