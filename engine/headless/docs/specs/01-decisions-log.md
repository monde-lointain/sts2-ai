# 01 — Architectural Decision Log: Q1 Internals

ADRs that shape Q1's internal architecture. Each entry: Title, Status, Context, Decision, Consequences (negatives first, then positives).

These ADRs are **subordinate** to pipeline-level ADRs at `~/development/projects/cpp/sts2-ai/docs/specs/01-decisions-log.md`. Where a pipeline ADR constrains Q1 internals, this log cross-references rather than restates.

| # | Title | Status |
|---|---|---|
| Q1-ADR-001 | Hexagonal Modular Monolith over Layered | Accepted |
| Q1-ADR-002 | Domain Core Sub-Decomposed into Combat/Run/Content-Behaviors/Action-Queue | Accepted |
| Q1-ADR-003 | M5 Owns RNG State Schema; M1 Serializes via Generic Codec Interface | Accepted |
| Q1-ADR-004 | Mod-Layer Discipline for Engine Strip (M8) | Accepted |
| Q1-ADR-005 | Hook Protocol Schema Co-Versioned with State Schema | Accepted |
| Q1-ADR-006 | Deterministic Effect Ordering is a Spec, Not Implementation-Defined | Accepted |
| Q1-ADR-007 | Determinism Probe is a CI Tool, Not a Runtime Module | Accepted |
| Q1-ADR-008 | Single-Threaded Decision Path; Concurrency by Process Replication | Accepted |
| Q1-ADR-009 | Strip Multiplayer Entirely | Accepted |
| Q1-ADR-010 | Soft Reuse of `Core/AutoSlay/AutoSlayer.cs` Patterns Where Aligned | Accepted |
| Q1-ADR-011 | Parallel Sub-Streams Must Partition by File, Not Code Region | Accepted |
| Q1-ADR-012 | Schema Lock for `contracts/schemas/game-simulator/` v0.1 (combat-only) | Accepted |
| Q1-ADR-013 | Caller-Callee Contract Gap Mitigation (Q1-ADR-011 follow-up) | Accepted |

---

## Q1-ADR-001 — Hexagonal Modular Monolith over Layered

**Status:** Accepted.

**Context.** Q1 is one .NET process (pipeline ADR-002, ADR-005). Within that process, two architectural styles compete: hexagonal (ports + adapters with the domain at the center) versus layered (presentation/application/domain/infrastructure). Determinism is Q1's rank-1 characteristic; the failure mode of a determinism leak (e.g., `DateTime.Now` reaching combat) is *silent* corruption of training data, weeks before discovery. Structural enforcement of purity is worth boilerplate.

**Decision.** Hexagonal modular monolith. Domain Core (M6a–M6d) at the center; M5 and M7 as infra primitives the core depends on; M1, M2, M3, M4 as schema-owning adapters at the edges; M8 as the structural Godot-surface replacement; M9 as the composition root. Ports are interfaces defined by the core or its bounded contexts (`IActionProvider`, `IPersistenceCodec`, `IReplaySink`, `IClock`, `IRngSource`, `IContentProvider`). A namespace-level Roslyn analyzer rule forbids the Domain Core from referencing IO, network, or wall-clock APIs.

**Consequences.**

- *Negative:* interface boilerplate. Every port is at minimum one interface plus one production adapter. For ports with only one impl (e.g., `IContentProvider`) this is pure ceremony.
- *Negative:* over-abstraction risk. Junior engineers will create ports for things that should just be classes. Mitigation: code-review rule — "ports cross schema or determinism boundaries; everything else is internal."
- *Negative:* discipline-dependent purity. Nothing structurally prevents the Domain Core from `using System.IO`. Mitigation: namespace-banned-API analyzer; CI fails on violation.
- *Negative:* adapter-port version coupling. Changing a port signature ripples to every adapter. Mitigation: ports are private API; only the schemas (M1, M2, M3, M4) are versioned externally.
- *Negative:* composition root (M9) grows as adapters accumulate. Mitigation: keep M9 declarative — "construct, wire, start," not "decide."
- *Positive:* determinism leaks are caught at build time, not runtime.
- *Positive:* engine-strip (M8) is a port adapter, not scattered through layers — patch adaptability is structural.
- *Positive:* test doubles for adapters are trivial; unit-testing the domain is mechanical.
- *Positive:* if Megacrit ever ships an official headless API (pipeline ADR-013), it replaces M8 as a thin adapter without disturbing the core.

---

## Q1-ADR-002 — Domain Core Sub-Decomposed into Combat/Run/Content-Behaviors/Action-Queue

**Status:** Accepted.

**Context.** The "Domain Core" of Q1 covers ~80% of inherited code from upstream `src/Core/{Combat, Entities, Models, GameActions, Runs, Rooms, Commands, MonsterMoves, Hooks}`. Treating it as a single module would produce one ~2000-line spec that no one reads end-to-end. Pure technical-layer split (entities/services/repositories) creates artificial seams; cards play actions on creatures with powers, and these don't separate cleanly along technical lines.

**Decision.** Sub-decompose by *responsibility*: M6a Combat Domain (combat state machine, turn lifecycle), M6b Run Domain (acts/map/rooms/rewards/run-scope state), M6c Content Behaviors (polymorphic content code: card OnPlay, relic OnHook, power OnAttack, monster intents), M6d Action Queue & Hooks (serial event-loop, hook registry, deterministic ordering). M6c is called by both M6a and M6b through context interfaces. M6d is the single point of effect ordering.

**Consequences.**

- *Negative:* four module specs to maintain instead of one. Cross-module changes require touching multiple files.
- *Negative:* the M6a ↔ M6b ↔ M6c boundary is not perfectly clean. Some concepts (e.g., a card with map-level effects) cross all three. Each crossing needs a documented owner.
- *Negative:* M6d becomes load-bearing — every effect ordering question comes here. Bus factor risk.
- *Negative:* navigating the inherited `src/Core/` codebase requires understanding the four-way mapping. Onboarding cost.
- *Positive:* clearer responsibility boundaries; reviewers can hold a single module in head.
- *Positive:* M6a is testable without M6b initialized — supports `[Phase 1 scope]` (combat-only) cleanly.
- *Positive:* M6c's polymorphic content can be exercised through tightly-scoped unit tests against mocked combat/run contexts.

---

## Q1-ADR-003 — M5 Owns RNG State Schema; M1 Serializes via Generic Codec Interface

**Status:** Accepted.

**Context.** RNG state is part of the bit-identically-roundtripped game state, so M1 (State Codec) must serialize it. But the *structure* of RNG state (per-subsystem seed + counter discipline, RNG identity per `RunRngType`) is M5's concern. Two ways to draw the line: M5 exposes its internals to M1 (M1 knows about RNG layout), or M5 exposes a generic `ISerializable`-style interface and M1 treats RNG state as opaque bytes with versioning.

**Decision.** M5 exposes a generic codec interface (`IRngStateSerializer` returning a versioned byte blob). M1 treats RNG state as one section of the larger state blob, with M5's version stamped alongside the overall state schema version. M1 has zero knowledge of RNG internals.

**Consequences.**

- *Negative:* two version numbers travel together (state schema version + RNG schema version). CI must validate compatibility matrix.
- *Negative:* opaque RNG bytes are harder to debug from a hex dump than a structured layout. Mitigation: M5 ships a debug-only RNG-state pretty-printer.
- *Negative:* generic codec adds one virtual call per RNG section per save/load. Negligible cost (save/load is cold-path) but real.
- *Positive:* M1 stays small; the binary state schema doesn't grow when a new RNG subsystem is added.
- *Positive:* RNG schema can evolve (e.g., adding a new `RunRngType` for a future subsystem) without bumping the overall state schema version.

---

## Q1-ADR-004 — Mod-Layer Discipline for Engine Strip (M8)

**Status:** Accepted.

**Context.** Pipeline ADR-002 commits to "out-of-tree mod via `Core/Modding` where possible; minimize patches to the upstream tree." The "where possible" is the load-bearing phrase. Some Godot surfaces cannot be cleanly replaced via mod hooks (e.g., main-loop replacement, `Engine.GetMainLoop()` substitution, scene-tree elision). For those, in-tree edits are required. Without explicit discipline, in-tree edits accumulate and the rebase cost grows monotonically.

**Decision.** M8 organizes Godot-surface replacements into three tiers, in order of preference: (T1) **mod hook** via `Core/Modding` — preferred, zero upstream edits; (T2) **dependency injection at composition root** — replace a Godot type with a stub via M9 wiring, no upstream edit; (T3) **upstream tree edit** — patch the inherited file. Every T3 edit must be documented with a comment block (`// Q1: <reason>`) and tracked in `modules/engine-strip.md`'s explicit T3 ledger. Quarterly review: are any T3 edits eligible to be promoted to T1 or T2?

**Consequences.**

- *Negative:* T3 ledger is documentation discipline that decays without enforcement. Mitigation: CI grep for `// Q1:` comments must match the ledger.
- *Negative:* some upstream changes will conflict with T3 edits at rebase time; manual reconciliation required per patch.
- *Negative:* T2 sometimes requires non-trivial DI plumbing for what would be a one-line T3 patch. Discipline cost.
- *Positive:* upstream tree stays maximally clean — most patches rebase without conflict.
- *Positive:* T3 ledger gives a precise patch-cost estimate ("we have N in-tree edits") for planning.
- *Positive:* if Megacrit ships a headless API (pipeline ADR-013), all T3 edits become candidates for elimination in one pass.

---

## Q1-ADR-005 — Hook Protocol Schema Co-Versioned with State Schema

**Status:** Accepted.

**Context.** M2 (Hook Protocol) carries serialized state across the IPC boundary in some message types (e.g., the per-decision response includes state + legal-action mask). If M2's schema and M1's schema version independently, a worker holding an old M2 client could receive a state blob it cannot decode, or vice versa.

**Decision.** M2's hook protocol schema and M1's binary state schema share a coordinated version: an M2 message version bump triggers a re-validation of M1 compatibility, and an M1 schema break requires an M2 message version bump. The Game Version Manifest (M1-owned) is included in M2's session-establish handshake; client and server reject mismatched manifests at session start, never silently coerce.

**Consequences.**

- *Negative:* version coordination overhead per change. Cannot bump M2 alone for a non-state-touching reason without revalidating.
- *Negative:* the compatibility matrix between Q1 versions and Q8 client versions becomes a real artifact CI must check.
- *Negative:* schema migrations that would be small in M1 alone may require an M2 version bump too — more release coordination.
- *Positive:* zero cross-version state-decoding bugs in production. The handshake catches it at session start.
- *Positive:* worker supervisor can log "schema mismatch — restart with matching binaries" cleanly, not "weird crash deep in deserializer."

---

## Q1-ADR-006 — Deterministic Effect Ordering is a Spec, Not Implementation-Defined

**Status:** Accepted.

**Context.** When a card play triggers multiple hooks (e.g., on-attack from Strength power, on-attack from Pen Nib relic, on-take-damage from Vulnerable, on-take-damage from Curl Up), the order in which those hooks fire affects the final state. Upstream `Core/Hooks/Hook.cs` resolves order via a mix of registration order and explicit priority. For determinism rank-1, this ordering is part of Q1's behavior contract — not "an implementation detail."

**Decision.** M6d documents the effect-ordering rule explicitly: (1) explicit priority field on hook registrations (highest first), (2) tie-breaking by registration order, (3) registration order is itself deterministic — owner-creature-id then owner-content-id then content-source-position. Any change to this rule is a state-schema-breaking change (because replays under the old rule no longer reproduce). M6d's tests pin specific multi-hook scenarios with expected final state.

**Consequences.**

- *Negative:* changing effect ordering — even in a way the upstream Godot game allows — is a breaking change for Q1. We may diverge from upstream behavior on edge cases where upstream relies on incidental ordering.
- *Negative:* test pinning is labor-intensive; every new power/relic interaction needs an ordering test.
- *Negative:* divergence-from-Godot risk. Differential testing (Q1-ADR-007's probe) is the only way to catch these. The probe must be adversarial against ordering, not just final-state.
- *Positive:* deterministic by construction; no "we got lucky on this run" failure mode.
- *Positive:* the spec is a contract Q2 (Oracle) and Q12 (Eval) can rely on.

---

## Q1-ADR-007 — Determinism Probe is a CI Tool, Not a Runtime Module

**Status:** Accepted.

**Context.** Differential testing against an unmodified Godot build is the gold-standard validator for Q1's mechanical fidelity. Two ways to organize this: (a) a runtime module inside Q1 that compares step-by-step against an embedded Godot instance, (b) an external CI tool that runs both Q1 and unmodified Godot from the same seed and diffs final states (and optionally per-step states).

**Decision.** External CI tool. Lives in a sibling project (target: `~/development/projects/cs/sts2-headless/test/determinism-probe/`), invokes both Q1 and an unmodified Godot binary, runs a fixed seed corpus, asserts state equality at fixed checkpoints. M5 (Determinism Kernel) ships supporting helpers (canonical state hash) but is not the probe itself.

**Consequences.**

- *Negative:* CI environment must run an unmodified Godot binary headfully — heavy and slow. Mitigation: probe runs nightly, not per-PR, with a smaller per-PR fast subset.
- *Negative:* probe is a separate project to maintain — bus factor, build infrastructure, integration with the seed-corpus tooling.
- *Negative:* finding a divergence tells you "something broke" but not exactly where. Requires per-step state hash to localize.
- *Positive:* Q1's runtime stays free of Godot-coupled diff-checking code. M8 strip stays pure.
- *Positive:* the probe can be turned off, parallelized, run only against suspicious PRs — it has its own deployment and lifecycle.
- *Positive:* the probe is also valuable to Q2 (Oracle's pinned-seed regression set) — a single tool for cross-quantum determinism validation.

---

## Q1-ADR-008 — Single-Threaded Decision Path; Concurrency by Process Replication

**Status:** Accepted.

**Context.** C# offers task-parallel libraries, async/await, and cooperative concurrency. None of these are deterministic without significant care: thread scheduling, work-stealing, async continuation order are all sources of latent nondeterminism. The pipeline scales by running many Q1 instances (one per worker, per pipeline ADR-005), not by parallelizing inside one Q1.

**Decision.** Q1's per-decision code path is single-threaded. No task-parallel work; no fire-and-forget; no async/await on the decision path. The only async primitive permitted is the action queue's serial loop (M6d), which is async only in the cooperative sense (yielding back to the IPC loop, not actually parallel). Q1 accepts the throughput cost; horizontal scale comes from process replication. Background work (Prometheus scrape, log flush) lives off the decision path on a single utility thread.

**Consequences.**

- *Negative:* single-core ceiling per Q1 instance. Scaling requires more processes, not bigger machines.
- *Negative:* upstream `Core/GameActions/` may use async patterns that need careful inheritance. Some inherited code may need rewriting to remove `Task.Run` and similar.
- *Negative:* cannot parallelize within a combat (e.g., evaluating multiple branches in parallel inside one Q1). Branches are evaluated by separate Q1 instances.
- *Positive:* deterministic by construction.
- *Positive:* simpler debugging; no race conditions.
- *Positive:* memory budget per process is small and predictable; total memory scales linearly with worker count.

---

## Q1-ADR-009 — Strip Multiplayer Entirely

**Status:** Accepted.

**Context.** Upstream `~/development/projects/godot/sts2/src/Core/Multiplayer/` and `src/Core/GameActions/Multiplayer/` exist for STS2's co-op / shared-room mode. The pipeline-level Q1 spec lists multiplayer as out of scope. Two ways to strip: (a) leave the code in place, never invoke it, hope nothing in the inherited code references it incidentally; (b) delete the files at extraction time, remove all references.

**Decision.** Option (b): delete at extraction time. M8's T3 ledger documents which Multiplayer-touching files in the inherited tree were modified (e.g., to remove Multiplayer references in shared types). All `IsMultiplayer` branches become "single-player path only" with the alternate branch deleted, not dead-code-eliminated.

**Consequences.**

- *Negative:* extracted files diverge from upstream further than minimum. Rebases are noisier in the touched files.
- *Negative:* if STS2 introduces single-player features that share code paths with Multiplayer, our stripped version may miss them.
- *Negative:* the extraction tooling that produces our headless tree from upstream must be aware of Multiplayer — extra logic.
- *Positive:* Q1's binary cannot call multiplayer code by accident. Mechanically impossible.
- *Positive:* state schema is simpler; no per-player synchronization fields.
- *Positive:* deterministic state-machine reasoning is easier without the conditional-on-multiplayer branches.

---

## Q1-ADR-010 — Soft Reuse of `Core/AutoSlay/AutoSlayer.cs` Patterns Where Aligned

**Status:** Accepted.

**Context.** Pipeline ADR-002 Positives: "`Core/AutoSlay/AutoSlayer.cs` already implies an internal automation harness we can lift." It suggests Megacrit has an internal automation pattern with handlers and helpers that approximates an action-decision loop. Reusing it directly couples M2 (Hook Protocol) to upstream patterns that may evolve.

**Decision.** M2 and M4 study `AutoSlayer.cs` and reuse its patterns *where they align with Q1's hook protocol* (action surface naming, decision-boundary identification). Where upstream patterns conflict with Q1's needs (e.g., upstream's reliance on Godot-engine signaling), Q1 deviates without apology. Reuse is at the *pattern* level, not the *code* level — no inheritance from `AutoSlayer`, no subclass extension. The reuse is documented in M2's spec.

**Consequences.**

- *Negative:* duplicates patterns that upstream may evolve in incompatible directions. Periodic reconciliation required.
- *Negative:* "soft reuse" is hard to enforce in code review without explicit before/after diffs.
- *Negative:* if upstream removes `AutoSlay` (it is described as suspicious — possibly internal-only), our pattern source disappears. Mitigation: the pattern is documented in M2's spec independent of upstream existence.
- *Positive:* avoids reinventing the wheel for action-surface enumeration.
- *Positive:* keeps Q1's M2 understandable to engineers familiar with upstream Megacrit code.

---

## Q1-ADR-011 — Parallel Sub-Streams Must Partition by File, Not Code Region

**Status:** Accepted.

**Context.** B.1 dispatched four parallel sub-streams (α RNG, β content audit, γ behavior fill-in, δ encounter policy). β and γ both touched `Phase1Monsters.cs` — β at stat-constant lines near class headers, γ at intent-rotation method bodies. Conceptually disjoint code regions inside one file. On merge, git auto-merge produced a CONFLICT marker; orchestrator resolution via `git checkout --theirs` over the conflict set silently dropped β's BowlbugEgg/Nectar/Rock/Silk HP corrections (γ's stale pre-β versions won). The loss was caught only because β had a unit test (`RollUniqueInitialHp_Skips_Values_In_Taken_Set` expected 21, got 11) that exercised the dropped HP envelope. Had β not landed that test, the drop would have shipped silently, corrupting probe goldens and downstream training distribution.

The failure mode is "silent change loss" — the dropping sub-stream's CI is green (its branch never saw the lost lines), and only post-merge integration testing reveals the drop. Pure code-region-level reasoning gives no merge tool the information needed to detect or resolve such collisions safely.

**Decision.** Parallel sub-streams must partition by file: if two sub-streams need to touch the same file, serialize them. The orchestrator dispatches the dependent sub-stream after the first has merged.

Practical guidance for the dispatch loop:

- Before dispatching a parallel wave, compute the pairwise file-overlap set across sub-streams. Any non-empty pairwise overlap is a serialization point.
- File-overlap on test fixtures or probe goldens regenerated by every sub-stream (universal) is exempted but routed through a single regeneration pass after all sub-streams merge — never resolved by `--theirs` or `--ours`.
- When a structural refactor (e.g., the MonsterIntent state-machine work in B.1-γ) needs to touch many files including those another stream wants, the refactor stream goes first; the consumer streams branch off post-merge.
- File-overlap detected mid-stream (e.g., a stream discovers it must touch a file outside its declared scope) is escalated to the orchestrator, who pauses the conflicting stream until the touching stream merges.

**Consequences.**

- *Negative:* parallel-wave wall-clock cost grows by ~5–10% as more dispatches serialize. Some streams that previously ran in parallel must now run sequentially — total wall-clock is the longest stream of each serial group plus the previous merged base.
- *Negative:* prompt-construction overhead for orchestrator: every sub-stream prompt must explicitly enumerate the files in scope AND mark files NOT in scope (already true under existing dispatch discipline; now load-bearing).
- *Negative:* tracks one more dispatch consideration. Easy to forget the rule and slip back to "they touch different regions, should be fine."
- *Positive:* eliminates silent change-loss class of bugs entirely. Conflicts surface as CONFLICT markers (loud) instead of as merged-but-wrong files (silent).
- *Positive:* removes the temptation to use `git checkout --theirs/--ours` on conflicts, which is the actual proximate cause of silent loss. With strict partitioning, conflict resolution is rare and always file-scoped (preserving the "stream that owns this file" semantic).
- *Positive:* makes parallel waves auditable — git log shows which sub-stream owns which file change; no interleaved authorship per file.

**Cross-references.** Tracks risk R8 (parallel-edit conflict surface) in the project risk register.

**Origin.** Lead's directive 2026-05-11 after the B.1-β/γ merge incident.

---

## Q1-ADR-012 — Schema Lock for `contracts/schemas/game-simulator/` v0.1 (combat-only)

**Status:** Accepted (project-lead Q8-proxy review 2026-05-12; committed as `chore(contracts): bump game-simulator schemas to v0.1` `d94afa6`).

**Context.** D1 deliverable per project-lead direction 2026-05-12. Phase-1A ratification depends on locking the cross-quantum schemas in `contracts/schemas/game-simulator/` so Q2 (post-D1 boot) and Q8 (substrate work) can target a stable wire contract. Pipeline ADR-001 makes schema migrations versioned releases; this lock formalizes v0.0 (skeleton) → v0.1 (combat-complete surface).

The v0.0 envelope+`LegalAction` proto3 surface was skeleton-grade and predated M1 (S7) / M2 (S9) final shapes. Specifically: `state_blob.proto` envelope fields were fine but lacked a comment-block on inner-payload SHA rule or manifest-stamp invariants; `hook.proto` `LegalAction` carried only `card_index` + `target_index` — missing pipeline-ADR-008 sequential-targeting payload, decision-phase enum, first-class end-turn action.

Two evolution shapes considered:

- (a) Extend in place under v0.1 (additive minor bump). v0.0 callers still see `card_index`/`target_index` field positions intact.
- (b) Carve a v1 package and leave v0 Phase-1-only.

**Decision.** Option (a): minor bump v0.0 → v0.1. v0 surface is COMBAT-ONLY. Run-level message types (card-pick, map, shop, event, rest, potion-out-of-combat) live behind a future v1 release (S15 per stage manifest). v1 is a cross-quantum coordination event for the (then-existing) Q8 lead; v0.x review is by project-lead-as-Q8-proxy per 2026-05-12 directive until Q8 lead boots.

Specific additions (full diff in commit `d94afa6`):

1. `state_blob.proto` adds comment-block fixing manifest-stamp invariants and inner-payload SHA rule. 5-tuple state-snapshot provenance identity stated.
2. `hook.proto` adds:
   - `DecisionPhase` enum `{UNSPECIFIED, DECISION, AWAITING_TARGET_N}` — pipeline-ADR-008 sequential-targeting support. CHANCE intentionally omitted from v0; Q1 resolves all RNG internally before each player-decision boundary.
   - `SpecialTarget` enum + `TargetRef` oneof — indexed slot / named slot (e.g. "crusher" / "rocket" per D3 fixture #4) / virtual target.
   - `LegalAction.surface` oneof — first-class `PlayCardAction`, `EndTurnAction`, `PotionAction`, `TargetChoice` sub-actions.
   - `DecisionRequest.phase` field — Q1 publishes phase per request. Wire default is `UNSPECIFIED` (proto3 0-value); Q1 in v0.1+ MUST set explicitly. Consumer `UNSPECIFIED`→`DECISION` fallback is a convention, not a wire default.
3. Both files' `// sts2.schema.minor` header bumped 0 → 1.
4. v0 surface explicitly noted as combat-only; v1 carve-out reserved.

**Consequences.**

- *Negative:* Q1 codegen + Q2 codegen must regenerate against v0.1 protos. One round-trip in the rollout.
- *Negative:* `LegalAction.card_index` + `target_index` (v0.0 fields) retained for backwards-compat; new code SHOULD prefer the `surface` oneof. Discipline cost in code review to prevent dual-API drift.
- *Negative:* v0 locked combat-only means Phase-2 run-level messages cannot reuse v0 surface — they MUST live behind v1. Cannot future-proof v0 against Phase-2 needs (lead's explicit constraint 2026-05-12).
- *Negative:* CHANCE phase omission means Phase-1A MCTS at Q8 cannot natively branch over chance nodes on the wire. Workarounds: single-sample (weakens search on draw-sensitive states — Silent + Ring of the Snake is a worst case), or clone-and-resample via M4 (correctness via expense). Chance-on-wire reserved for v1; revisit when real Q8 lead boots and MCTS chance-handling is scoped.
- *Negative:* No real Q8 lead exists yet; lead-as-Q8-proxy review carries forward-compat risk. Mitigation: v1 carve-out reserved for rebalancing exactly when Q8 lead boots.
- *Positive:* Q2 / Q8 / Q12 substrate work can boot against a stable contract without further blocking on Q1.
- *Positive:* Sequential-targeting is structurally encoded — Q1 publishes `phase=AWAITING_TARGET_N`, Q8 emits `TargetChoice` sub-action, no string-matching on `action_type`.
- *Positive:* state-snapshot provenance is self-describing — 5-tuple in the envelope identifies the producing simulator + wire format. This is the state-snapshot half of the pipeline ADR-001 reproducibility chain; the model-artifact half is owned by Q5.

**Origin.** Lead's directive 2026-05-12. Q8-proxy review by project lead 2026-05-12.

---

## Q1-ADR-013 — Caller-Callee Contract Gap Mitigation (Q1-ADR-011 follow-up)

**Status:** Accepted (project-lead direction 2026-05-12).

**Context.** Q1-ADR-011 mandates parallel sub-streams partition by file. The B.1-β/γ incident that motivated Q1-ADR-011 was direct file overlap with silent change-loss via `--theirs` resolution. The wave-2 D3/D6 merge surfaced a related but distinct failure mode:

- D3 owned `engine/headless/test/Sts2Headless.Tests.Tools/Fixtures/StateBlobFixtureRecipe.cs` (new file).
- D6 owned `engine/headless/src/Sts2Headless.Host/CliArgs.cs` (modified to add a required positional `RegistryPath` parameter).
- D3 USED `Sts2Headless.Host.CliArgs` from D6's owned module.
- Both sub-streams' `make ci` was green individually.
- Merge into `main` failed to compile (`CS7036 RegistryPath required`).

Q1-ADR-011's partition-by-file rule does not catch caller-callee contract changes where stream X uses a type that stream Y extends. The new failure mode is "loud at integration" (compile error) rather than "silent change loss" — strictly better than the original B.1 bug, but still a wave-level wall-clock cost the orchestrator must mitigate. Surface size grows for P-1.5-1 where 5+ sub-streams have substantial cross-module type usage (console host + 12 stubs + Pinned harness + driver + tests).

**Decision.** Three elements added on top of Q1-ADR-011:

1. **Declarative public-surface diffs.** Each sub-stream's DONE report MUST enumerate any public-type-signature change (record/class/interface) in modules the stream owns. One line per signature change is enough. Empty if the stream made no public-surface changes.

2. **Orchestrator post-merge compile gate.** After merging a parallel wave into `main` and before reporting wave-complete to the project lead, run `make ci` (or a faster `dotnet build`) across the merged tree. If red, re-dispatch the affected caller-side sub-stream with the new signature. The integration fix may be done inline by the orchestrator if ≤ 20 LOC per role-prompt §4 (caller-side wire-up) — the failure must still be reported in the wave status, not silently absorbed.

3. **Pre-dispatch heuristic (optional).** When constructing parallel dispatches, scan each sub-stream's prompt for "uses public type from a module owned by another sub-stream." Flag as a serialization point unless the modifying stream promises additive-only changes. Use when wall-clock cost of misfire exceeds the cost of serialization.

**Consequences.**

- *Negative:* sub-stream DONE-report scope grows by one section. Trivial in practice; most streams have zero public-surface changes.
- *Negative:* orchestrator's post-merge step adds one `make ci` invocation per wave (~30 s for current Q1 build). Cumulative cost grows with wave count.
- *Negative:* pre-dispatch heuristic is judgment-based. Easy to over-fire (over-serialize, losing parallelism) or under-fire (miss a contract change, hit the gate at merge time).
- *Negative:* "additive-only" promises in stream prompts are not auditable from a prompt alone. Verification still happens at merge time.
- *Positive:* eliminates the integration-time-compile-fail class of bugs entirely when the heuristic fires correctly.
- *Positive:* the post-merge compile gate is a definite catch (deterministic), independent of the pre-dispatch heuristic's accuracy.
- *Positive:* DONE-report public-surface diffs become an auditable record of which streams change which contracts. Useful for future cross-stream coordination.

**Cross-references.** Q1-ADR-011 (parent: partition-by-file rule). The D3/D6 incident is recorded in wave-2 status.

**Origin.** Lead's directive 2026-05-12 after the D3/D6 caller-callee integration break (orchestrator fix at commit `30840c5`).
