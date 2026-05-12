# Module: Engine Strip / Mod Layer (M8)

> Replaces Godot rendering, audio, animation, UI, scene-tree, lifecycle, platform-integration, and telemetry-vendor surfaces with deterministic no-ops or test doubles. Implemented as out-of-tree mod via `Core/Modding` where possible (per pipeline ADR-002 and Q1-ADR-004's tier discipline).

## Responsibilities

- Provide deterministic no-op or test-double implementations of every Godot surface the inherited `src/Core/` code calls. Categories below.
- Apply replacements at composition time via M9 (T2 — DI substitution) or via mod hooks (T1 — `Core/Modding`). Where neither works, apply T3 (in-tree edit) and document in this module's T3 Ledger.
- Ensure M8 stubs are *behaviorally inert*: no allocation in the decision path, no I/O, no clock reads, no nondeterministic data. A stub `Audio.Play(sound)` is `void return`; a stub `Animation.Tween(...)` returns immediately.
- Provide stubs whose presence is detectable by tests: every stub registers with a `StubRegistry` so tests can assert "no stub was hit during this test" or "exactly these stubs were hit."
- Maintain the **T3 Ledger**: an explicit list of every in-tree edit applied to inherited Godot-coupled files, with `(file_path, edit_summary, reason, T1/T2-reason-rejected)` for each. Reviewed quarterly per Q1-ADR-004.
- Enforce stub coverage at build time: a CI rule fails the build if a Godot-namespace API is called from outside M8 (analyzer rule against `using Godot;` in domain assemblies).

`[Phase 1 scope]` — full M8 functional. Without M8, Q1 cannot run headless at all. Stub coverage matches the inherited code paths exercised by Phase 1.

`[Phase 2]` — stub coverage extended to run-level paths (full `Run` + `Map` + `Rooms` + `Events` + `Shops`).

`[Phase 3+]` — coverage extended for any new STS2 surfaces introduced by patches. Quarterly T3-ledger review.

Out of scope: replacing the entire Godot runtime (we use the .NET runtime directly per pipeline ADR-002, not Godot's headless mode); replacing third-party vendor packages we still use (e.g., we keep `System.Text.Json` for non-state JSON).

## Data Ownership

M8 owns the **boundary contract** between upstream STS2 surfaces and headless stubs — not a versioned data schema. The contract is structural: which Godot APIs are stubbed, which categories they fall in, and whether the replacement is T1, T2, or T3.

- **Stub category list** (groupings; full leaf enumeration deferred to implementation per Phase 1 plan):
  - **Rendering** — `Node2D`, `Sprite`, `MeshInstance`, shaders, draw calls.
  - **Audio** — `AudioStreamPlayer`, sound playback, music transitions.
  - **Animation** — `Tween`, `AnimationPlayer`, frame-driven effects.
  - **Input** — `Input.IsActionPressed`, `InputEventMouse*` (no input arrives in headless).
  - **Scene-tree** — `Node._process`, `_ready`, `Node.AddChild`, `SceneTree.ChangeScene`.
  - **Lifecycle** — `Engine.GetMainLoop`, `Engine.GetFrameTicks*` — replaced with the deterministic loop driven by M9 + M6d.
  - **Telemetry vendors** — `Sentry` (crash reporting), `Steamworks.NET` (Steam features), `Vortice.DXGI` (DirectX integration). All replaced with no-op shims.
  - **File I/O via Godot** — `FileAccess`, `DirAccess`, `Resource.Load`. Replaced with .NET `System.IO` equivalents inside M3 (replay) / M9 (config); domain code does not access files.
  - **Localization & string-format runtime** — Godot's `tr` and `SmartFormat` — replaced with a deterministic wrapper that returns the localization key when no human is reading.
  - **0Harmony reflection hooks** — used by upstream for runtime patching; mostly stubbed; selective forwarding only where modding integration requires it.
- **T3 Ledger format**: a markdown table at the bottom of this file (see §T3 Ledger placeholder), maintained by the team and audited each quarter.

## Communication

### Synchronous (in-process calls)

- **Inbound:** any inherited code path that calls a Godot surface (these are the call sites M8 must cover).
- **Outbound:** none — stubs are leaves.

### Asynchronous

- None.

### Events emitted

- Stub-hit registrations to `StubRegistry` (tests inspect this).
- Optionally: stub-hit counters to M9 telemetry for visibility into "what surfaces did this run touch."

## Coupling

- **Afferent (in):** M6a, M6b, M6c (any inherited code path that calls Godot APIs); M9 Process Host (DI wiring for T2 stubs).
- **Efferent (out):** **none** at runtime. M8 is structurally a leaf — its job is to terminate calls into Godot, not forward them.

Aim: M8 has zero outbound runtime dependencies. It is the *floor* of Q1's dependency graph from the Godot-surface side, mirroring how M5 is the floor from the determinism side.

## Testing Strategy

### Unit Tests

Mock nothing — stubs are pure. Focus on ensuring stubs are inert and detectable.

- **Per-stub-category inertia:** for each category, invoke every stubbed method; assert no allocation, no exception, no side-effect (apart from `StubRegistry` registration).
- **No-clock-leak in stubs:** a stub `Audio.Play()` must not call `DateTime.Now` or `Stopwatch`. Static-analysis rule fails the build if it does.
- **No-IO-leak in stubs:** stubs must not read or write files (M3 and M9 are the only IO-permitted modules). Static-analysis rule.
- **Detection:** invoking a stub from a test fixture causes that stub to appear in `StubRegistry.GetHitsThisTest()`.
- **T3 ledger consistency:** the T3 ledger entries match the actual `// Q1: <reason>` comments in the inherited source tree (CI grep cross-check).
- **Coverage gate (per Phase 1 surface set):** a known-invocation test exercises every Phase-1-relevant code path; assert every stub category is hit at least once. New uncovered Godot calls in inherited code fail the gate.

### Integration Tests

Verify M8's quantum boundaries:

- **Full Q1 boot with M8 active:** spawn Q1; assert no exception during init; assert no Godot.* call escapes to the real Godot runtime (we do not link Godot at all in headless; if a call escaped it would be a missing-method exception, which would already fail).
- **Differential boot vs Godot:** start Godot in headless mode and Q1 with M8; both reach "ready for first decision" with identical `M1.CanonicalHash(initial_state)`.
- **Stub-coverage telemetry:** during a full Phase-1 combat, M9's stub-hit counters are non-zero across exactly the expected categories (Rendering, Audio, Animation hit; SceneTree-lifecycle hit; etc.).
- **T3-edit re-promotion drill:** quarterly, attempt to convert one T3 entry to T2 (DI) or T1 (mod hook); update ledger. CI tests for the converted edit pass without regression.
- **Patch-rebase smoke:** after a hypothetical upstream STS2 patch, the T3 ledger entries either rebase cleanly or surface a list of conflicts. Conflict count is a quarterly-tracked metric.

---

## T3 Ledger (placeholder)

In-tree edits to inherited Godot-coupled files. Maintained per Q1-ADR-004.

| File | Edit Summary | Reason | T1/T2 Rejected Because | Date Added | Last Reviewed |
|------|--------------|--------|------------------------|------------|---------------|
| _(none yet — populated during Phase 1 implementation)_ | | | | | |
