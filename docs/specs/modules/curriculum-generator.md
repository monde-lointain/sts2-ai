---
quantum: Q11
substrate: n/a (Phase-2+ TBD)
---

> Status legend: see ADR-023. Section badges = `[SHIPPED]` / `[PHASE-N]` / `[ASPIRATION]`.

# Module: Curriculum Generator (Q11)

> Synthesizes mid-run starting states and adversarial scenarios. Phase 2+. Writes to Q3 with explicit provenance tags so the trainer can stratify.

## Responsibilities

- **Synthetic mid-run states.** Generate "start at floor 30 with this deck and these relics" trajectories using Q1 save/restore. Required to teach the value head before the agent's own exploration is reliable (`scaling-strategy.md` §3 Phase 2).
- **Curriculum scheduling.** Direct workers (via Q11-controlled run configs) toward under-explored regions of `(character × ascension × deck-archetype × encounter)` space.
- **Adversarial scenario generation (Phase 5).** Use the value head's own uncertainty + `observable_run_state + macro_context` (per ADR-015 + ADR-019 v1.1 surface) to find weak spots — synthesize combat states the agent fails on given specific shadow-price configurations across HP / MaxHP / gold / per-potion-slot; feed into Q3 with adversarial provenance tag.
- **Archetype clustering.** Maintain a clustering of "successful decks" used both for diversity bonuses (training) and as labels for an auxiliary archetype-classification task (representation shaping).

Out of scope: scoring states (Q12 / value head); writing to Q1's binary state directly (Q11 uses Q1's load/save API).

## Data Ownership

- **Scenario templates** — checked into the repo. Hand-authored or programmatically generated descriptors of `(character, deck composition, relic set, floor, gold, HP, encounter)`.
- **Archetype clustering definitions** — versioned cluster centers + assignment rules.
- **Coverage state** — `(scenario × outcome)` counters used by the scheduler to identify under-explored regions.

Q11 does not own the trajectories it produces — those are Q3's. Q11 writes them with a provenance tag.

## Communication

- **Sync — Q1 load/save:** Q11 generates engine save blobs corresponding to scenario templates and feeds them to workers as starting states.
- **Async — write to Q3:** trajectories produced from synthetic starts arrive in Q3 with `generator = curriculum` and the scenario template ID stamped in provenance.
- **Sync — read from Q4:** scenario templates reference content by token ID.
- **Sync — read from Q12 / dashboards (Phase 5):** value-head uncertainty maps used to drive adversarial scenario synthesis.
- **Pull — metrics:** Q7 surfaces coverage state and scenario throughput.

## Coupling

- **Afferent (in):** none directly. Scheduler outputs scenario assignments; workers consume them, not Q11 itself.
- **Efferent (out):** Q1 (load/save API), Q3 (write), Q4 (token references), Q12 (Phase 5 uncertainty signal).
- **Indirect:** Git (scenario templates as code).

## Phase Expectations

- **Phase 1.** Not active. Phase 1 trains on natural rollouts only.
- **Phase 2.** Synthetic mid-run states for value-head bootstrap. Coverage-driven scheduler in early form.
- **Phase 3.** Full curriculum schedule across decision types. Archetype clustering operational.
- **Phase 5.** Adversarial scenario generation; held-out content scenarios used for OOD probes.

## Open Risks

- **Distribution drift.** Synthetic states that do not occur on the natural-play distribution can teach the agent things irrelevant to actual runs. Mitigation: train mostly on natural rollouts; synthetic for value-head warmup only; check post-Phase-2 that synthetic-trained agent generalizes.
- **Over-specialization to scenario templates.** If scenario library is small, the agent overfits. Mitigation: programmatic generation; randomized variants of each template.
- **Adversarial scenarios target engine bugs, not strategic weaknesses.** Mitigation: cross-check adversarial states against differential-test parity; flag for review.
- **Coverage bookkeeping is itself a state-management problem** at 10⁸ steps/day scale. Mitigation: probabilistic sketches (Count-Min, HyperLogLog) rather than exact counts.
