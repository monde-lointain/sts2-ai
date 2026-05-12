# Sts2Headless.MockWorker — Reference Q8 IPC Peer

Pinned to: `contracts/schemas/game-simulator/hook.proto` v0.1 (Q1-ADR-005, Q1-ADR-012).

## Scope (binding)

This project is a **reference Q8 IPC peer, not a production Q8**. Its sole purpose is to
let Q1's `Sts2Headless.Adapters.HookProtocol` exercise the M2 wire protocol end-to-end
in tests — specifically the **S9 latency hard gate** (p99 < 500 µs over 10 000 roundtrips,
per `engine/headless/docs/specs/modules/hook-protocol-adapter.md`).

### What this worker does

- Attaches to SHM rings + semaphores created by `HookProtocolAdapter`.
- Echoes a `ManifestResponse` matching Q1's manifest.
- Replies to `HookRequest` with one of two scripted action payloads (`echo` / `always-end-turn`).
- Exits cleanly on `Terminate`.

### What this worker does NOT do

- **No MCTS, no policy network, no value head.** Q8's real substrate owns AlphaZero combat search.
- **No run-level decision handling.** Card-pick, map, shop, event, rest, potion-out-of-combat,
  and Phase-2 run-level messages are out of scope — those live behind hook.proto v1 (S15)
  and are Q8's quantum.
- **No state-blob deserialization.** The worker treats `state_blob` as opaque bytes.
  Real Q8 substrate decodes via Q2's `engine→CompactState` adapter (ADR-011) or its own
  `RichState` codec.
- **No latency targeting other than "minimize work per frame."** Real Q8 substrate tunes
  for batched GPU inference; this peer prioritizes deterministic correctness over throughput.

## Re-surface trigger

If a change to this worker is needed beyond what the S9 latency gate exercises, **stop**.
That change belongs in Q8's substrate or behind a hook.proto v1 carve-out, not here.

## Wire-version pin

The header in `Program.cs` is pinned to `hook.proto v0.1`. Schema migrations:

- **Minor bump (additive)** — re-pin the header comment + re-generate codegen.
- **Major bump** — coordination event per pipeline ADR-001; loop in Q8 lead if booted,
  otherwise project-lead-as-Q8-proxy per 2026-05-12 directive.

## How to run

See `Program.cs` header. Direct invocation is for the S9 test harness only; do not invoke
manually in production workflows.

## Provenance

Originally added at S9-T5 (`4c174b4 feat(mock-worker): standalone Q8 simulator subprocess`).
Pinned to hook.proto v0.1 in D2 (project-lead direction 2026-05-12).
