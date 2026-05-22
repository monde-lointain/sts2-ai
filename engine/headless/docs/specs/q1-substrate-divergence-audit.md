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

(§1-§11 to be authored by A.1.α/β subagents + A.2 Q1 lead inline; see plan for dispatch sequencing.)
