# Submodule: run-config

> Identity + provenance bootstrap. Loads `config/local.json`, validates run
> parameters, captures `(code_sha, host, start_ts, seed, parent_artifact_id)`
> once. Both the config and the provenance snapshot are immutable post-construct.

## Responsibilities

- Load and validate `pipeline/trainer/config/local.json`. Required keys
  (Phase 1): `service`, `port`, `data_dir` (inherited from
  `pipeline/common/service_host.py:load_config`); Q10-specific: `q3_url`,
  `q5_url`, `parent_artifact_id` (nullable for from-scratch runs),
  `seed`, `network`, `optim`, `loss_weights`, `checkpoint`, `wandb`.
- Resolve `data_dir` relative to repo root if not absolute (Q3 convention
  at `experience-store/service.py:251-256`).
- Capture `RunProvenance` at bootstrap: `code_sha` via `git rev-parse HEAD`
  + `git status --porcelain` (refuse-or-warn on dirty per config flag),
  `start_ts_ns`, host info, `seed`, `parent_artifact_id`. Frozen for the
  lifetime of the run.
- Expose typed dataclasses to all other submodules: `RunConfig`,
  `RunProvenance`, plus per-section dataclasses (`NetworkConfig`,
  `OptimConfig`, `LossWeights`, `CheckpointConfig`, `WandbConfig`).
- Seed Python's `random`, NumPy, and `torch` RNGs via a single
  `seed_everything(seed)` helper invoked once by `service.py.__init__`.

Out of scope: training-step logic (`train_driver`), provenance manifest
serialization (`artifact_publisher.manifest`), runtime config mutation
(no submodule may write `RunConfig`; reload-on-SIGHUP not supported in
Phase 1).

## Data Ownership

In-process, immutable after construction:

- `RunConfig` — frozen dataclass; all submodule config sub-blocks reachable
  as attributes.
- `RunProvenance` — frozen dataclass; serialized into the provenance
  manifest at publish time by `artifact_publisher`.

Schema-owning? **No** at the wire/disk level. The provenance manifest
schema is owned by `artifact_publisher` (Q10's only schema-owning module).

## Communication

**External:** none. `run_config` does no I/O beyond the local config file
and `git`.

**Internal (in-process function calls, sync):**

- `RunConfig.load(path: Path) -> RunConfig` — called once at startup.
- `RunProvenance.capture(config: RunConfig, parent_artifact_id: Optional[str])
  -> RunProvenance` — called once at startup, before any background thread
  starts.
- `seed_everything(seed: int) -> None` — called once at startup.

**Metrics:**

- `sts2_q10_run_dirty{value="0"|"1"}` — gauge; 1 if `git status` reported a
  dirty tree at bootstrap. Lets operators audit whether published artifacts
  were built from a clean SHA.

## Coupling

- **Afferent (in):** `service.py.__init__` (constructs once).
- **Efferent (out):** filesystem (`config/local.json`); subprocess (`git
  rev-parse`, `git status --porcelain`).
- **Indirect:** all other submodules consume `RunConfig` / `RunProvenance`
  read-only.

## Testing Strategy

### Unit

1. **Missing required key raises.** Config without `q3_url` → `ValueError`
   listing the missing key. Absent test: silent defaults mask
   misconfigurations.
2. **Code-SHA captured cleanly.** Clean git tree → `run_dirty=False` and
   `code_sha` matches `git rev-parse HEAD`. Absent test: provenance lies
   about reproducibility.
3. **Dirty tree behavior is configurable.** With `refuse_on_dirty=True`,
   dirty tree → `RuntimeError`. With `refuse_on_dirty=False`, dirty tree
   → `run_dirty=True` and bootstrap proceeds. Absent test: experimental
   work blocked or untracked.
4. **`seed_everything` reproducibility.** Seed `s`, generate 100 random
   tensors via torch + numpy + random → second run with same `s` produces
   bit-identical output. Absent test: training is not reproducible.
5. **Frozen dataclass refuses mutation.** Attempting `config.q3_url = 'x'`
   raises `FrozenInstanceError`. Absent test: a submodule mutates config
   mid-run, breaks reproducibility.

### Integration

1. **Bootstrap with parent_artifact_id chain.** Configure
   `parent_artifact_id="abc123"`; `RunProvenance.capture` records it;
   downstream `artifact_publisher` includes it in the published manifest.
   Verifies the lineage contract from Q10-ADR-001.
2. **Config schema bump policy.** Add a new required key in a future
   spec bump; old configs fail loudly with a clear message naming
   the new key (no silent default). Verifies upgrade UX.

### Smoke (mandatory)

- `run_config` is constructed implicitly by `service.py.__init__`; failures
  surface as service-start failures. The `pipeline/tests/smoke_services.py`
  trainer entry exercises this path.
