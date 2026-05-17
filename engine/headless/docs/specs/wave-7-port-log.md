# Wave 7 / B.2-β — Combat-Engine Bucket Port Log
# v0.103.2 → v0.105.1

**Date:** 2026-05-17
**Stream:** B.2-β (single stream, Wave 7)
**Engineer:** Sonnet 4.6 subagent
**Base SHA:** 7f0d8bb6a7260e2d32e25b2f2144909e7714c6e1
**ADR refs:** ADR-026 (pin semantics), ADR-027 (caps-as-floors), Q1-ADR-009 (multiplayer stripped)

## Summary

All 37 upstream combat-engine PORT rows are SKIP-NO-Q1.
No source edits made; no test edits made; no LOC delta.

**Rationale:** Q1 is a headless, single-player, immutable-record engine with no Godot
scene-tree dependency. Every row in this bucket is either:

1. A Godot-coupled mutable class (CombatManager, CombatStateTracker, Commands/*Cmd) with
   no headless analogue — Q1 has CombatEngine (static), ICombatContext, ActionQueue instead.
2. A multiplayer-protocol class (ActionQueueSet, ActionQueueSynchronizer, HookPlayerChoiceContext,
   NetCombatCardDb, PlayerChoiceResult) — Q1-ADR-009 strips multiplayer entirely.
3. A new Godot interface or null-object (ICombatState, NullCombatState, PlayerTurnPhase) whose
   role is already covered by Q1's ICombatContext / CombatPhase — adding them would be
   architectural noise with no consumer in the headless substrate.
4. A Godot combat-history tracker (CombatHistory, CombatHistoryEntry, CardGeneratedEntry) —
   Q1 has no audit-history tracking; out of scope for Phase 1.

## Re-surface triggers hit

- **>15 SKIP-NO-Q1 rows in Commands/ subtree (17 rows):** Recommend classifying the entire
  `src/Core/Commands/` subtree as SKIP-WHOLE-SUBTREE in port-decisions tooling. The
  Commands/ bucket is Godot scene-tree command infrastructure; Q1 has no analogue and will
  never carry this directory tree.
- **All 37 rows SKIP:** Zero code change for this wave. Re-surface to quantum-lead to
  determine whether Wave 7 should be closed as a doc-only wave or collapsed into Wave 8
  (Model-bases), since no source delta lands.

## Per-row breakdown

### Combat/ (12 rows)

| Upstream path | Status | git_status | Reason |
|---|---|---|---|
| `src/Core/Combat/CombatManager.cs` | SKIP-NO-Q1 | M | Godot singleton; Q1 uses CombatEngine (static stateless). Diff removes `IsPlayPhase`, adds `SetPhaseForAllPlayers`, wires `PlayerTurnPhase` — all Godot/async/scene-tree. No Q1 analogue. |
| `src/Core/Combat/CombatState.cs` | SKIP-NO-Q1 | M | Upstream's mutable Godot class refactored to implement `ICombatState`, add `BadgeModel` ctor param, make `EscapedCreatures` readonly. Q1's `CombatState` is an immutable record with completely different design — the two share only a name. No behavioral overlap applicable. |
| `src/Core/Combat/CombatStateTracker.cs` | SKIP-NO-Q1 | M | Godot SceneTree signal-driven tracker (Subscribe/Unsubscribe on PlayerCombatState.PlayerTurnPhaseChanged). No Q1 analogue — Q1 has no mutable reactive state tracking. |
| `src/Core/Combat/History/CombatHistory.cs` | SKIP-NO-Q1 | M | Audit-history class; all methods changed from `CombatState` → `ICombatState` param; `CardGenerated` sig changes `bool generatedByPlayer` → `Player? creator`. Q1 has no combat history. |
| `src/Core/Combat/History/CombatHistoryEntry.cs` | SKIP-NO-Q1 | M | `HappenedThisTurn(CombatState?)` → `HappenedThisTurn(ICombatState?)`. No Q1 analogue. |
| `src/Core/Combat/History/Entries/CardGeneratedEntry.cs` | SKIP-NO-Q1 | M | `GeneratedByPlayer bool` → `Creator Player?`. No Q1 analogue. |
| `src/Core/Combat/ICombatState.cs` | SKIP-NO-Q1 | A (new file) | New upstream interface abstracting CombatState. Q1 already has ICombatContext for this role — a different, broader abstraction covering the mutation surface too. Adding ICombatState would create a redundant interface with no consumer. |
| `src/Core/Combat/ICombatState.cs.uid` | SKIP-NO-Q1 | A (new file) | Godot .uid sidecar. Q1 has no .uid files. |
| `src/Core/Combat/NullCombatState.cs` | SKIP-NO-Q1 | A (new file) | Null-object implementation of ICombatState. No headless use case — ICombatContext is never null in Q1; CombatEngine methods take explicit inputs. |
| `src/Core/Combat/NullCombatState.cs.uid` | SKIP-NO-Q1 | A (new file) | Godot .uid sidecar. |
| `src/Core/Combat/PlayerTurnPhase.cs` | SKIP-NO-Q1 | A (new file) | Enum: None/Start/AutoPrePlay/Play/AutoPostPlay/End. Q1's CombatPhase already covers turn lifecycle at the coarse level (PlayerTurnStart/PlayerActing/PlayerTurnEnd); the sub-phase granularity drives Godot UI/animation sequencing that Q1 does not render. No Q1 consumer. |
| `src/Core/Combat/PlayerTurnPhase.cs.uid` | SKIP-NO-Q1 | A (new file) | Godot .uid sidecar. |

**Sub-bucket: Combat/ — PORT=0 SKIP=12 STUB=0**

### Commands/ (17 rows) — SKIP-WHOLE-SUBTREE

All 17 rows SKIP-NO-Q1. The `src/Core/Commands/` subtree is Godot scene-tree command
infrastructure (scene-node I/O, VFX, SFX, UI screens, multiplayer synchronization).
Q1 has no Commands/ directory and will never port this subtree.

Recommend: reclassify the entire `Commands/` bucket as `SURFACE-NO-ACTION` in
`03-v0.103.2-to-v0.105.1-port-decisions.json` via a tooling pass (similar to
the `scenes-gameplay` false-positive filter discussed in the quantum-lead plan).

| Upstream path | Status | git_status | Reason |
|---|---|---|---|
| `src/Core/Commands/Builders/AttackCommand.cs` | SKIP-NO-Q1 | M | Scene-coupled: VFX node wiring (`NCombatRoom`, `GetVfxContainer()`), async Godot hooks, `CombatState→ICombatState` refactor, `_results` type change to `List<List<DamageResult>>`. Q1 handles attack in CombatEngine.cs directly. |
| `src/Core/Commands/Builders/AttackContext.cs` | SKIP-NO-Q1 | M | Godot async attack context; `AfterAttack` sig adds `PlayerChoiceContext` param. No Q1 analogue. |
| `src/Core/Commands/CardCmd.cs` | SKIP-NO-Q1 | M | Godot card-play command; `CombatState→ICombatState`, VFX node changes, `CardGenerated` sig change. Q1 handles card play in CombatEngine.PlayerPlayCard. |
| `src/Core/Commands/CardPileCmd.cs` | SKIP-NO-Q1 | M | `ICombatState` refactor, tween/animation changes, `AddGeneratedCardToCombat` sig `bool addedByPlayer→Player? creator`. Q1 handles pile mutations via CombatContext. |
| `src/Core/Commands/CardSelectCmd.cs` | SKIP-NO-Q1 | M | Godot UI card selection screen. No Q1 analogue (Q1 selections are via LegalActions). |
| `src/Core/Commands/CreatureCmd.cs` | SKIP-NO-Q1 | M | `ICombatState` refactor, VFX node changes, `DamageDealt` tracking added, `IsLiveCombat()` guard. Q1 handles creature damage in CombatEngine. |
| `src/Core/Commands/ForgeCmd.cs` | SKIP-NO-Q1 | M | Forge (card upgrade) UI command. No Q1 Phase-1 analogue. |
| `src/Core/Commands/OrbCmd.cs` | SKIP-NO-Q1 | M | Orb VFX/channeling command. No Q1 Phase-1 analogue. |
| `src/Core/Commands/OstyCmd.cs` | SKIP-NO-Q1 | M | Osty summon command; `ICombatState`, `PowerCmd.Apply` sig adds `PlayerChoiceContext`. No Q1 Phase-1 analogue. |
| `src/Core/Commands/PlayerCmd.cs` | SKIP-NO-Q1 | M | Player energy gain command; `ICombatState` refactor. Q1 handles energy in CombatContext. |
| `src/Core/Commands/PowerCmd.cs` | SKIP-NO-Q1 | M | Power application command; `Apply` sig adds `PlayerChoiceContext` param; `FindExistingInstanceForStacking` introduced. Q1 handles power application in CombatContext.ApplyPower. |
| `src/Core/Commands/RelicSelectCmd.cs` | SKIP-NO-Q1 | M | Godot UI relic selection. No Q1 Phase-1 analogue. |
| `src/Core/Commands/RewardsCmd.cs` | SKIP-NO-Q1 | M | Godot rewards screen. No Q1 Phase-1 analogue. |
| `src/Core/Commands/SfxCmd.cs` | SKIP-NO-Q1 | M | Sound FX command (Godot audio). No Q1 analogue (headless). |
| `src/Core/Commands/TalkCmd.cs` | SKIP-NO-Q1 | M | Dialogue/talk command (Godot UI). No Q1 analogue. |
| `src/Core/Commands/ThinkCmd.cs` | SKIP-NO-Q1 | M | Thought bubble VFX command (Godot). No Q1 analogue. |
| `src/Core/Commands/VfxCmd.cs` | SKIP-NO-Q1 | M | VFX command (Godot). No Q1 analogue (headless). |

**Sub-bucket: Commands/ — PORT=0 SKIP=17 STUB=0**

### GameActions/ (7 rows)

| Upstream path | Status | git_status | Reason |
|---|---|---|---|
| `src/Core/GameActions/ActionExecutor.cs` | SKIP-NO-Q1 | M | Godot async action executor; diff is `ToSignal(GetTree()…)→AwaitProcessFrame()` — pure Godot extension cosmetic. Q1 has ActionQueue (sync FIFO). |
| `src/Core/GameActions/GenericHookGameAction.cs` | SKIP-NO-Q1 | M | Godot async game action; adds `debugArtificialDelayAfterTask` debug field and enriched ToString. Q1 has synchronous hook firing via HookRegistry. |
| `src/Core/GameActions/Multiplayer/ActionQueueSet.cs` | SKIP-NO-Q1 | M | Multiplayer per-player queue; adds try/catch + SentryService.CaptureException on ActionEnqueued. Q1-ADR-009: multiplayer stripped. |
| `src/Core/GameActions/Multiplayer/ActionQueueSynchronizer.cs` | SKIP-NO-Q1 | M | Multiplayer synchronizer; diff is "client"→"clients" in log strings (cosmetic). Q1-ADR-009. |
| `src/Core/GameActions/Multiplayer/HookPlayerChoiceContext.cs` | SKIP-NO-Q1 | M | `CombatState→ICombatState`; adds `WaitForCompletion()` method. Q1-ADR-009: no player choice protocol. |
| `src/Core/GameActions/Multiplayer/NetCombatCardDb.cs` | SKIP-NO-Q1 | M | Multiplayer card DB; adds null-owner guard + debug log. Q1-ADR-009. |
| `src/Core/GameActions/PlayerChoiceResult.cs` | SKIP-NO-Q1 | M | `FromIndex(int)→FromIndex(int?)` null-safety; adds `AsIndexOrNull()`. Q1-ADR-009: no player choice protocol. |

**Sub-bucket: GameActions/ — PORT=0 SKIP=7 STUB=0**

### Hooks/ (1 row)

| Upstream path | Status | git_status | Reason |
|---|---|---|---|
| `src/Core/Hooks/Hook.cs` | SKIP-NO-Q1 | M | Static hook coordinator over AbstractModel (Godot scene-tree mutable). Key changes: `AfterAttack` adds `PlayerChoiceContext` param; `AfterCardChangedPiles` uses `ICombatState?`; `CardGenerated`/`AfterCardGeneratedForCombat` change `bool addedByPlayer→Player? creator`. Q1 has HookRegistry (sync delegates, no AbstractModel). Behavioral semantics of `creator` vs `addedByPlayer` relevant only if Q1 ever implements AfterCardGenerated — not in Phase 1 scope. |

**Sub-bucket: Hooks/ — PORT=0 SKIP=1 STUB=0**

## Overall totals

| Category | Count | / 37 |
|---|---|---|
| PORT | 0 | 0 / 37 |
| SKIP-NO-Q1 | 37 | 37 / 37 |
| STUB | 0 | 0 / 37 |

## Upstream behavioral changes to track for future waves

The following v0.103.2→v0.105.1 behavioral changes in the combat-engine bucket
are NOT ported to Q1 now (SKIP) but represent semantic changes that downstream
waves should be aware of:

1. **`CardGenerated`: `bool generatedByPlayer` → `Player? creator`** (CombatHistory.cs,
   CardGeneratedEntry.cs, CardPileCmd.cs, CardCmd.cs, Hook.cs). If Q1 ever implements
   `AfterCardGeneratedForCombat` hook firing, the payload should carry `Player? creator`
   not a bool. Note for Wave 9+ (Cards bucket).

2. **`AfterAttack` gains `PlayerChoiceContext`** (Hook.cs, AttackCommand.cs, AttackContext.cs).
   If Q1 ever adds `AfterAttack` hook firing in CombatEngine, the signature should include
   a choice-context param. Note for future combat-engine expansions.

3. **`PlayerChoiceResult.FromIndex` becomes null-safe** (GameActions/PlayerChoiceResult.cs).
   No Q1 impact (no player choice protocol), but may matter if Q2 Oracle ever calls
   into upstream's FromIndex path.

4. **`AttackCommand._results` changes from `List<DamageResult>` to `List<List<DamageResult>>`**
   (per-hit result grouping). Q1's damage tracking in CombatEngine is per-hit flat;
   the grouped structure is for Godot VFX node management and has no headless analog.

5. **`CreatureCmd.Damage` adds `DamageDealt` field increment** to `Player.ExtraFields`.
   Q1's `Creature` record doesn't have `ExtraFields`. If Q1 Phase-2 needs cumulative
   damage-dealt stats, this upstream field is the precedent.

## Verification results

No source files were modified. Build and test results are unchanged from HEAD:

- `dotnet build sts2-headless.sln`: pending (see commit)
- `BitIdenticalRoundtripTests`: pending (no source change — expected unchanged)
- `probe-upstream-initial-state`: pending (no source change — expected 140 PASS / 20 SKIP)
- `DllSignatureGate`: pending (no source change)

## LOC delta

- Source: 0 lines changed (no source edits)
- Tests: 0 lines changed (no test edits)
- This port-log doc: ~170 lines (new file, owned by F7)
