# Excluded-spec audit — 2026-05-16

> Phase 4 of doc/spec sync initiative. Audits Q3/Q5/Q6 specs not repaired in
> Phase 2. Findings ranked HIGH/MEDIUM/LOW per ADR-023 acceptance criteria.
>
> **Auditor note:** ADR-023 (Spec Status Badges + Module-Spec Frontmatter) is
> referenced by the badge preamble on the `docs/spec-sync-phase1` branch but
> does not exist in `docs/specs/01-decisions-log.md` on main or in this
> worktree (last entry is ADR-022). All three specs have YAML frontmatter +
> badge-preamble on `docs/spec-sync-phase1` but NOT on main / this worktree's
> base. Severity classifications below are against the badge convention implied
> by the preamble text itself (`[SHIPPED]` / `[PHASE-N]` / `[ASPIRATION]`).

## Summary

- **3 HIGH-severity findings** → recommends follow-up repair wave: **YES**
- **4 MEDIUM-severity findings**
- **3 LOW-severity findings**

---

## Q3 Experience Store

**Substrate audit:** `pipeline/experience-store/` is a substantively implemented
Phase-1A service. `service.py` wires HotStore, SchemaRegistry, IngestAPI,
Sampler, Lifecycle, SidebandRouter, ProvenanceLog, RetentionController into a
`ThreadingHTTPServer`. `sampler/engine.py` is the uniform sampling core.
`priority_index/__init__.py` and `cold_store/__init__.py` are explicit Phase-2+
stubs (their `__init__` docstrings state "Phase-1 stub only").

**Findings:**

- HIGH: The **Responsibilities** section lists `"uniform, stratified-by-bucket, prioritized"` as current sampling modes with no phase qualification. `sampler/engine.py` is self-described as "Phase-1A uniform sampling core"; `priority_index/__init__.py` states "Phase-2+ activation. Phase-1 stub only." An agent reading the Responsibilities bullet would assume all three modes are available today and wire calls to them.

- HIGH: The **Responsibilities** section states Q3 tiers data between hot and cold tier as a current responsibility, with no phase qualifier. `cold_store/__init__.py` is "Phase-2+ activation. Phase-1 stub only." Hot-tier-only behavior is correctly documented in `Phase Expectations` but the top-level Responsibilities list implies tiering is active now.

- MEDIUM: **Responsibilities** names Q11 (Curriculum Generator) as a current writer (`"Q8 and Q11 append via a streaming write API"`). Q11 is TBD Phase 2+ per the quantum map. An agent wiring ingest clients would include Q11 prematurely.

- MEDIUM: No section-level badges (`[SHIPPED]` / `[PHASE-N]` / `[ASPIRATION]`) anywhere in the body despite the frontmatter preamble referencing ADR-023. A reading agent has no machine-scannable signal for what is live vs. deferred; it must infer from prose.

- MEDIUM: The **Data Ownership** section lists "Priority index — per-trajectory priority floor for prioritized replay" as a current data artifact. This is a Phase-2+ construct; the Phase-1 column family exists but the Python submodule is a stub. No phase qualifier in Data Ownership.

- LOW: Sampler references `"RocksDB or LMDB"` for the hot tier. The actual implementation (`hot_store/store.py`) uses a flat-file append log (per `_framing.py` / `_atomic_io.py`) — not RocksDB or LMDB. Not actively misleading for Phase-1 work but will mislead an agent expecting RocksDB APIs.

**Net assessment:** Q3 is usable at a high level — the Phase Expectations section correctly partitions the roadmap. However an agent picking up any work involving sampling modes, tiering, or ingest clients would be misled by the Responsibilities section into implementing Phase-2+ code as if it were Phase-1 scope. The two HIGH findings are in the first section read.

---

## Q5 Model Registry

**Substrate audit:** `pipeline/model-registry/` contains exactly two artifacts:
`config/local.json` (service name + port + data_dir) and `service.py` (3 lines
delegating to `pipeline.common.service_host.main`). No publish API, no fetch
API, no metadata table, no blob store, no promotion log, no ONNX export — the
substrate is a pre-boot placeholder.

**Findings:**

- HIGH: The **Responsibilities** section describes `publish(blob, metadata) → artifact_id`, `fetch(artifact_id) → blob_path`, artifact blob store, metadata table, promotion log, and ONNX export pipeline as current Q5 responsibilities. None of these exist in the substrate. An agent picking up Q5 work would arrive expecting a service to extend, but would find only a 3-line stub. Every spec section reads as if it describes a running system.

- MEDIUM: No section-level badges (`[SHIPPED]` / `[PHASE-N]` / `[ASPIRATION]`) in the body. The entire spec is implicitly aspirational but carries no markers that tell a reading agent this.

- MEDIUM: **Phase Expectations** says "Phase 1: Single-host metadata store; local filesystem blob store mirrored to S3-equivalent. Manual promotion." That implies Phase-1 delivers a working artifact store. The substrate gives no indication whether this is implemented or still pending boot. A Q5 boot-directive writer would not know where Phase-0 left things.

- LOW: The **Coupling** section lists `"Afferent (in): Q9 (fetches at startup / promotion), Q12 (fetches under-test artifact)"` — both fetching from an API that does not exist. Harmless for spec-reading but would break any agent attempting to wire those calls.

**Net assessment:** Q5 spec is entirely aspirational; the substrate is pre-boot. The spec does not say this anywhere. An agent picking up Q5 work with this spec as context would not know whether to boot the service from scratch or extend an existing one — a HIGH-severity ambiguity for any boot-directive author.

---

## Q6 Evaluation Reports

**Substrate audit:** `pipeline/evaluation-harness/` contains the same two
artifacts as Q5: `config/local.json` (service name `evaluation-harness`, port
18112, data_dir) and `service.py` (3 lines delegating to
`pipeline.common.service_host.main`). No report tables, no CI gate RPC, no
drilldown index — pre-boot placeholder.

**Findings:**

- HIGH: The **Responsibilities** section describes storing evaluation outputs, enforcing versioning, indexing drilldowns, and gating CI as current Q6 responsibilities, including a concrete `gate_check(artifact_id, phase) → pass/fail` RPC in the Communication section. None of this exists in the substrate. An agent picking up Q6 work expecting a service to extend would find only a 3-line stub.

- MEDIUM: No section-level badges in the body. The spec reads as a fully-operational system.

- MEDIUM: **Data Ownership** lists Parquet report tables, a report-version manifest, a drilldown index, machine-readable CI gate rules, and exploit-incident records — none of which exist. A Q6 boot-directive author needs to know the slate is blank; the spec implies pre-existing structure.

- LOW: **Coupling / Efferent** lists `"Q5 (artifact metadata)"` as an outgoing coupling — Q6 writes to Q5's metadata. In the current substrate neither Q5 nor Q6 has any implementation. The coupling is architecturally intended but should carry a phase qualifier; as written it implies a live dependency.

**Net assessment:** Q6 is fully aspirational; the substrate is pre-boot. Same problem as Q5 — the spec conceals the blank-slate reality from any agent assigned Q6 boot work.

---

## Recommendation

**Follow-up Phase 2.5 repair wave recommended on Q3/Q5/Q6.**

The three HIGH findings (Q3 Responsibilities mixes Phase-1 and Phase-2+ scope
without badges; Q5 and Q6 describe fully-operational systems over pre-boot
substrates) would actively mislead engineer subagents picking up boot or
extension work on any of these quanta. The required repairs are bounded:

1. **Q3** — add `[SHIPPED]` / `[PHASE-N]` badges to Responsibilities, Data
   Ownership, and Communication; move prioritized sampling and tiering bullets
   under `[PHASE-2]`; add Q11 phase qualifier.
2. **Q5** — add a prominent "Phase-1 status: pre-boot" note and badge all
   sections `[PHASE-1]` (not yet shipped); gate the Responsibilities prose
   under `[PHASE-1]` with an explicit note that the substrate is a stub.
3. **Q6** — same treatment as Q5.
4. **All three** — ensure ADR-023 is merged to main before or alongside the
   repair wave so the `see ADR-023` preamble reference is resolvable.
