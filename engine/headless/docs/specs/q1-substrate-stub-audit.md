# Q1 Substrate Stub Audit (wave-48; R15 mitigation)

> Triggered by wave-47b/B HauntedShipSolo V1-DROPPED finding. Project-lead-authorized 2026-05-21 (wave-47b close response).
> Catalog-wide audit of Q1 substrate monsters vs upstream parity status.
> Generated 2026-05-21 against main HEAD `e52cc60`.

## §Summary

| # | Monster | Q1 location | Parity | Infrastructure needed |
|---|---|---|---|---|
| 1 | CalcifiedCultist | `Content/Monsters/CalcifiedCultist.cs` | FULL | (none) |
| 2 | DampCultist | `Content/Monsters/DampCultist.cs` | FULL | (none) |
| 3 | Chomper | `Phase1Monsters.cs:24` | PARTIAL | ArtifactPower spawn; ScreamFirst per-slot override; StatusCardIntent |
| 4 | Exoskeleton | `Phase1Monsters.cs:75` | PARTIAL | ConditionalBranchState (per-slot initial move); CannotRepeat tracking |
| 5 | LeafSlimeS | `Phase1Monsters.cs:459` | PARTIAL | StatusCardIntent (Slimed card mid-combat); CannotRepeat tracking |
| 6 | TwigSlimeS | `Phase1Monsters.cs:565` | FULL | (none) |
| 7 | LeafSlimeM | `Phase1Monsters.cs:521` | FULL | (none) |
| 8 | TwigSlimeM | `Phase1Monsters.cs:604` | PARTIAL | StatusCardIntent (Slimed card mid-combat); CannotRepeat tracking |
| 9 | BowlbugRock | `Phase1Monsters.cs:1119` | STUB | ConditionalBranchState (IsOffBalance stun gate); ImbalancedPower spawn; StunIntent; damage value wrong |
| 10 | BowlbugNectar | `Phase1Monsters.cs:1102` | PARTIAL | BUFF_MOVE missing; damage value wrong; rotation incomplete |
| 11 | BowlbugSilk | `Phase1Monsters.cs:1136` | STUB | Full alternation missing; TOXIC_SPIT Weak debuff absent; initial move wrong |
| 12 | FuzzyWurmCrawler | `Phase1Monsters.cs:137` | STUB | Full 3-move rotation missing; INHALE Strength +7 absent; damage value wrong |
| 13 | FossilStalker | `Phase1Monsters.cs:1165` | PARTIAL | TACKLE missing Frail(1) debuff application; CannotRepeat deviation |
| 14 | FrogKnight | `Phase1Monsters.cs:1221` | STUB | 1-move ATTACK vs 4-move ConditionalBranchState; PlatingPower spawn missing |
| 15 | LagavulinMatriarch | `Phase1Monsters.cs:801` | PARTIAL | AsleepPower replaced by HpThresholdResolver (deviation); SLASH2 missing SelfBlockGain; SOUL_SIPHON power-id mapping |
| 16 | HauntedShip | `Phase1Monsters.cs:307` | STUB | 1-move ATTACK vs 4-move RandomBranchState; HAUNT Weak+Dazed card injection; round-parity weights |
| 17 | LivingFog | `Phase1Monsters.cs:324` | STUB | 1-move ATTACK vs 3-move rotation; SmoggyPower debuff; GasBomb mid-combat spawn (SummonIntent) |
| 18 | GremlinMerc | `Phase1Monsters.cs:216` | FULL | (none) |
| 19 | FatGremlin | `Content/Monsters/FatGremlin.cs` | FULL | (none) |
| 20 | SneakyGremlin | `Content/Monsters/SneakyGremlin.cs` | FULL | (none) |
| 21 | Crusher | `Phase1Monsters.cs:927` | PARTIAL | Spawn powers fail-soft (unknown ids); RECHARGE SleepIntent missing |
| 22 | Rocket | `Phase1Monsters.cs:1020` | PARTIAL | RECHARGE mapped to Buff vs SleepIntent; spawn powers fail-soft |
| 23 | CeremonialBeast | `Phase1Monsters.cs:349` | PARTIAL | PlowPower stun-transition wiring absent; BEAST_CRY missing RingingPower debuff |
| 24 | LouseProgenitor | `Phase1Monsters.cs:162` | PARTIAL | PounceDamage wrong (Q1=16, upstream A0=14); WEB_CANNON missing Frail(2) debuff |
| 25 | Nibbit | `Content/Monsters/Nibbit.cs` | FULL | (none) |

**Counts: FULL 7 / PARTIAL 12 / STUB 6 of 25 in-scope.**

---

## §Per-monster findings

### 1. CalcifiedCultist
- **Q1**: INCANTATION (Buff + Ritual +2 self) → DARK_STRIKE (Attack 9, self-loop). HP 38–41. initialMove=INCANTATION.
- **Upstream**: Identical structure. HP 38–41 A0. IncantationAmount=2, DarkStrikeDamage=9 A0.
- **Match**: FULL
- **Notes**: Ascension variants (ToughEnemies HP +1, DeadlyEnemies damage +2) deferred uniformly across Q1; not a stub gap.

### 2. DampCultist
- **Q1**: INCANTATION (Buff + Ritual +5 self) → DARK_STRIKE (Attack 1, self-loop). HP 51–53. initialMove=INCANTATION.
- **Upstream**: Same structure. IncantationAmount=5 A0 (`GetValueIfAscension(DeadlyEnemies, 6, 5)`). DarkStrikeDamage=1 A0. HP 51–53 A0.
- **Match**: FULL
- **Notes**: Ascension variants deferred uniformly.

### 3. Chomper
- **Q1**: CLAMP (MultiAttack 8×2) → SCREECH (Status 3 Dazed cards, intent-only) → CLAMP (alternating). HP 60–64. initialMove=CLAMP.
- **Upstream**: Same 2-move alternation. `ScreamFirst` flag allows per-slot initial-move override (slot-1 starts SCREECH). `AfterAddedToRoom` applies ArtifactPower(2) to self. SCREECH adds Dazed cards to discard mid-combat. Upstream uses CannotRepeat-style alternation (static FollowUpState chains, not RngBranchResolver — actually equivalent here).
- **Match**: PARTIAL
- **Infrastructure gaps**:
  1. ArtifactPower spawn missing from Q1 — `spawnPowers` list absent on Chomper; upstream grants 2 Artifact at combat-start via `AfterAddedToRoom`.
  2. `ScreamFirst` per-slot override not wired in Q1 encounters — ChompersNormal slot-1 should start SCREECH; Q1 lacks the flag/override mechanism for Chomper specifically.
  3. StatusCardIntent: Dazed card injection mid-combat absent (Phase-1 documented deferral; intent-only).

### 4. Exoskeleton
- **Q1**: SKITTER (MultiAttack 1×3) → MANDIBLES (Attack 8) → ENRAGE (Buff + Strength +2) → RngBranchResolver 50/50 SKITTER|MANDIBLES. SpawnPower HardToKillPower(9). HP 24–28. initialMove=SKITTER.
- **Upstream**: Same moves. `ConditionalBranchState("INIT_MOVE")` selects initial move by slot: "first"→SKITTER, "second"→MANDIBLES, "third"→ENRAGE, "fourth"→RAND. `RandomBranchState` with `CannotRepeat` on both branches. HardToKillPower(9) spawn.
- **Match**: PARTIAL
- **Infrastructure gaps**:
  1. Per-slot initial-move (ConditionalBranchState "INIT_MOVE"): Q1 always starts SKITTER; ExoskeletonsNormal spawns exactly slots first/second/third so ENRAGE-first and MANDIBLES-first paths are unreachable in current encounters — low practical impact.
  2. CannotRepeat suppression on RAND: Q1 uses equal-weight RngBranchResolver without CannotRepeat; empirical distribution differs but since ENRAGE just fired, both SKITTER and MANDIBLES are eligible either way so no functional first-turn difference.

### 5. LeafSlimeS
- **Q1**: TACKLE (Attack 3) + GOOP (Status 1 Slimed intent-only) with uniform RngBranchResolver; initialMove=TACKLE. HP 11–15.
- **Upstream**: Same moves, same RAND initial state. GOOP adds Slimed card to discard mid-combat. `MoveRepeatType.CannotRepeat` on both branches.
- **Match**: PARTIAL
- **Infrastructure gaps**:
  1. StatusCardIntent: Slimed card addition is intent-only in Q1; engine does not inject cards mid-combat at Phase-1.
  2. CannotRepeat: Q1 uses uniform weight without CannotRepeat; empirical distribution differs from turn 2 onward. Initial-state probe unaffected.

### 6. TwigSlimeS
- **Q1**: TACKLE (Attack 4, self-loop). HP 7–11. initialMove=TACKLE.
- **Upstream**: Identical single-state self-loop. HP 7–11 A0.
- **Match**: FULL

### 7. LeafSlimeM
- **Q1**: STICKY_SHOT (Status 2 Slimed intent-only) → CLUMP_SHOT (Attack 8) → STICKY_SHOT (alternating). HP 32–35. initialMove=STICKY_SHOT.
- **Upstream**: Same structure. Slimed card adds mid-combat. HP 32–35 A0. initialMove=STICKY_SHOT.
- **Match**: FULL (with same Phase-1 StatusCardIntent deferral caveat as LeafSlimeS — uniformly documented; not a new gap).

### 8. TwigSlimeM
- **Q1**: STICKY_SHOT (Status 1 Slimed intent-only) + POKEY_POUNCE (Attack 11) with RngBranchResolver weight 2:1; initialMove=STICKY_SHOT. HP 26–28.
- **Upstream**: Same moves. RAND with weight-2 POKEY_POUNCE + `MoveRepeatType.CannotRepeat` on STICKY_SHOT. initialMove=STICKY_SHOT.
- **Match**: PARTIAL
- **Infrastructure gaps**:
  1. CannotRepeat on STICKY_SHOT: Q1 uses weight-1 approximation; acknowledged in class comment.
  2. StatusCardIntent for Slimed (same Phase-1 deferral as other slimes).

### 9. BowlbugRock
- **Q1**: Single-move ATTACK 7 self-loop. HP 45–48. No spawn powers.
- **Upstream**: HEADBUTT_MOVE (Attack 15 A0) → ConditionalBranchState("POST_HEADBUTT"): IsOffBalance → DIZZY_MOVE (StunIntent, recover), else → HEADBUTT (self-loop). `AfterAddedToRoom` applies ImbalancedPower(1). HP 45–48 A0.
- **Match**: STUB
- **Notes**: Q1 damage (7) ≠ upstream HEADBUTT (15). Entire branching mechanic and stun-recovery cycle absent. ImbalancedPower spawn missing. Q1 has no mechanism for a boolean flag (`IsOffBalance`) driving ConditionalBranchState transitions at combat runtime.

### 10. BowlbugNectar
- **Q1**: Single-move ATTACK 5 self-loop. HP 35–38. No spawn powers.
- **Upstream**: THRASH_MOVE (Attack 3) → BUFF_MOVE (Buff + Strength +15 A0 self) → THRASH2_MOVE (Attack 3, self-loop). HP 35–38 A0. No spawn powers.
- **Match**: PARTIAL
- **Notes**: Q1 has only one ATTACK move. Upstream has 3-move sequence where BUFF_MOVE applies Strength +15 — a significant combat power gain. Q1 damage (5) ≠ upstream THRASH (3). Missing rotation and Strength application. The 3-move sequential structure (THRASH → BUFF → THRASH2 self-loop) requires adding 2 moves and a Strength spawn.

### 11. BowlbugSilk
- **Q1**: Single-move ATTACK 4 self-loop. HP 40–43. No spawn powers.
- **Upstream**: THRASH_MOVE (MultiAttack 4×2, initialMove) → TOXIC_SPIT_MOVE (DebuffIntent + Weak 1 to player) → THRASH alternating. HP 40–43 A0. initialMove=TOXIC_SPIT.
- **Match**: STUB
- **Notes**: Q1 has 1 ATTACK move vs upstream's 2-move alternation. Initial move is TOXIC_SPIT (not THRASH) in upstream. Q1 ATTACK (4 single hit) vs upstream THRASH (4×2 multi-attack). TOXIC_SPIT Weak(1) to player absent. The Weak debuff infrastructure already exists in Q1 (GremlinMerc, Crusher). Fix requires adding THRASH_MOVE (MultiAttack) + TOXIC_SPIT_MOVE with Weak debuff + correct initialMove.

### 12. FuzzyWurmCrawler
- **Q1**: Single-move ATTACK 14 self-loop. HP 55–57.
- **Upstream**: FIRST_ACID_GOOP (Attack 4 A0) → INHALE (Buff + Strength +7 self) → ACID_GOOP (Attack 4 A0) → FIRST_ACID_GOOP loop (ACID_GOOP.FollowUpState = FIRST_ACID_GOOP). HP 55–57 A0. No spawn powers.
- **Match**: STUB
- **Notes**: Q1 ATTACK damage (14) ≠ upstream AcidGoopDamage (4 A0). Full 3-move rotation missing. INHALE applies Strength +7 — major combat divergence. This is the highest-priority self-contained STUB (no new infrastructure beyond existing power application patterns). Blocks FuzzyWurmCrawlerSolo mid-combat coverage.

### 13. FossilStalker
- **Q1**: TACKLE (Attack 9) + LATCH (Attack 12) + LASH (MultiAttack 3×2) with uniform RngBranchResolver; initialMove=LATCH. SpawnPower SuckPower(3). HP 51–53.
- **Upstream**: Same 3 moves. `RandomBranchState` with `AddBranch(state, 2)` (weight-2 cooldown per branch). SuckPower(3) spawn. HP 51–53 A0. TACKLE_MOVE applies FrailPower(1) to player after attack.
- **Match**: PARTIAL
- **Infrastructure gaps**:
  1. TACKLE missing Frail(1) debuff to player — Q1 TACKLE is pure Attack; needs `AppliesPowers: [MonsterIntentPower(PowerIds.Frail, 1, PowerTarget.Player)]`.
  2. CannotRepeat deviation: Q1 uses uniform weight-1 across all 3 branches; upstream uses weight-2 cooldown. Empirical distribution of TACKLE/LATCH/LASH differs.

### 14. FrogKnight
- **Q1**: Single-move ATTACK 15 self-loop. HP 191. No spawn powers.
- **Upstream**: 4-move rotation with ConditionalBranchState ("HALF_HEALTH"): initialMove=TONGUE_LASH (Attack 13 + Frail 2 to player) → STRIKE_DOWN_EVIL (Attack 21) → FOR_THE_QUEEN (Buff + Strength +5 self) → HALF_HEALTH branch: HasBeetleCharged OR HP ≥ MaxHp/2 → TONGUE_LASH, else → BEETLE_CHARGE (Attack 35, sets HasBeetleCharged). `AfterAddedToRoom` applies PlatingPower(15 A0). HP 191 A0.
- **Match**: STUB
- **Notes**: Q1 ATTACK damage (15) ≠ any upstream move. Full 4-move rotation absent. ConditionalBranchState gated on `HasBeetleCharged` (boolean set by monster's own move) — more complex than HP-threshold because it tracks a move-execution side-effect. PlatingPower(15) spawn missing. Frail(2) on TONGUE_LASH and Strength(+5) on FOR_THE_QUEEN both absent. Most complex STUB to implement.

### 15. LagavulinMatriarch
- **Q1**: SLEEP (Buff, HpThresholdResolver at 50% HP) → SLASH (Attack 19) → DISEMBOWEL (MultiAttack 9×2) → SLASH2 (Attack 12) → SOUL_SIPHON (Debuff + Strength +2 self + StrengthDown 2 player + DexterityLoss 2 player) → SLASH loop. SpawnPower Plated(12). HP 222. initialMove=SLEEP.
- **Upstream**: Same move ids. Wake condition = `HasPower<AsleepPower>()` via ConditionalBranchState ("SLEEP_BRANCH"), not HP-threshold. `AfterAddedToRoom` applies PlatingPower(12) + AsleepPower(3). SLASH2Move gains block after attack (`CreatureCmd.GainBlock(self, Slash2Block=12)`). SOUL_SIPHON applies `StrengthPower(targets, -2m)` + `DexterityPower(targets, -2m)` + `StrengthPower(self, +2m)`.
- **Match**: PARTIAL
- **Infrastructure gaps**:
  1. AsleepPower replaced by HpThresholdResolver: documented deviation. Wake fires at 50% HP rather than when AsleepPower.stacks reaches 0. Functional divergence in edge cases (e.g., player reduces HP below 50% in one hit vs chipping down while asleep).
  2. SLASH2 missing `SelfBlockGain: 12` — upstream calls `CreatureCmd.GainBlock(self, 12)` after the attack; Q1 SLASH2 has no `SelfBlockGain` property set.
  3. SOUL_SIPHON: Q1 uses `PowerIds.StrengthDown` and `PowerIds.DexterityLoss`; upstream uses negative-stack `StrengthPower` and `DexterityPower`. Equivalent if Q1 power resolution treats negative stacks as StrengthDown/DexterityLoss; otherwise a behavioral difference.
  4. AsleepPower(3) not spawned (intentionally deferred along with the ConditionalBranchState wake gate).

### 16. HauntedShip (reference STUB from wave-47b/B)
- **Q1**: Single-move ATTACK 12 self-loop. HP 63. No spawn powers.
- **Upstream**: 4-move RandomBranchState with round-parity weight function. initialMove=HAUNT_MOVE (DebuffIntent + StatusIntent Dazed 5): applies Weak 3 to player + adds 5 Dazed cards to discard. RAND then picks from RAMMING_SPEED (Attack 10 A0), SWIPE (Attack 13 A0), STOMP (MultiAttack 4×3 A0) on odd rounds (weight=1); all three weight=0 on even rounds (HAUNT_MOVE repeats). HP 63 A0.
- **Match**: STUB
- **Notes**: Q1 ATTACK damage (12) ≠ any upstream move. Round-parity weight lambda not representable with current `RngBranchResolver` (requires round-number access). Weak power + Dazed card injection both absent. 4-move structure entirely absent.

### 17. LivingFog
- **Q1**: Single-move ATTACK 14 self-loop. HP 80. No spawn powers.
- **Upstream**: 3-move sequential rotation: ADVANCED_GAS_MOVE (Attack 8 A0 + CardDebuffIntent + SmoggyPower(1) to player) → BLOAT_MOVE (Attack 5 A0 + SummonIntent + spawns 1 GasBomb per `_bloatAmount` via `CreatureCmd.Add<GasBomb>`) → SUPER_GAS_BLAST_MOVE (Attack 8 A0) → BLOAT loop. HP 80 A0.
- **Match**: STUB
- **Notes**: Q1 ATTACK damage (14) ≠ any upstream move. Full 3-move rotation absent. SmoggyPower(1) debuff (CardDebuffIntent) absent. GasBomb mid-combat spawn requires dynamic `CreatureCmd.Add<GasBomb>` infrastructure — most complex infrastructure gap in the catalog. `_bloatAmount` increments each BLOAT cycle (starts 1); Q1 lacks stateful monster-field support.

### 18. GremlinMerc
- **Q1**: GIMME (MultiAttack 7×2) → DOUBLE_SMASH (MultiAttack 6×2 + Weak 2 player) → HEHE (Attack 8 + Strength +2 self) → GIMME loop. SpawnPowers SurprisePower(1) + ThieveryPower(20). HP 47–49. initialMove=GIMME.
- **Upstream**: Identical A0. ThieveryPower steal-logic wired at Phase-1 (gold-tracking deferred per ADR-030).
- **Match**: FULL

### 19. FatGremlin
- **Q1**: SPAWNED_MOVE (StunIntent wake-up) → FLEE_MOVE (IntentKind.Unknown/Escape self-loop). HP 13–17.
- **Upstream**: Identical. EscapeIntent mapped to IntentKind.Unknown (documented Phase-1 deferral).
- **Match**: FULL

### 20. SneakyGremlin
- **Q1**: SPAWNED_MOVE (StunIntent) → TACKLE_MOVE (Attack 9 self-loop). HP 10–14.
- **Upstream**: Identical A0.
- **Match**: FULL

### 21. Crusher
- **Q1**: THRASH (Attack 12) → ENLARGING_STRIKE (Attack 4) → BUG_STING (MultiAttack 6×2 + Weak 2 + Frail 2 player) → ADAPT (Buff + Strength +2 self) → GUARDED_STRIKE (Attack 12 + SelfBlockGain 18) → THRASH loop. SpawnPowers BackAttackLeftPower(1) + CrabRagePower(1) [fail-soft]. HP 209.
- **Upstream**: Identical rotation. All A0 damage values match. GUARDED_STRIKE gains 18 block via `CreatureCmd.GainBlock` (hardcoded; behaviorally equivalent to Q1's `SelfBlockGain: 18`). Spawn powers are real ids (fail-soft in Q1).
- **Match**: PARTIAL
- **Infrastructure gaps**:
  1. Spawn powers BackAttackLeft and CrabRage unknown in Q1 power catalog (fail-soft; documented). No mechanical impact until catalog expanded.
  2. ADAPT Strength: Q1=2, upstream A0=2 — match confirmed.

### 22. Rocket
- **Q1**: TARGETING_RETICLE (Attack 3) → PRECISION_BEAM (Attack 18) → CHARGE_UP (Buff + Strength +2 self) → LASER (Attack 31) → RECHARGE (Buff, self-loop) → TARGETING_RETICLE. SpawnPowers BackAttackRight, CrabRage, Surrounded [fail-soft]. HP 199.
- **Upstream**: Identical rotation and A0 damage values. RECHARGE uses `SleepIntent` not `BuffIntent`. SurroundedPower applied to opponents (not self) in upstream.
- **Match**: PARTIAL
- **Infrastructure gaps**:
  1. RECHARGE uses `Intent.Buff()` in Q1 instead of SleepIntent. Q1 lacks `IntentKind.Sleep`. Intent kind diverges in mid-combat probe display.
  2. SurroundedPower targeting: Q1 declares as self-spawn; upstream targets opponents. No mechanical impact until SurroundedPower implemented.

### 23. CeremonialBeast
- **Q1**: STAMP (Buff, PlowPower stub) → PLOW (Attack 18 + Strength +2 self, loop). Post-stun states present: BEAST_CRY (Debuff) → STOMP (Attack 15) → CRUSH (Attack 17 + Strength +3 self) → BEAST_CRY loop. HP 252. initialMove=STAMP.
- **Upstream**: Same pre-stun and post-stun moves. STUN_MOVE (StunIntent, `MustPerformOnceBeforeTransitioning=true`) is inserted between PLOW-loop and BEAST_CRY when PlowPower is removed. BEAST_CRY applies `RingingPower(1)` to player. CrushStrength=3 A0. PlowAmount=150 A0.
- **Match**: PARTIAL
- **Infrastructure gaps**:
  1. STUN_MOVE transition wiring: Q1 has the STUN_MOVE state in its move list but lacks the PlowPower-removal trigger that causes the state machine to jump to STUN_MOVE mid-PLOW-loop. Post-stun cycle is structurally present but unreachable.
  2. BEAST_CRY missing RingingPower(1) debuff to player — Q1 BEAST_CRY is `Intent.Debuff()` with no power application; needs `AppliesPowers: [MonsterIntentPower(PowerIds.Ringing, 1, PowerTarget.Player)]`.
  3. PlowPower: Q1 uses the power id as documentation-only (fail-soft); not in Q1 power catalog.

### 24. LouseProgenitor
- **Q1**: WEB_CANNON (Attack 9) → CURL_AND_GROW (Defend 14 + Strength +5 self) → POUNCE (Attack **16**) → WEB_CANNON loop. SpawnPower CurlUp(14). HP 134–136. initialMove=WEB_CANNON.
- **Upstream**: Same move rotation. WEB_CANNON applies FrailPower(2) to player after attack. PounceDamage A0 = **14** (`GetValueIfAscension(DeadlyEnemies, 16, 14)`). WebDamage A0 = 9 (match). CurlBlock A0 = 14 (match). HP 134–136 A0 (match).
- **Match**: PARTIAL
- **Infrastructure gaps**:
  1. **PounceDamage value error**: Q1 `PounceDamage = 16` but upstream A0 = 14. Q1 is using the DeadlyEnemies (Ascension) value. POUNCE deals 2 extra damage per turn vs upstream — active behavioral divergence.
  2. **WEB_CANNON missing Frail(2)**: Q1 WEB_CANNON is pure `Intent.Attack(9)`; upstream applies `FrailPower(2)` to player after the attack. Missing power application in WEB_CANNON.

### 25. Nibbit
- **Q1**: BUTT (Attack 12) → SLICE (Attack 6 + SelfBlockGain 5) → HISS (Buff + Strength +2 self) → BUTT loop. HP 42–46. initialMove=BUTT.
- **Upstream**: Same moves and A0 values. ConditionalBranchState ("INIT_MOVE") for per-slot initial-move override (NibbitsNormal: front=SLICE, back=HISS; NibbitsWeak: alone=BUTT). Q1 handles per-slot override at encounter level via `GenerateMonstersWithMoves`.
- **Match**: FULL

---

## §Infrastructure dependency summary

| Pattern | Status in Q1 | Required by N monsters | Monsters |
|---|---|---|---|
| RandomBranchState with round-parity weight lambda | Absent | 1 | HauntedShip |
| ConditionalBranchState (boolean flag gate, not HP-threshold) | Absent as standalone; LagavulinMatriarch uses HpThresholdResolver proxy | 2 | BowlbugRock (IsOffBalance), FrogKnight (HasBeetleCharged) |
| ConditionalBranchState (per-slot initial-move) | Absent; handled at encounter level for Nibbit/Exoskeleton | 1 (direct substrate gap) | Exoskeleton (low-impact; current encounter uses only slot "first") |
| StunIntent + in-combat stun cycle (IsOffBalance) | Partial (FatGremlin/SneakyGremlin use IntentKind.Stun for wake-up); BowlbugRock needs full stun-to-DIZZY transition | 1 | BowlbugRock |
| StatusCardIntent (add Slimed/Dazed cards to discard mid-combat) | Absent; all instances intent-only at Phase-1 | 4 | LeafSlimeS (Slimed 1), TwigSlimeM (Slimed 1), HauntedShip (Dazed 5), LivingFog (CardDebuffIntent/SmoggyPower) |
| SummonIntent (mid-combat monster spawn `CreatureCmd.Add<T>`) | Absent | 1 | LivingFog (GasBomb spawn) |
| DebuffPower on Attack moves (Frail or Weak inline) | Present for GremlinMerc, Crusher, Exoskeleton; absent as noted | 4 | FossilStalker (Frail 1 on TACKLE), LouseProgenitor (Frail 2 on WEB_CANNON), FrogKnight (Frail 2 on TONGUE_LASH), BowlbugSilk (Weak 1 on TOXIC_SPIT) |
| ArtifactPower spawn | Absent (Chomper-specific) | 1 | Chomper |
| ImbalancedPower spawn | Absent | 1 | BowlbugRock |
| PlatingPower spawn (active/known id) | Absent; fail-soft for LagavulinMatriarch; must be known id for FrogKnight | 2 | FrogKnight (active PlatingPower), LagavulinMatriarch (documented fail-soft) |
| SleepIntent | Absent; Rocket RECHARGE mapped to Intent.Buff | 1 | Rocket |
| CannotRepeat suppression in RngBranchResolver | Absent; all Q1 random branches use equal/weighted without repeat-tracking | 5 | Exoskeleton RAND, LeafSlimeS, TwigSlimeM, FossilStalker, HauntedShip RAND |
| Multi-move rotation (moves entirely missing) | N/A (infrastructure present; moves not coded) | 2 | BowlbugNectar (BUFF_MOVE absent), FuzzyWurmCrawler (INHALE absent) |

---

## §Re-surface trigger assessment

- **>5 STUB findings**: YES — 6 STUBs found (BowlbugRock, BowlbugSilk, FuzzyWurmCrawler, FrogKnight, HauntedShip, LivingFog) vs project-lead's candidate-4 list. **R15 trigger FIRED.** Surface to project-lead for Phase-1.5 scope reassessment.
- **≥3 stubs share same infrastructure**: YES — **DebuffPower inline on Attack moves** needed by 4 monsters (FossilStalker PARTIAL + LouseProgenitor PARTIAL + FrogKnight STUB + BowlbugSilk STUB). **Batch-infrastructure-wave candidate FIRED.** Additionally, **StatusCardIntent** needed by 4 monsters but those are Phase-1 documented deferrals; ConditionalBranchState needed by 2 STUBs.

---

## §Recommendations

### Top-priority STUBs to fix (ordered by fix complexity and encounter frequency)

1. **FuzzyWurmCrawler** — 3-move rotation (FIRST_ACID_GOOP → INHALE+Strength7 → ACID_GOOP loop). Self-contained; uses only existing Q1 infrastructure (MultiAttack, Buff, Strength power application). Blocks FuzzyWurmCrawlerSolo mid-combat coverage. Fix complexity: **LOW**.

2. **BowlbugSilk** — 2-move alternation with TOXIC_SPIT Weak debuff; correct initial move is TOXIC_SPIT; THRASH is MultiAttack 4×2. Weak debuff already in Q1 (GremlinMerc precedent). Fix complexity: **LOW**.

3. **BowlbugRock** — ConditionalBranchState (IsOffBalance gate) + DIZZY_MOVE StunIntent + ImbalancedPower spawn + correct HEADBUTT damage. New infrastructure needed for non-HP conditional branching. Fix complexity: **MEDIUM**.

4. **HauntedShip** — 4-move RandomBranchState with round-parity weight function + Weak(3) power + Dazed card injection. Round-parity lambda requires RngBranchResolver extension (round-number access). StatusCardIntent deferred at Phase-1. Fix complexity: **HIGH** (Weak power available; Dazed card injection deferred → combat still partially fixable without card injection).

5. **FrogKnight** — 4-move ConditionalBranchState gated on `HasBeetleCharged` (boolean set by move execution) + PlatingPower spawn + Frail(2) + Strength(+5). ConditionalBranch on move-execution state is new infrastructure. Fix complexity: **HIGH**.

6. **LivingFog** — 3-move rotation + SmoggyPower + GasBomb mid-combat spawn. Requires dynamic `CreatureCmd.Add<GasBomb>` infrastructure and stateful `_bloatAmount` counter. Fix complexity: **VERY HIGH**. May require dedicated infrastructure wave before LivingFogSolo is addressable.

### Batch infrastructure waves (candidates)

- **Batch A — Inline DebuffPower on Attack moves** (4 monsters, file-disjoint edits): Add `AppliesPowers` entries to: FossilStalker TACKLE (Frail 1), LouseProgenitor WEB_CANNON (Frail 2), BowlbugSilk TOXIC_SPIT (Weak 1), plus fix LouseProgenitor PounceDamage value (14 not 16). All use existing Q1 power infrastructure. Single wave, 3 files (Phase1Monsters.cs sections). **Highest ROI batch** — fixes 2 PARTIAL and contributes to 1 STUB.

- **Batch B — BowlbugNectar + FuzzyWurmCrawler rotation fixes** (2 STUBs/PARTIALs, same file): Both live in Phase1Monsters.cs; file-disjoint within that file. No new infrastructure needed. Small wave.

- **Batch C — CannotRepeat suppression** (if RngBranchResolver extended): Would fix LeafSlimeS, TwigSlimeM, FossilStalker, Exoskeleton RAND in one infrastructure wave. Lower priority since initial-state probes unaffected.

### Priority order for substrate-fix waves

1. Batch A (inline debuffs + LouseProgenitor damage correction) — low risk, high correctness gain.
2. Batch B (FuzzyWurmCrawler + BowlbugNectar rotation) — self-contained, unblocks FuzzyWurmCrawlerSolo.
3. BowlbugSilk standalone (simple alternation + Weak) — very low risk.
4. BowlbugRock (ConditionalBranchState infrastructure wave).
5. HauntedShip (RandomBranchState round-parity wave; partial fix possible without StatusCardIntent).
6. FrogKnight (ConditionalBranchState on move-state flag; more complex than BowlbugRock).
7. LivingFog (mid-combat spawn infrastructure; dedicated infrastructure wave prerequisite).

---

## §Out-of-scope (Phase1Monsters.cs classes not used by current AllKnownIds)

Listed for completeness; NOT deep-audited against upstream in this wave. All observed in Q1 as single-move ATTACK stubs.

| Monster | Q1 class | Status note |
|---|---|---|
| BowlbugEgg | `Phase1Monsters.cs:1085` | Single-move BUFF stub; not spawned by current AllKnownIds encounters |
| JawWorm | `Phase1Monsters.cs:398` | Single-move ATTACK stub; deprecated STS1-era monster |
| RedLouse | `Phase1Monsters.cs:415` | Single-move ATTACK stub; deprecated |
| GreenLouse | `Phase1Monsters.cs:432` | Single-move ATTACK stub; deprecated |
| AcidSlimeS | `Phase1Monsters.cs:662` | Single-move ATTACK stub; deprecated (replaced by LeafSlimeS/TwigSlimeS) |
| AcidSlimeM | `Phase1Monsters.cs:679` | Single-move ATTACK stub; deprecated |
| AcidSlimeL | `Phase1Monsters.cs:696` | Single-move ATTACK stub; retained as non-current-pool monster |
| SpikeSlimeL | `Phase1Monsters.cs:718` | Single-move ATTACK stub; retained |
| FungalBoss | `Phase1Monsters.cs:735` | Single-move ATTACK stub; prep for future encounters |
| SnakePlant | `Phase1Monsters.cs:752` | Single-move ATTACK stub |
| Sentry | `Phase1Monsters.cs:769` | Single-move ATTACK stub |
| CenturyGuard | `Phase1Monsters.cs:879` | Single-move ATTACK stub |
| SilverMage | `Phase1Monsters.cs:896` | Single-move ATTACK stub |

Project-lead decision needed: whether to audit these 13 against upstream for future encounter ports or treat as backlog until encounter use is planned.
