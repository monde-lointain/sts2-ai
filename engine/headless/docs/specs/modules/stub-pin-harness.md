# Stub-Pin Harness (Phase-1.5 design)

Required mitigation for R9.1 (singleton-stub semantic drift) per project-lead direction 2026-05-12. Baked into Phase-1.5 upfront — retrofitting after singleton-stub drift surfaces as a per-step probe DIVR is harder.

## Problem

`CombatManager.StartCombatInternal` references ~12 SceneTree-coupled singletons (`NRunMusicController.Instance`, `NCombatRoom.Instance`, `NModalContainer.Instance`, `NCombatStartBanner.Create()`, `NCombatRulesFtue`, `Cmd.CustomScaledWait`, `SaveManager.Instance`, `RunManager.Instance.ActionExecutor`, `NetCombatCardDb.Instance`, + 3 `await` points).

Headless per-step parity (P-1.5-A) requires stubbing all of them. **One missed semantic** — wrong call-count, wrong arg, missed continuation order — produces a nondeterministic divergence indistinguishable at the stub level. Downstream symptom: per-step probe DIVR with no localization of which stub drifted.

Cost without harness: per-step bisection from final state divergence back to the first divergent step + read upstream singleton source to find which call differed. Hours per failure. Hides systematic errors (e.g., one stub silently wrong across all encounters).

Cost with harness: stub-pin diff names the drifted stub immediately. Minutes per failure.

## Mechanism

### Per-call recording

Each stub wraps its production replacement in a `Pinned<TStub>` decorator:

```csharp
public sealed class Pinned<TStub> : TStub {
    private readonly TStub inner;
    public int CallCount { get; private set; }
    public List<long> ArgShapeHashes { get; } = new();
    public List<string> CallSequence { get; } = new();  // ordered, per-stub

    public override TReturn Call(TArg arg) {
        CallCount++;
        ArgShapeHashes.Add(CanonicalHash.Of(arg));  // M5 CanonicalHash
        CallSequence.Add($"{typeof(TStub).Name}.{methodName}({CanonicalHash.Of(arg):X16})");
        return inner.Call(arg);
    }
}
```

- `CanonicalHash` from M5 (S1-T5, `1d2931e`) gives a deterministic content hash over the arg payload.
- `CallSequence` records the ordered call across this stub only. Per-run cross-stub interleave is captured in a separate run-level trace (next section).

### Run-level trace

The probe driver collects all per-stub records at session end into a single `stub-trace-<seed>-<encounter>.jsonl` artifact:

```jsonl
{"step":0,"stub":"NRunMusicController","method":"StopMusic","argHash":"0x0000000000000000","callIdx":1}
{"step":0,"stub":"NCombatRoom","method":"SetEnemies","argHash":"0xA1B2C3D4E5F60718","callIdx":1}
{"step":0,"stub":"NetCombatCardDb","method":"GetCard","argHash":"0x1234567890ABCDEF","callIdx":1}
...
{"step":1,"stub":"RunManager_ActionExecutor","method":"Enqueue","argHash":"...","callIdx":1}
```

`step` is the per-decision step counter from the existing probe per-step infrastructure. `callIdx` is the cumulative call count for that stub. Order within a step preserves the interleave.

## Gates (binding before P-1.5-2)

Three checkpoints, in order. ANY failure halts the stage and forces stub-stack repair.

### Gate 1 — Initial-state stub-trace baseline

After P-1.5-1 ships the console host + 12 stubs, capture a stub-trace for all 16 encounters at seed 42 against the Stream-C initial-state goldens (already byte-exact upstream-derived).

- Pass: stub-trace exists, replays deterministically (no per-run variance).
- Fail: stub stack is nondeterministic on its own. Halt; bisect across stubs.

### Gate 2 — Smoke-set per-step stub-trace pin

CultistsNormal smoke is the existing per-step gold (50 seeds × per-step Godot parity from S13). Capture stub-trace for smoke seeds; pin as `goldens-stub-traces/cultistsNormal-perstep-seedN.jsonl`.

- Pass: smoke stub-trace replays byte-exact against the pin.
- Fail: stub stack drift on a known-good corpus. Halt; localize via the first-diverging line in the trace.

### Gate 3 — Non-smoke encounter freeze

Before any non-smoke encounter runs through the stub stack, the stub-trace from Gate 1 and Gate 2 must be frozen. After freeze, any stub-stack change requires explicit re-pinning + project-lead notification (re-surface trigger).

- Reason: capturing a per-step golden against a stub stack that later changes silently invalidates the golden.

## Failure-mode catch order

When a per-step probe run on a non-smoke encounter fails Phase-1.5-2 capture, the bisection order is:

1. **Stub-pin diff** (loudest, earliest) — name the drifted stub. Fix at stub level.
2. **Per-step state hash diff** (downstream) — name the diverged step.
3. **Final-state hash diff** (latest catch, only if 1 + 2 are clean) — fall-back; usually means a subtle state-codec issue, not a stub issue.

Stub-pin failure short-circuits per-step bisection. The probe-localization Risk R6 mechanism (CanonicalHash + first-diverging-action) gives step-level localization; stub-pin gives stub-level localization. Both compose.

## Implementation cost

- `Pinned<TStub>` decorator template — generic, ~50 LOC.
- 12 stub-class adaptations to be wrapped — ~50 LOC total (mostly mechanical).
- Probe driver hook to dump trace at session end — ~30 LOC.
- Trace comparison utility — ~50 LOC.
- Golden management (capture / pin / diff) — ~30 LOC.
- Total: **~200 LOC**. Bundled with P-1.5-1.

## Non-goals

- The harness does NOT validate semantic correctness of stub returns. It validates **call shape consistency** between runs. Correctness against upstream Godot is downstream's job (per-step probe vs Godot).
- The harness is test-mode only. Production builds wire raw stubs without `Pinned<>` decoration — zero runtime cost.

## Re-surface trigger

Stub-stack changes after Gate 3 freeze require lead notification + golden regeneration. Re-surface concretely:

- "P-1.5-1 added/modified stub `<name>`. Goldens at `goldens-stub-traces/` must be re-captured. Proceed?"

This is a Phase-1.5 internal trigger, not a project-lead-blocking one — included here so the orchestrator surfaces before silent golden regen.

## Provenance

Per lead pushback 2, 2026-05-12. Cross-references R6 (probe localization) and Q1-ADR-007 (probe is a CI tool, not a runtime module).
