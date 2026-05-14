# Micro/Macro Policy Interaction Note

## Summary

The combat ladder should not be framed as "minimize HP loss" beyond Phase 1. In STS2, combat is a run-state transformer: it consumes and mutates HP, potions, energy/stars, card piles, card/relic counters, power state, RNG counters, and reward hooks. The macro-policy should choose whether a fight is worth taking; the micro-policy should choose how to execute that fight under the macro-policy's current resource prices.

The key architecture change is replacing the narrow combat interface:

```text
value(deck, encounter)
```

with a run-conditioned outcome oracle:

```text
evaluate_combat(observable_run_state, encounter_spec, macro_context, budget)
  -> combat_outcome_samples + summary_stats
```

The macro-policy then scores macro choices by composing sampled post-combat states with reward generation and `V_run`, not by adding static room rewards to expected HP loss.

## Source-Grounded Critique

The mechanics reports under `~/development/projects/godot/sts2/docs/mechanics` make the HP-only view too weak:

- The content surface is large: 577 card classes, 295 relic classes, 262 power classes, 64 potions, 68 events, and 88 encounters (`model.md`, evidence index).
- `RunState` includes acts, map, visited coordinates, map history, RNG sets, odds, relic grab bags, modifiers, player states, and extra fields. A combat decision can only be valued correctly against that wider run state.
- `PlayerCombatState` includes hand/draw/discard/exhaust/play piles, energy, stars, pets, and orb queue. Combat output is not just HP.
- Rewards are room-type dependent: Monster gives gold, potion roll, and card reward; Elite adds relic; Boss gives gold, potion roll, and card reward. This directly supports taking higher-damage fights for future value.
- Relics hook into run and combat timing: many override `AfterCombatEnd`, `AfterRoomEntered`, `BeforeCombatStart`, reward modification, merchant pricing, rest options, hand draw, and max energy. Macro choices can activate or preserve these hooks.
- Cards include 117 `Exhaust`, 30 `Retain`, 25 `Ethereal`, 25 `Innate`, and 10 `Sly` cards in the evidence catalog. These create future-state effects that a single HP scalar hides.
- Persistent counters exist on 38 relic classes and 5 card classes. Examples include combat counters, turn counters, elite counters, potion/shop/rest flags, and scaling cards such as `GeneticAlgorithm` / `TheScythe`.
- The reports warn that source state includes hidden/internal fields: RNG counters, future encounter/event queues, action queues, hook-private saved fields, and unpopulated rewards. Policy input must choose an information regime explicitly.

So ADR-009's `value(deck, encounter)` is not enough. It misses HP/max HP, potion slots, relic counters, saved card properties, map pressure, reward modifiers, exact pile/order effects, current relic/power hooks, and uncertainty.

## Resolved Decisions

1. Combat oracle output uses samples first.

   The canonical combat output is a small set of terminal-state samples. Also emit summary stats for pruning and dashboards: survival probability, mean HP loss, HP-loss quantiles, potion-use rates, turn-count distribution, timeout risk, and uncertainty/OOD score.

   Rationale: samples preserve correlations. For example, "low HP but potion preserved" and "higher HP but potion spent" are different states; averaging them destroys the strategic tradeoff.

2. Combat is conditioned by observable run state plus explicit macro context.

   Inputs should include the observable run state, encounter spec, and `macro_context`. The context carries HP shadow price, per-potion shadow prices, risk tolerance, upcoming elite/boss/shop/rest pressure, and search budget.

   Rationale: full run state provides facts; explicit prices prevent the combat head from needing to infer every strategic preference from scratch.

3. Deployed policy is player-observable / belief-state based.

   Hidden source state can be used for simulator correctness, labeling, debugging, and counterfactual analysis, but not as model input for deployed policy. Hidden RNG counters, future encounter queues, unpopulated rewards, and hook-private fields must be excluded from inference features. When hidden state matters, use sampled beliefs.

   Rationale: perfect-source-info training will overstate agent strength and teach choices a player could not justify from visible information.

4. Counterfactuals stay observational.

   Q12 counterfactual rollouts should drive evaluation, curriculum scenario selection, replay priority metadata, and human debugging. They should not become direct supervised targets unless a later ADR proves the estimates are calibrated and not variance-amplifying.

   Rationale: counterfactuals are high-value diagnostics, but direct training on them can amplify simulator variance and value-head bias.

5. Reward valuation stays macro-owned.

   Combat estimates fight costs and terminal combat state. Reward generation and reward choice valuation are composed by macro/reward heads after combat. Combat may expose room-completion facts needed by hooks, but should not bake in reward value.

   Rationale: this avoids double-counting card/gold/relic/potion value and keeps reward-specific learning in the run layer.

## Recommended Interface

```text
CombatOutcomeSample = {
  survived,
  after_combat_observable_state,
  hp_delta,
  potion_delta,
  card_instance_deltas,
  relic_counter_deltas,
  rng_public_belief_delta,
  turns_taken,
  timeout,
  probability_weight
}

CombatOutcomeSummary = {
  survival_probability,
  expected_hp_delta,
  hp_delta_quantiles,
  potion_use_probabilities,
  expected_turns,
  timeout_probability,
  uncertainty
}

evaluate_combat(observable_run_state, encounter_spec, macro_context, budget)
  -> { samples, summary }
```

Macro scoring:

```text
score(path_action) =
  mean_over_samples(
    V_run(apply_room_rewards(sample.after_combat_observable_state, room_type))
  )
```

For non-combat alternatives:

```text
score(rest)     = V_run(apply_rest_site_choice(state))
score(treasure) = V_run(apply_treasure_room(state))
score(merchant) = V_run(apply_shop_plan(state))
score(event)    = mean_over_event_outcomes(V_run(event_result_state))
```

This handles the motivating case directly: a monster, elite, rest, treasure, or merchant node is chosen by expected downstream run value, not by local HP preservation.

## Training Implications

- Phase 1 keeps HP-fraction prediction as the tactical bootstrap target.
- Phase 2+ keeps HP prediction as an auxiliary loss and adds run-conditioned outcome/value training.
- Combat policy should be frozen while early macro heads stabilize, then periodically fine-tuned on the live deck/path distribution.
- Replay must tag decision type, macro context, combat outcome samples/summaries, resource deltas, reward context, and observability regime.
- Evaluation must include tradeoff tests: elite vs hallway, monster vs rest, potion spend vs preserve, low-HP high-upside event/fight choices, and OOD deck/relic combinations.

## ADR-009 Update

Replace:

```text
Combat policy exposes a value(deck, encounter) oracle interface for run-level search to query.
```

with:

```text
Combat policy exposes a run-conditioned outcome oracle:

evaluate_combat(observable_run_state, encounter_spec, macro_context, budget)
  -> combat_outcome_samples + summary_stats.

Run-level search composes these outcomes with reward generation, reward-choice heads, and V_run. Combat HP loss remains an auxiliary prediction target, not the run-level objective.
```

## Follow-Up Spec Work

- Add formal mechanics notes for treasure, merchant, rest site, events, forge, ancient events, orbs, and multiplayer-vote systems before Phase 3.
- Add an observability policy to the state schema: source-perfect fields vs policy-visible fields vs belief-sampled fields.
- Extend trajectory records with decision type, macro context, resource deltas, terminal-state samples/summaries, and post-combat reward context.
- Add Q12 reports for HP-spent-per-reward-value, potion shadow-price calibration, elite-vs-hallway counterfactuals, and observable-input audits.

## Unresolved Questions

None.

---

**Cascaded into canonical specs on 2026-05-14:** see ADR-009 amendment + ADR-014..018 (Accepted) and ADR-019 (Deferred — macro_context derivation policy) in `docs/specs/01-decisions-log.md`; `contracts/schemas/trajectory/trajectory.proto` major bump v0→v1 (package `sts2.q3.v1`); module specs updated for Q1 (`game-simulator.md`), Q3 (`experience-store.md`), Q6 (`evaluation-reports.md`), Q7 (`observability.md`), Q8 (`rollout-workers.md`), Q9 (`inference-server.md`), Q10 (`trainer.md`), Q11 (`curriculum-generator.md`), Q12 (`evaluation-harness.md`); Q2 echo in `docs/specs/modules/oracle.md` + `engine/cpp/docs/q2-architecture.md §12`; observability-regime anchor in `engine/headless/docs/specs/modules/state-codec.md § Field observability tagging`; mechanics-notes backlog tracked in project auto-memory.
