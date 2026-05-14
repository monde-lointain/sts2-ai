# Slay the Spire 2 AI: Strategy from Tactical Prototype to Generalized Agent

**Document type:** Internal engineering roadmap
**Audience:** Senior research+engineering team (RL, systems, evaluation)
**Horizon:** 18-month plan; 12-month committed scope; 6-month gating review
**Owner:** Research Lead (TBD) · **Reviewers:** Systems Lead, Eval Lead

---

## 0. Executive Summary

We have an **expectimax solver** for one A0 combat (`Silent + Ring of the Snake` vs `CULTISTS_NORMAL`), not an RL agent. It is provably optimal on its restricted state space, with a hashable `CompactState`, masked legal actions, transposition table, and chance-node handling for draws. This is a stronger starting point than "we trained PPO on a single fight": the search infrastructure is the *labeling factory* for everything that comes next.

**Recommended architecture:** an **AlphaZero-style ladder for combat** (search produces targets, networks learn priors+value, networks guide deeper search) under a **hierarchical run-level policy** (search-guided meta-policy over map/shop/event/reward decisions, with a learned run-value function). Reject "monolithic end-to-end PPO from pixels": it has no traction here and the source code makes it unnecessary.

**Five staged phases**, each gated by ascension-ladder win rate against a reference battery, with phase-specific compute budgets and abort criteria. We have access to the C# Godot source — we exploit this aggressively for headless rollouts, deterministic seeding, branchable state, and instrumentation. We do **not** reimplement the whole game in C++; we strip Godot dependencies and run the C# Core headless, with C++ reimplementation reserved for the combat hot path if and only if profiling demands it.

**Top three risks (not in priority order):** (1) representational rot on patches — STS2 will receive content updates that invalidate trained agents; we engineer for re-fit, not eternal models. (2) Long-horizon credit assignment — a card pick at floor 3 swings a floor-50 win rate; we mitigate with hierarchy, run-value bootstrapping, and search-augmented training. (3) Hidden state and combinatorial deck/relic interactions — bag-of-counts scales until it doesn't; we plan the representation transition before we hit the wall.

---

## 1. Revised Starting Point Analysis

### 1.1 What the prototype actually solves

Reading the code (`include/sts2/ai/state.h`, `search.h`, `transition.h`):

- **Discrete, deterministic transition function** validated against the game (252 tests), with chance nodes for draws separated from decision nodes (`Phase::kAtChanceDraw`).
- **CompactState** hashable across deck/hand/discard piles via `CardCounts` (bag-of-counts, packed to 8 bits/slot).
- **Expectimax solver** with transposition table keyed on full state (round included — correct, given Ring of the Snake's first-turn draw bonus).
- **Legal-action enumeration with masking** in `transition::legal_actions`.
- **Two-tier scoring**: maximize expected HP; tiebreak on expected rounds. Deterministic per `--seed`. Lexicographic comparison with float-eps.
- **Reproducibility infrastructure**: `tools/seed-pinner` regenerates expected values; deterministic regression battery.
- **Evidence of design taste**: `static_assert` chains tying `CardCounts` indexing to `CardId` enum order (so silent misalignment when adding cards is impossible).

This is genuinely good engineering. The team has internalized determinism, hashing, and search. We build on it; we do not rewrite it.

### 1.2 Hidden complexities that don't appear in the prototype

These are dormant in the small encounter and will *all* surface during scaling:

1. **Position-dependent cards.** The bag-of-counts representation works because no card in the prototype's 4-card deck cares about *order* in the draw/discard piles. STS2 has cards that fetch by position (top of draw, bottom of discard), peek (Foresight equivalents), or rearrange. Bag-of-counts is fundamentally wrong for these. **Migration: ordered piles for cards with positional semantics, multiset for the rest, dual representation behind a typed accessor.**
2. **Play-history dependence.** Cards that scale with copies played this combat (Streamline-likes, Genetic Algorithm-likes, "you played X cards this turn") need history features that don't compose into a multiset.
3. **State on cards in piles.** Upgraded copies, temporary buffs ("ethereal this combat"), retain, innate, snecko'd cost, conjured copies. The card token isn't a `CardId` anymore — it's a `(CardId, modifiers)` instance. Hash/TT keys need rework.
4. **Multi-target and multi-action cards.** AoE, "deal X to ALL", choose-N effects, on-play branching. Action representation must support structured payloads, not just `(card, target)`.
5. **Player-side stochastic effects.** Deal 2-5 damage; gain 0-3 block; random card from set X. These are chance events *inside the player's turn*, not just at draw boundaries. Expectimax must enumerate or sample these.
6. **Power interaction order.** Resolution order of triggered effects (e.g., on-block, on-attack, on-deal-damage, on-take-damage) is finicky in STS1 and will be in STS2. The prototype has cultist-specific code paths; the general case is data-driven and irregular.
7. **Reshuffle indistinguishability.** Right now `apply_draw` reshuffles discard into draw transparently. The legal player choices that *condition* on knowing "did a reshuffle happen?" (relic interactions, retain, end-of-turn discard counts) need to track the shuffle event explicitly.
8. **Energy scaling and one-shot burst caps.** The prototype's energy is a `Stat`. Energy can be debt-style negative (Berserk-likes), gated (Mummified Hand-likes that play follow-ups for 0), or pooled across turns (Heel Hook-likes). Energy is not a number; it is a small expression.
9. **Run-level state is not in `CompactState` at all.** Gold, potion slots, relic-induced combat preconditions, deck composition, current floor, map position, key bosses defeated — none of this exists yet.

### 1.3 Prototype assumptions that *will* break under scaling

| Assumption | Where it's encoded | What breaks it |
|---|---|---|
| 4 unique counted card kinds (`kCountedCardIds <= 8`, packed in `uint8_t`) | `state.h:23-26` | Real decks: 30–50 unique kinds in late-game |
| `EnemyState` as a fixed POD with named fields | `state.h:66-78` | 50+ enemy archetypes, varying buff/debuff stacks, multi-counter intents |
| Two enemies hardcoded (`std::array<EnemyState, 2>`) | `state.h:94` | 1–5 enemies; minions; spawn/despawn |
| Chance nodes only at draw boundary | `Phase` enum | In-turn random outcomes (potions, X-cost cards, scry, conjure) |
| `Score = (expected_hp, -expected_rounds)` | `search.h:12-20` | Run-level: HP at end of *fight* is not the right proxy when downstream fights exist |
| Provably-optimal expectimax tractable | `search.cc` | Combinatorial blowup: deck size + status stacks + multi-target cards |

The first three are **mechanical** — straightforward to refactor with bounded effort. The last three are **architectural** — they require new components.

### 1.4 Where naive RL approaches fail in STS2 specifically

We list these because the team will hear "just throw PPO at it" from outside reviewers:

1. **Reward density.** Win/lose at floor 50 is the *real* reward. A naive shaped reward (HP delta, damage dealt) reliably produces agents that stall to grind block. Anyone who has trained card-game RL has seen this.
2. **Exploration via random play is dead.** Random card pickups produce incoherent decks. Random map paths sidestep elites. There is no "random walk reaches the goal" baseline. We must seed exploration with structured priors.
3. **Off-policy data scarcity.** STS1 has community replay datasets; STS2 likely will not, at least at first. We will manufacture our own data via search and scripted heuristics.
4. **Sample efficiency.** Even at 10⁶ rollouts/day, getting good run-level coverage of all character × deck × relic × map combinations is infeasible without hierarchy and shared representations.
5. **Catastrophic forgetting across characters.** If we train sequentially Watcher → Defect → Ironclad, the network forgets Watcher unless we use either (a) modular per-character heads or (b) IID character sampling with character embeddings. We will need both.
6. **Self-play is the wrong frame.** STS2 is PvE. There is no co-evolving opponent; the game is the opponent and is fixed. The exploration partner is the *curriculum*, not an adversary. (We address this with adversarial scenario generation in Phase 5, but it is a deliberate later addition.)
7. **Exploration via curiosity bonuses leaks into exploits.** RND-style intrinsic rewards push the agent toward unusual states. In STS, "unusual states" includes degenerate infinite loops (Reaper-like card with healing relic). We must detect and disqualify these in evaluation.

### 1.5 Architectural decisions to reconsider before scaling

Before any phase work begins, the team should make three decisions explicitly. Each has a default recommendation, but the team should argue them out and write down the answer.

1. **Representation strategy.** Default: keep `CompactState` for the analytical/regression path, but introduce `RichState` (the representation the network sees) as a separate type that is derivable from `CompactState`. **Rationale:** the network needs token sequences, embeddings, and history features that don't belong in a hashable POD.
2. **Search vs network responsibility split.** Default: AlphaZero pattern — network proposes, MCTS disposes. The expectimax solver becomes the **ground-truth oracle for small states** (used for offline labeling and evaluation), while AlphaZero-style PUCT MCTS becomes the **runtime decision-maker**.
3. **Reimplementation discipline.** Default: do NOT reimplement the full game in C++. Take the C# Core headless, add a deterministic clock, expose state via a binary protocol, and sit Python/PyTorch on top of that via shared memory. Reimplement combat in C++ only if profiling shows we are sim-bound *and* the C# version cannot close the gap. Keep the reimplementation work scoped: per-card mechanics, not whole-game systems.

---

## 2. High-Level Architecture Strategy

### 2.1 The decision: rule-based vs IL vs RL vs planning hybrid

We compare the four naive frames and reject all of them in favor of a hybrid.

| Approach | Where it works | Where it fails for STS2 |
|---|---|---|
| **Rule-based / scripted** | Encounter-specific tactics; tie-breakers; boss openers | Cannot generalize to unseen card/relic combinations; fragile to balance changes |
| **Imitation learning** | Bootstrapping, behavioral cloning from human runs | No high-quality dataset yet; STS2 is new; ceilinged at human skill |
| **Model-free RL (end-to-end)** | Continuous control, dense rewards | Sparse reward; long horizon; large discrete action space; combinatorial deck state |
| **Pure planning (search)** | Combat with bounded state; deterministic mechanics | Combinatorial blowup; intractable for run-level; useless for events/shops without a value function |

**Chosen architecture: staged hybrid.** The components and their responsibilities:

- **Combat tactical policy.** AlphaZero-style PUCT MCTS over the combat MDP. Network outputs (a) prior over legal actions, (b) outcome samples + summary stats (the run-conditioned combat outcome oracle per ADR-014); HP-fraction-at-end-of-combat is kept as an auxiliary prediction target, not the run-level objective. Trained on (state, search-improved policy, search-improved outcomes) triples. Search uses a learned value function as the leaf evaluator; expectimax remains the gold-standard verifier for tractable sub-states.
- **Run-level meta-policy.** Hierarchical: a strategic planner that operates over "decision points" (card pick, map fork, shop visit, event choice, rest site, potion use). Combat outcomes are wrapped as macro-actions: the meta-policy queries the combat policy via `evaluate_combat(observable_run_state, encounter_spec, macro_context, budget)` and composes returned outcome samples through reward generation + `V_run` (per ADR-014, ADR-015, ADR-018). `macro_context` carries shadow prices (HP, per-potion), risk tolerance, and pressure indicators.
- **Card / relic / event evaluators.** Specialized value heads. The card-pick head scores `(deck, relic_set, floor, character, candidate_card)` → expected run-value delta. Same shape for relic offers, potion offers, event branches.
- **Search augmentation at runtime.** At decision-time, the meta-policy runs short MCTS rollouts to *the next combat resolution* using the run-conditioned combat outcome oracle (ADR-014), optionally with the rest of the run *imagined* via a learned world model.
- **Opponent modeling.** Not adversarial; we model *encounter generation* and *card/relic offer distributions*. These are stationary modulo balance patches; we estimate them once per patch from sim rollouts.

### 2.2 Why staged hybrid (and not "just AlphaZero everything")

AlphaZero works in chess and Go because (a) the action space is small per ply, (b) the horizon is bounded, (c) there is a single shared evaluator for any position. STS2 violates all three at the run level. So we use AlphaZero *where it fits* (combat, with bounded plies and shared evaluator) and a different stack at the run level. The components share *representations* (card/relic/enemy embeddings) but not *algorithms*.

This split also means we can ship a strong combat agent in Phase 1 even before the run-level stack exists, and validate the bottom of the stack against the existing expectimax oracle.

### 2.3 How source code access changes the optimal architecture

The team should treat source access as a *force multiplier on simulation throughput*, not as a license to reimplement. Specifically:

- **Headless build** of the C# Core (compile out Godot scenes, audio, animation; replace `Engine.GetMainLoop()` with a manual deterministic loop). Target: 1k–10k combat steps/sec/core, single-threaded.
- **Branchable state** via serializable `CombatState` + `MapPointState` + run save. The `CombatStateTracker` and `History` directories under `Core/Combat` suggest this already exists for replays. We reuse it.
- **Hooks** at every player-decision boundary. The `Core/Hooks` and `Core/GameActions` directories indicate an event-driven architecture. We attach our policy as a hook handler and never modify game logic.
- **Determinism audit.** STS2's `Core/Random` is the only source of stochasticity if we headless-out animation timing. We seed it explicitly per rollout.
- **Mod-shaped extension.** `Core/Modding` is a clean injection point for our agent, evaluation harness, and curriculum tooling. We build the agent as an out-of-tree mod where possible to avoid rebasing on every game patch.

`Core/AutoSlay/AutoSlayer.cs` is suspicious in a useful way: it suggests Megacrit already has an automation/test harness internally. Lift it. Its `Handlers` and `Helpers` likely encode the exact action surface we need.

### 2.4 Whether to prioritize model-free, model-based, planning, or hybrid

Per component:

- **Combat:** model-free networks (policy + value) used inside model-based search (MCTS over the deterministic transition function). The transition function is exact and free — there is no model to learn for combat dynamics. Only the policy and value are learned.
- **Run-level:** initially planning + learned value (search over short horizons with neural leaf evaluator). Phase 5 adds a learned world model for longer-horizon imagination, gated on whether the imagined model actually helps win rate.
- **Single decisions (events, shops, card picks):** myopic neural value head with a 1–3 step rollout for nontrivial cases.

We do not need a learned model of card mechanics. We have the source. Use it.

### 2.5 Modular decomposition

```
                    ┌────────────────────────────────────────┐
                    │       Run-Level Meta-Policy            │
                    │  (decision-point router; HRL)          │
                    └─────┬──────────────┬───────────────┬───┘
                          │              │               │
              ┌───────────▼──┐  ┌────────▼─────────┐  ┌─▼──────────────┐
              │ Map/Path     │  │ Card/Relic/      │  │ Event/Shop/    │
              │ Planner      │  │ Potion Evaluator │  │ Rest Decider   │
              └─────┬────────┘  └────────┬─────────┘  └─┬──────────────┘
                    │                    │              │
                    └─────────┬──────────┴──────────────┘
                              │ shared latent run state
                    ┌─────────▼──────────┐
                    │  Run Encoder        │
                    │  (transformer over  │
                    │   floor, deck,      │
                    │   relics, gold,     │
                    │   path, history)    │
                    └─────────┬──────────┘
                              │
                ┌─────────────▼─────────────────┐
                │ Combat Policy (AlphaZero-style)│
                │   - PUCT MCTS                  │
                │   - prior π(a|s)               │
                │   - evaluate_combat → outcome  │
                │     samples + summary stats    │
                │     (HP-fraction = aux target) │
                └─────────────┬─────────────────┘
                              │
              ┌───────────────▼───────────────┐
              │  Shared Token Embeddings:      │
              │  cards, relics, enemies,       │
              │  buffs, intents, events        │
              └───────────────────────────────┘
```

The arrows are dependency, not control flow. Inference flows top-down at decision time, but training flows bottom-up: combat must work alone before card-pick training is meaningful, and card-pick must work before run-level routing is.

### 2.6 Shared representations

We commit to a single embedding table for game tokens, used by every component:

- **Tokens:** every card (with upgrade variant as a separate token; Snecko-cost is a feature, not a token), every relic, every enemy archetype, every buff/debuff, every event id, every potion. Output dim 128 (Phase 1) → 256 (Phase 4).
- **Cards in hand/draw/discard/exhaust:** transformer over an unordered set of card-tokens with multiplicity-aware positional encoding. We do NOT use a fixed-length one-hot; the deck size grows.
- **Enemy state:** transformer over a sequence of enemy-tokens, each augmented with HP/block/intent/buff feature vectors.
- **Map:** GNN over the act DAG. Each node has its room-type embedding + visited mask + reachable mask. Output a per-node embedding consumed by the path planner.
- **Run history:** transformer over a compressed event stream (each prior decision summarized as ~16 floats). Caps the context to ~256 events.

The argument for one shared embedding table (rather than per-component) is twofold: (1) gradient signal flows from every objective into the same tokens, multiplying effective sample count; (2) it makes character/archetype transfer easier in Phase 4, because shared tokens (Strike, Defend, common relics) carry their meaning across heads.

### 2.7 Memory and patch adaptation

- **In-run memory:** a learned summary updated at each decision point, fed back as context. Implementation: an LSTM/GRU over decision-point embeddings, OR a transformer with growing context. We start with GRU (cheaper, simpler) and revisit if attention helps.
- **Across-run memory:** none. Every run starts fresh modulo character/ascension.
- **Patch adaptation strategy:** maintain a *content-token table* with stable IDs across patches when cards are unchanged, new IDs when cards are added, and a *deprecation* mechanism for removed cards. Embeddings for new cards are initialized from a "card description" subnetwork that consumes structured card text (cost, type, effects-as-DSL). This is the closest we get to zero-shot patch adaptation. Even with this, expect 1–3 weeks of fine-tuning per major patch and budget for it.

### 2.8 Observability regime (per ADR-016)

Q1's serialized state contains hidden source state (RNG counters, future encounter/event queues, action queues, hook-private saved fields, unpopulated rewards). Each field in Q1's emitted state schema carries one of three observability tags:

- **`SOURCE_PERFECT`** — hidden from the player; usable only for simulator correctness, labeling, debugging, counterfactual analysis. **Never** in deployed-policy inference inputs.
- **`POLICY_VISIBLE`** — fully player-observable; safe for deployed inputs.
- **`BELIEF_SAMPLED`** — hidden from the player; deployed policy reads a sampled belief over its value (e.g., posterior over draw-pile ordering), never the true value.

Q1 emits all fields; Q8/Q9/Q10 filter at the inference boundary via the tag manifest co-located with the state schema. Q12 evaluation harness enforces no-hidden-state-leak audits as part of every gate evaluation. Training on perfect-source-info overstates agent strength and produces exploit-shaped policies — this regime exists to prevent that drift structurally.

---

## 3. Progressive Development Roadmap

Each phase has hard gating metrics. **Do not advance to the next phase until the gate is met.** Phase work that bleeds into the next phase ahead of its gate is technical debt that compounds.

### Phase 1 — Generalized Tactical Combat (current → broad)

**Duration:** 3–4 months. **Compute:** modest — 1 GPU box for training, 64–128 CPU cores for rollouts.

**Goals.** Train a combat policy that wins ≥95% of A0 normal encounters across all four (or however many) STS2 starter decks, with full-deck and full-relic-but-no-potions variation, on held-out random seeds.

**Environment modifications.**
1. Headless C# Core build. Strip Godot rendering, animation, audio. Replace any `await ToSignal(...)` patterns with synchronous deterministic completion.
2. Combat-only entry point that takes `(seed, character, deck, relics, encounter_id, ascension)` → final state. No map, no rewards screen, no inter-combat code paths.
3. Branchable state: serialize `CombatState` to a binary blob; deserialize identically. Test with deterministic round-trip: 10⁵ random states, save/restore, replay one action, compare outcomes. Bit-identical or you have a determinism bug — fix it before training.
4. `RichState` derivation from in-engine state. Stable serialization version; bumped on schema change.
5. Hookpoint at every player-decision boundary. Hook returns an `Action`; engine validates and applies.

**Training topology.**
- 1 trainer (GPU) ↔ 1 replay buffer (Redis or RocksDB) ↔ N rollout workers (CPU).
- AlphaZero-pattern: workers run MCTS using current network weights (cached locally, refreshed every K rollouts), submit (state, search-policy, search-value) triples to replay.
- Trainer samples uniformly with optional prioritization on `(value-target, predicted-value)` mismatch.

**State/action representation.**
- State: `RichState` → token sequence as in §2.6. ~128–256 tokens typical, padded.
- Actions: structured `Action` records — `(kind, card_token_index, target_index, payload)`. Action mask returned from the engine alongside the legal-action list.
- Action embedding: same token table; the *played card* is the action embedding seed, augmented by target embedding.

**Architecture.**
- Encoder: 6–8 transformer blocks, 128–256 dim, ~10M parameters. We are not training a frontier model; we are training a tactical policy that runs at high throughput inside MCTS. Smaller is better.
- Heads: policy (over masked legal actions), value (scalar in [0, 1] = HP-fraction-at-end-of-combat).
- Training loss: standard AlphaZero — cross-entropy on policy, MSE on value, L2 weight decay, with KL penalty against the network's own prior to stabilize.

**Data generation.**
- 100% self-rollout (search-vs-environment). No imitation data needed yet.
- Curriculum: start with the existing 1-encounter prototype as a regression seed, then expand by encounter difficulty, then by deck variation, then by relic variation. Use the **expectimax solver as oracle on small states** — when the network's policy disagrees with the solver on a state the solver can fully expand, log it, sample it at higher weight.

**Evaluation criteria (gate to Phase 2).**
- ≥95% win rate against the full A0 normal encounter pool, all starter decks, no relics beyond starter, no potions, on 10K held-out seeds.
- ≥90% agreement (top-1 action) with expectimax oracle on all states the oracle can fully solve in <1s.
- Inference latency: <100ms per decision at search budget = 64 simulations on a single CPU core. Search budget tunable up to 1024 with linear latency.

**Expected bottlenecks.**
- Sim throughput. Goal: 5k combat-steps/sec/core after headless port; we will hit 500/sec/core first and have to optimize.
- MCTS overhead. Action enumeration, state hashing, and value head invocation must be cache-friendly.
- Reproducibility breakage. Every time someone touches the engine, determinism regresses. Continuous regression suite required from day 1.

**Failure modes.**
- Network learns to stall (block-heavy degenerate). Mitigation: penalize round count past a deck-aware budget in the value target.
- Network exploits a bug in the headless port. Mitigation: differential testing — random rollouts in the headless build vs. the original Godot build, compare final states, alarm on divergence.
- Catastrophic forgetting on early encounters. Mitigation: replay buffer with stratified sampling by (encounter, deck-archetype-bucket).

**Scaling risks.**
- Self-play deadlock: workers stall waiting for new weights; weights stall waiting for replay; replay stalls waiting for workers. Standard fix: independent rate-limiters, but it will bite at first.
- TT vs MCTS interaction: MCTS already implicitly memoizes; do not also use a TT inside the network calls. Pick one.

**Tooling.**
- RL framework: **EnvPool + custom MCTS** in C++/PyTorch, OR **TorchRL** with custom rollout. We recommend a custom MCTS: existing libraries are over-general and undertuned for our action mask shape. Reuse the prototype's transposition table for state hashing; the data structure is correct.
- Replay: RocksDB or LMDB on local NVMe. Avoid distributed replay until throughput demands it.
- Experiment tracking: Weights & Biases or MLflow. Pick one and force every experiment through it.

### Phase 2 — Deck Adaptation and Build Strategy

**Duration:** 3–4 months. **Compute:** 1–2 GPU boxes; rollout cluster grows to 256–512 CPU cores.

**Goal.** Train a card-pick policy that, when paired with the Phase 1 combat policy, achieves ≥70% win rate on full A0 runs of one character. (We pick one character first — recommend Silent if it remains in STS2, else whichever character has the largest move set in current code, since variety helps generalization.)

**What this phase teaches the agent.**
- Reading card rewards in context: deck composition, current relics, current floor, expected upcoming encounters.
- Archetype recognition: "this deck is going wide on poison" vs "this deck wants to scale a single card." Emerges from learned latent-deck representations.
- Resource valuation: when does +HP beat +card; when does +1 max HP at floor 3 beat +5 gold.

**Environment modifications.**
1. Run-level rollout entry point: `(seed, character, ascension)` → full run. The engine now drives map, encounters, rewards, shops, events end-to-end, querying our hook for player decisions.
2. Two hooks: `combat_decision` (Phase 1) and `meta_decision` (Phase 2).
3. Card reward instrumentation: at each card-pick decision point, log `(run_state, candidate_cards)` and the eventual run outcome.

**Training topology.**
- Separate the combat policy (frozen by default in Phase 2) from the meta-policy. Train only meta-policy initially.
- Two-headed loss: card-pick (categorical over offered cards + skip), value (scalar = run-outcome estimate).
- Eventually unfreeze combat policy for joint fine-tuning, but only after the meta-policy has stabilized — otherwise the combat policy will chase a moving target.

**State/action representation.**
- State adds: full deck embedding (transformer over deck), relic set embedding (set transformer), floor index, gold, max HP, current HP, potion slots (typed, may be empty), map context (next 3 floors visible), run history summary (GRU output).
- Card-pick action: `Pick(card_idx)` or `Skip`. Mask Skip if Singing Bowl-likes are present. Cards offered are 3 of N (N grows over the game).

**Architecture.**
- Reuse encoder from Phase 1 with new context tokens.
- Card-pick head: dot product between candidate-card embedding and a context-conditional query vector. This handles variable-arity offers naturally.
- Run-value head: MLP from run-state encoder.

**Data generation.**
- **Self-play runs** with frozen Phase 1 combat policy and learning meta-policy.
- **Off-policy decision injection**: at random decision points, force-pick a random offered card to explore the value landscape. Annealed from high to low through training.
- **Synthetic curriculum**: start from non-floor-1 states with hand-crafted "good decks" and "bad decks" to teach the value head before exploration is reliable.

**Evaluation criteria (gate to Phase 3).**
- ≥70% win rate on A0 full runs of one character, 10K held-out seeds.
- Card-pick top-1 agreement with high-skill human reference (if available) ≥60% on a labeled set.
- Calibration: predicted run-value within ±10% of empirical win rate, bucketed by predicted-value decile.

**Expected bottlenecks.**
- **Long horizon credit assignment.** A card pick at floor 2 affects outcome at floor 50. Fixes: bootstrap value targets from intermediate run-state evaluations (TD-lambda style); use search at decision time so the value head doesn't have to do all the work.
- **Sample efficiency.** A full A0 run is ~100 decisions; one trajectory yields ~100 (state, action, return) tuples. To cover the offer-distribution we need millions of runs. Plan compute accordingly.

**Failure modes.**
- Meta-policy learns to skip every card (monocultural deck). Mitigation: monitor deck-size distribution; flag if mean drops too far from human baselines.
- Meta-policy specializes to one archetype. Mitigation: archetype diversity bonus during training; broader curriculum.
- Combat policy degrades when meta-policy explores unusual decks (out-of-distribution combat states). Mitigation: continuous evaluation of combat policy on the live deck distribution.

### Phase 3 — Full Run Planning

**Duration:** 4–5 months. **Compute:** 2–4 GPU boxes; 512–1024 CPU cores.

**Goal.** Map routing, rest sites, shops, events, potion conservation, gold management — the full A0 run is now end-to-end controlled by the agent, not just card picks. Target ≥85% win rate on A0 of the lead character.

**What this phase adds.**
- Map planning: choose paths through the act graph weighted by expected reward and combat risk.
- Shop policy: when to spend, what to buy.
- Event policy: branch choices conditioned on run state.
- Potion policy: hold vs. use, target selection in combat.
- Rest site policy: rest vs. upgrade vs. character-specific actions (smith, dig, etc., as applicable to STS2).

**Hierarchical structure becomes mandatory here.** A flat policy over all decision types is feasible to implement but pedagogically muddled. Use:

- **Manager (run-level):** decides "next significant decision is type X" only by virtue of where the engine is — this is not really a learned manager, it's just dispatch.
- **Workers (per decision type):** specialized heads sharing the encoder. Map-path head, shop head, event head, potion-use head, rest head, card-pick head (from Phase 2), combat policy (from Phase 1).
- **Shared run-value function:** all workers query a single `V(run_state)` for their value computation; this is what ties them together.

**Environment modifications.**
1. Hooks at all decision points. The C# engine should *only* progress when a hook returns an action.
2. Counterfactual rollouts: starting from a saved state at floor F, run K alternative continuations under different policies and aggregate. Required for offline evaluation of map decisions.
3. Faithful event simulation: events with stochastic outcomes (e.g., gambling-likes) must use seeded RNG that we control.

**Training topology.**
- Separate replay buffers per decision type (so we can balance training data across heads).
- Joint training of all worker heads + value function.
- Combat policy mostly frozen, periodically unfrozen and fine-tuned on the live distribution.

**State/action representation.**
- State adds: full visible map with node-type and reachability, gold, current floor, expected encounters per remaining act, potion slot contents.
- Map action: discrete over reachable next-nodes (≤3 typical).
- Shop action: structured: buy(item) for each affordable, remove(card) if affordable, skip.
- Event action: discrete over event branches; many events have side-effect previews we must encode.
- Potion-use: timing decision (combat-internal, integrated into combat policy via an action extension).

**Search augmentation.**
- At map decisions: enumerate all path completions to next boss, score each by `mean_over_samples(V_run(apply_room_rewards(sample.after_combat_observable_state, room_type)))` (per ADR-014, ADR-018) — i.e., compose the combat outcome oracle's samples through reward generation and the run-value head, NOT by summing scalar expected HP loss.
- At shop: enumerate purchase combinations under budget (small combinatorial; brute-force or beam search), evaluate each via deck-update-rerun-value-head.
- At events: most events have ≤3 branches; evaluate all.
- At rest sites: 2 options usually; evaluate both with quick value-head queries.

**Evaluation criteria (gate to Phase 4).**
- ≥85% A0 win rate on lead character, 10K held-out seeds.
- ≥70% A10-A15 win rate on lead character.
- Map decision agreement with expected-value-maximizing oracle (computed offline) ≥80%.
- No degenerate behaviors: agent does not infinite-loop in any combat; agent does not always-skip card rewards; agent does not stockpile potions to floor 50.

**Expected bottlenecks.**
- **Search depth.** Run-level search to next boss is 6–10 decisions deep, branching ≈3 at maps and ≈3 at events. This is tractable. Search to end of run is not. Use neural value as cutoff.
- **Worker imbalance.** The map worker runs 50× more often than the rest-site worker. Without rebalancing, gradients lopside.
- **Potion timing.** Potion use is in-combat; integrating it into combat policy means combat action space grows. Action mask gets larger; representation must include potion slot tokens. Plan the schema change explicitly.

**Failure modes.**
- Value function over-confident on early-floor states (where information is thin). Mitigation: train value with explicit aleatoric uncertainty head; use it for search-budget allocation.
- Agent learns to early-game-snowball at the expense of resilience (loses to elites in act 2). Mitigation: counterfactual evaluation against alternative paths; reward shaping that values robust HP buffers.

### Phase 4 — Multi-Character Generalization

**Duration:** 3–4 months. **Compute:** 4 GPU boxes; 1k+ cores.

**Goal.** Lift the lead-character agent to all characters with character-specific specialization but shared learning.

**Architecture choices.**
- **Shared encoder.** Cards, relics, enemies, events all share token embeddings.
- **Character embedding** as a context token, prepended to state encoding.
- **Per-character heads** for combat policy, plus a **shared head** trained on multi-character data. At inference, the per-character head is used; the shared head is the fallback for low-data characters.
- **Mixture-of-experts** for card-pick value, with character-conditional gating. We try this only after the simpler shared-head approach plateaus.

**Curriculum scheduling.**
- Round-robin character training, with sample weight inversely proportional to character data count to combat data imbalance early.
- "New character" warmup: pretrain a new character's head on shared-encoder representations from existing characters, then unfreeze with low LR.
- Anti-forgetting: maintain an "old run" replay slice (5–10% of every batch) drawn from prior characters' best-runs distribution.

**Evaluation criteria (gate to Phase 5).**
- ≥80% A0 win rate on every character, 10K held-out seeds.
- ≥60% A10 on every character.
- No more than 5% win-rate degradation on any character after a new character is trained.

**Failure modes.**
- Character bleed: a card with the same name but different mechanics across characters (rare but possible) corrupts shared embeddings. Mitigation: per-character token IDs for any card whose mechanics depend on character context.
- Gradient interference: jointly trained character heads fight each other on shared parameters. Mitigation: PCGrad or cosine-similarity gradient projection if observed.

### Phase 5 — Generalized Superhuman Agent

**Duration:** 6+ months and ongoing. **Compute:** scaled to need; expect 8 GPU boxes + several thousand CPU cores during active push.

**Goals.**
- Beat highest-ascension on every character at >50% rate.
- Robust to unseen card/relic/event combinations (held out from training).
- Patch-adapt within 2 weeks of a balance change with bounded compute (no training from scratch).

**Architectural additions.**
- **MCTS hybrids at higher decision levels.** AlphaZero-style search at run-level decisions (not just combat). Tractable because we have a strong value function from Phase 3.
- **Learned world model** for run-level imagination: given a run state and a sequence of actions, predict run state K decisions ahead. This is the analog of MuZero's world model. Train on real run trajectories; gate on whether imagined rollouts improve decisions vs. simulator rollouts.
- **Trajectory search**: at decision time, search over `(map_path, card_picks)` tuples over the next act, evaluate via the run-conditioned combat outcome oracle (ADR-014) composed through `V_run` (ADR-018). Beam search; pruning by gradient-of-value.
- **Distributed self-improvement loop**: continuous training pipeline with auto-evaluation, regression detection, and rollback.

**Hardest sub-problems in Phase 5.**
- **Out-of-distribution combinations.** A held-out card × relic combo can produce dynamics the training distribution never saw. Mitigation: train on randomized scenarios that include compositional novelty (e.g., random subset of cards with random subset of relics).
- **Patch adaptation.** A balance change can flip a card from S-tier to C-tier. The card-text subnetwork is the main lever; if it generalizes well, fine-tuning is cheap.
- **Exploit detection.** As ascension increases, the agent will find degenerate strategies that exploit code edge cases. We need an exploit detector — see §4.6.

**Evaluation criteria (ongoing).**
- Held-out content win rate within 5% of training-distribution win rate.
- Patch fine-tune <2 weeks compute.
- Inference latency <500ms per decision at full search budget.

---

## 4. Technical Deep Dives

### 4.1 Simulator and Environment Engineering

The simulator is the first-class component of this project. Throughput here multiplies every other capability.

**Concrete deliverables, in order:**

1. **Headless C# Core build.** Goal: eliminate all Godot-rendering, animation, audio, and main-loop dependencies. Compile against `Godot.NET` only for type compatibility, run on .NET runtime directly. Replace `Engine.GetMainLoop()` patterns with a deterministic step driver.
2. **Deterministic clock.** A single `IClock` interface, injected; rollouts use `DeterministicClock` that ticks on demand. No `DateTime.Now`, no `OS.GetTime()`. Audit and replace.
3. **Seeded RNG plumbing.** Single `RandomService` (likely already exists in `Core/Random`); every consumer takes it via DI. Verify by setting seed and replaying a run twice — bit-identical states required.
4. **Save/restore primitives.** `CombatState.Serialize() -> byte[]`; `CombatState.Deserialize(byte[])`. Likewise `RunState`. Round-trip test in CI: serialize, deserialize, take one action, compare against control. Identical or fail.
5. **Branchable rollouts.** From any saved state, execute K alternative actions. This is what MCTS needs. Implementation: snapshot state, execute, restore, repeat. Memory cost is the per-state size; budget for it (~10–100KB/state).
6. **Hook protocol.** A binary IPC between C# engine and Python policy via shared memory or Unix sockets. Latency target <50µs per decision. We benchmark this early; if slow, switch to in-process via Python.NET or embed the policy network in C# via ONNX Runtime.
7. **Differential test harness.** Random rollouts in the headless build vs. the unmodified Godot build. Compare final HP, final deck, final gold. Any divergence = headless port bug.
8. **Replay system.** Every rollout produces a replay file (seed + decisions). Replays are truth-checkable via differential test. Rollout failures are debuggable from replay alone.
9. **Distributed simulation workers.** Each worker is a single C# process with one or more rollout slots. Workers expose a simple "run-task" RPC. Workers stateless apart from network weights cache. Standard worker-pool pattern.
10. **Instrumentation hooks.** Per-decision logs of state, legal actions, chosen action, search statistics. Streamed to a sink (Kafka or Redis Streams) for offline analysis.
11. **Validation harness.** A battery of fixed-seed runs whose final states are pinned (analogous to `tools/seed-pinner` in the prototype). Run on every CI commit.

**Throughput target.** 10⁸ combat-steps/day on a 1024-core fleet by end of Phase 3. A combat is ~30 player decisions; that's 3×10⁶ combats/day, ~10⁵ A0 runs/day on 50-floor runs. This is ambitious but achievable if (a) the headless port is clean and (b) we don't reinvent every wheel.

### 4.2 State Representation

**Don't conflate `CompactState` (verifier representation) with `RichState` (policy input).** Two types, two purposes. The hashable canonical state is for analytical search and TT; the rich state is for the network.

**Token table** (single shared embedding):
- All cards (per upgrade variant as a separate token; cost-modified copies handled via a feature, not a token, unless the rules text changes).
- All relics.
- All enemies (per ascension variant if stats differ enough).
- All buff/debuff types.
- All potions.
- Special tokens: `[CLS]`, `[SEP]`, `[MASK]`, `[CHAR_*]`, `[ACT_*]`.

**Combat state encoding.**
- Hand: ordered sequence of card tokens, each with cost and any per-instance modifiers as a feature vector.
- Draw / Discard / Exhaust: bag-of-counts representation augmented with positional cards' positions (an extra small ordered list for "top of draw" etc.).
- Player: vector of HP, max HP, block, energy, gold, plus each active power as `(power_token, stack_count)`.
- Enemies: sequence of `(enemy_token, hp, max_hp, block, intent_token, intent_value, powers...)`.
- Combat metadata: turn count, deck composition snapshot at start.

**Run state encoding.**
- Character token + ascension level.
- Floor, gold, current/max HP, potion slots.
- Deck: bag-of-counts (multiset transformer) + count.
- Relics: set transformer over relic tokens.
- Map: GNN over visible nodes; node features include type, depth, visited-mask, on-current-path-mask.
- History: GRU or small transformer over past decision summaries (~16 floats each).

**Trade-offs:**
- Flat feature vectors are cheapest but break on variable-arity (deck size, enemies, cards in hand). We use them for fixed scalars (HP, gold, floor) and nothing else.
- Transformers are strong on token sets and natural for our domain; they handle deck-size variation.
- GNNs are right for the map (sparse adjacency, varying topology).
- Hybrid neural-symbolic representations (e.g., combining a neural state encoder with a symbolic deck composition tag) may help in Phase 4–5; not an early-phase priority.

### 4.3 Action Space Design

**Hierarchy of actions:**
- **Macro:** "next decision is map vs shop vs combat" (decided by engine, not by us).
- **Decision-type-specific:** within a decision type, the action is a small structured object.
- **Combat:** `(card_idx, target_idx, payload)` where payload covers card-specific choices (e.g., choose-N selections).

**Combinatorial card sequencing during a turn.** A turn is a sequence of card plays, but we treat each play as a *step* in the combat MDP rather than the whole sequence as one action. This keeps the action space small per ply and lets MCTS reason about ordering. End-turn is a distinct action.

**Targeting.** For multi-target cards, target is a list. We handle this via either:
- Sequential targeting: each target picked as a sub-action (clean but doubles ply count).
- Joint targeting: action representation includes an array of target indices (smaller ply count, larger action space per ply).

We recommend sequential targeting for simplicity, accept the ply cost.

**Stochastic player decisions** (X-cost cards, "deal 2-5 damage", random card from set). We do NOT learn the distribution; the simulator owns it. Stochastic outcomes appear as chance nodes in the MCTS tree, exactly like the existing prototype handles draws.

**Search-space pruning.** A single turn can have huge legal-action sequences. We prune:
- Dominated card plays (e.g., never play Strike before Defend if Defend changes draw outcome — but this rule is brittle; prefer search-derived pruning).
- Action equivalence: the engine should canonicalize action ordering where outcomes are commutative (e.g., target-symmetric AoE vs. asymmetric).
- Move-grouping in MCTS: group equivalent actions at search time to reduce branching.

**Macro-actions.** We considered learning macro-actions ("clear minions", "set up scaling"). We reject this for early phases — the action space is already discrete and small per ply, and the policy network is the macro-action analog. Revisit in Phase 5 if hierarchical search struggles.

### 4.4 Reward Design

**The terminal reward is run outcome.** Win = 1, loss = 0. Anything else introduces shaping bias. We use HP-based intermediate rewards only in Phase 1 (combat-only training), where the run-outcome doesn't apply.

**Phase 1 (combat-only) rewards.**
- Terminal: end-of-combat HP fraction normalized to a target. Targets are deck/encounter-aware; we don't reward 100% HP on a free encounter.
- No per-step reward. The value head learns to predict end-of-combat HP fraction directly.

**Phase 2+ (run-level) rewards.**
- Terminal: 1 if run won, 0 if lost. Optionally scaled by floor reached (so partial-progress runs distinguish).
- Combat-internal reward: end-of-combat HP fraction, used as the local target for the combat policy when the run-value head is unavailable.
- We avoid shaping rewards for "good intermediate states" (e.g., gold accumulated). They cause local optimization traps. Run-value is learned, not engineered.

**Pitfalls explicitly named:**
- **Local survival traps.** Reward shaping that values HP can produce stalling agents. Mitigation: include round penalty, deck-aware budget.
- **Score optimization vs. survival.** STS rewards finishing the run; per-floor "score" is irrelevant. Train on win/loss, not score.
- **Degenerate loops.** Some card+relic combos can produce infinite-loop states (Reaper-like + healing relic, +0-cost spam). The engine must terminate combats with a turn budget; the value function must penalize timeouts.
- **Counterfactual rewards.** It is tempting to reward "what would have happened if you'd skipped this card." We resist — counterfactual evaluation is for offline analysis, not online training signal.

### 4.5 Data Generation Strategy

**Sources, ranked by phase:**

1. **Self-rollout via search.** AlphaZero-style: MCTS-derived policy is the training target. Phase 1+.
2. **Scripted heuristic baselines.** Phase 0–1 only: a hand-written "play Strike, play Defend if HP low" baseline gives us coverage of the state space before the network can. Discarded once the network outperforms.
3. **Curriculum-generated states.** Synthetic mid-run starting states ("you are at floor 30 with this deck; survive"). Uses the engine's save/load to inject. Critical for Phase 2 (teach the value head before exploration is reliable).
4. **Adversarial scenario generation.** Phase 5: synthesize combat states the agent fails on, train on them. Uses the value head's own uncertainty to find weak spots.
5. **Human replay ingestion.** If/when STS2 community datasets exist. Lower priority; supplemental for archetype seeding.
6. **Offline RL datasets.** A pinned, versioned dataset of past trajectories used for new-architecture training and sample-efficiency research. Rebuilt every patch.

**Prioritized experience replay.** Standard formulation; priority = TD error. Bounded priority floor to avoid neglecting easy transitions.

**Distributed experience collection.** Workers emit trajectories to a Kafka topic; a small ingest service writes to RocksDB shards. Trainer reads from the active shard plus a stratified slice from the history.

### 4.6 Evaluation Harness

**Metrics, prioritized:**

- **Win rate** by character × ascension × encounter pool. The headline metric.
- **Floor reached** distribution. Sub-headline; useful when win rate is too coarse (early training).
- **Damage taken per encounter** by encounter type. Detects combat-policy regression.
- **Deck composition entropy** by floor. Detects archetype collapse.
- **Inference latency** distribution per decision type. Detects regressions in serving.
- **Search budget vs. quality.** Curve of win rate vs. MCTS simulations per decision; should be monotonic and saturate.
- **Adaptation speed.** After a content patch, days/compute to recover within 5% of pre-patch win rate.
- **Robustness to RNG.** Variance of win rate across 10K-seed batches. Lower is better but only after mean is high.
- **Exploit incidence.** Number of detected exploit-class behaviors per 10K runs. Should be 0 or trending toward 0.
- **Calibration.** Predicted run-value vs. empirical win rate by predicted-value decile.
- **Tradeoff tests.** Targeted scenario suites that exercise the macro/micro composition (per ADR-014..018): elite-vs-hallway path choice, monster-vs-rest stop choice, potion-spend-vs-preserve under combat pressure, low-HP high-upside event/fight choices, OOD deck/relic combinations. The point is to verify the agent makes the *strategically correct* tradeoff, not just survives.
- **`macro_context` shadow-price calibration.** Predicted HP / potion shadow prices vs. realized run-value lift per unit resource. Detects miscalibrated macro→micro composition.
- **Observability-regime audit.** No `SOURCE_PERFECT` field appears in any deployed inference input (per ADR-016). Run on every checkpoint promotion.

**Eval harness components:**

1. **Pinned regression battery.** Fixed (seed, character, deck, encounter) tuples with known ground-truth (often expectimax-derived) optimal policies. Run on every commit. Any deviation alarms.
2. **Held-out seed pool.** 100K seeds reserved, never trained on. Used for final evaluation per phase.
3. **Ascension ladder.** A0, A5, A10, A15, A20 for each character. Win rate at each.
4. **Counterfactual evaluator.** For map decisions, compute the value of the not-taken path and compare. Surfaces map-policy deficiencies.
5. **Exploit detector.** Monitors for: combats lasting >50 turns, gold/HP loops, repeated identical action sequences, infinite-block states. Flags runs for human review.
6. **Human-comparable benchmarks.** A small set of curated runs with expert-annotated "correct" decisions. Used as a sanity check on the agent's decision-making, not as a primary metric.

**Versioning.** Every evaluation report is tagged with (game version, agent version, eval-suite version). Cross-version comparisons require explicit acknowledgment of all three.

---

## 5. Scaling and Infrastructure

### 5.1 Rollout cluster design

- **Worker shape.** 1 process per worker, single-threaded core pinned, ~2GB RAM. Each worker holds a network-weights cache, refreshed every K rollouts via shared filesystem (NFS) or pull-from-server.
- **Cluster shape.** 16 workers per host, 64–128 hosts in steady-state by Phase 3. Use whatever cluster mgmt the org runs (k8s, Slurm, in-house). Avoid distributed orchestration headaches by treating workers as cattle: identical, stateless, restart-on-failure.
- **Network weight distribution.** Trainer publishes weights to S3-equivalent every N steps. Workers pull on a schedule. Avoid synchronous "broadcast" — adds tail latency.
- **GPU/CPU balancing.** Combat MCTS is CPU-bound (state copies, action enumeration). Value/policy network calls are GPU-friendly only when batched. Use **batched inference servers** that aggregate calls from multiple workers; co-locate inference servers with worker pools by network topology.

### 5.2 Simulation throughput optimization

Profile-driven, in this order:

1. **State serialization.** If we copy state for branching, serialization is on the critical path. Avoid JSON; use binary; consider arena allocation.
2. **Action enumeration.** Enumerate lazily; cache per-state legal action lists when revisited.
3. **Card effect dispatch.** A switch over CardId is slow at scale. Generate code or use a function pointer table.
4. **GC pauses.** C# GC is the headline risk. Use struct types where possible; pool reusable objects (state buffers, action lists).
5. **Reimplementation in C++**: only after profiling shows >50% time in dispatch and we cannot close it in C#. We do not pre-empt this.

### 5.3 Replay storage systems

- **Hot tier:** RocksDB or LMDB on local NVMe. Rolling window ~50–500GB.
- **Cold tier:** S3-equivalent. Compressed Parquet with episode-level shards.
- **Schema:** versioned protobuf or flatbuffers. Schema migrations are first-class events.
- **Sampling primitives:** uniform, stratified-by-bucket, prioritized. Implemented at storage layer for performance, exposed as a sampling API to the trainer.

### 5.4 Distributed training

- **Single GPU first.** Resist multi-GPU until sample throughput exceeds single-GPU consumption rate. Distributed training for Phase 1 is premature.
- **Multi-GPU when needed.** Standard data-parallel (DDP). Single-node first (8 GPUs). Multi-node only if a single 8-GPU node can't keep up; this is unlikely until Phase 4+.
- **Gradient accumulation** for stability with large effective batches.
- **Checkpointing:** every N steps + every M minutes (whichever fires first), to local disk + cold tier. Atomic rename to avoid corrupt checkpoints. Eval the checkpoint before promoting it as "current".

### 5.5 Reproducibility

- Pinned dependency versions (lockfile).
- Pinned Game version. Treat patches as a new project version.
- Pinned RNG seeds for eval suites.
- Deterministic environment: even floating-point determinism for combat simulation. (Avoid `dot(x, y)` in multi-threaded reductions; use deterministic libs.)
- Every trained model is reproducible from `(code SHA, dataset SHA, seed, hyperparameters)`. Store all four in the model artifact.

### 5.6 Observability and debugging RL failures

- **Real-time dashboards.** Throughput, win rate over time, value-loss over time, KL between successive policy versions.
- **Anomaly alerts.** Sudden win-rate drop, value-loss spike, replay starvation.
- **Drill-down replays.** Click-through from any metric anomaly to a sample of replays. The replay pipeline should be navigable.
- **A/B harness.** Run two policy versions in parallel on identical seed batches; surface per-decision diffs.
- **Evaluation provenance.** Every aggregate metric must trace back to the underlying runs.

### 5.7 Deterministic regression testing

The prototype already has this for combat (`tools/seed-pinner`). Extend it to runs:

- A small (~50) set of pinned seeds that produce known-good run outcomes.
- Run on every CI commit; fail the CI on divergence.
- If a divergence is intentional (e.g., a network update changed a card pick), update the pin set explicitly with reviewer sign-off.

---

## 6. Research Challenges (the hard ones, ranked)

### 6.1 Long-horizon credit assignment

- **The problem.** A floor-3 card pick ripples to floor-50 win rate. Naive return propagation is too noisy at this distance.
- **Mitigations.** (a) Hierarchical decomposition with intermediate value functions per decision type. (b) Run-value bootstrapping: V(state) trained against multi-step TD targets, not raw return. (c) Search-augmented training: at decision time, n-step lookahead with the run-conditioned combat outcome oracle (ADR-014) reduces variance. (d) Counterfactual evaluation tools to measure decision impact in retrospect (observational only per ADR-017).
- **Open question.** Whether to train V(state) end-to-end via REINFORCE (high-variance, low-bias) or via TD bootstrapping (low-variance, high-bias). Recommend hybrid (TD with eligibility traces / V-trace).

### 6.2 Combinatorial deck/relic interactions

- **The problem.** ~200 cards × ~150 relics × ascension variants × deck-size variation. The pairwise interaction matrix alone has 30K entries, of which many are non-trivial.
- **Mitigations.** (a) Shared embeddings carry pair-info via composition. (b) Curriculum that explicitly samples interaction-heavy decks. (c) Adversarial scenario generation that targets predicted-low-confidence combos.
- **Open question.** Whether the value function is calibrated on tail combos or only on the bulk. Likely the latter; we monitor calibration by combo decile.

### 6.3 Exploration in run-level decision making

- **The problem.** Random card picks produce incoherent decks; the agent never sees "good" decks if it explores at random.
- **Mitigations.** (a) Bootstrap from heuristic baselines. (b) Search at decision time provides a soft prior even with random rollouts. (c) Decay random exploration aggressively once value-head is calibrated.
- **Open question.** Whether structured exploration (mode-seeking via determinantal point processes over deck archetypes) helps. Worth a research spike in Phase 3.

### 6.4 Hidden strategic state

- **The problem.** Some run-level state is implicit: the *kind* of deck the player is building (an archetype label that is never explicitly chosen), the *long-term plan* (e.g., "scale Defect's lightning"). The agent needs an internal representation of this.
- **Mitigations.** (a) Latent run-state from a recurrent encoder over decision history. (b) Auxiliary archetype-classification task for representation shaping (predict which archetype the deck is closest to; labels generated by clustering successful decks).
- **Open question.** Whether explicit archetype labels are useful or whether the latent representation suffices. Default: latent only; archetype as auxiliary loss only.

### 6.5 Balancing tactical and strategic optimization

- **The problem.** A combat-policy improvement that reduces HP loss in fight A may harm fight B if it changes deck-state distribution. Local optimization conflicts with global.
- **Mitigations.** (a) Joint training in Phase 3+ keeps tactical-strategic gradients aligned. (b) Continuous evaluation of combat policy on the live deck distribution detects drift.
- **Open question.** Whether to use a single shared encoder (maximum sharing, max gradient interference) or separate encoders per layer (less sharing, less interference). Default: single encoder, mitigation via gradient projection.

### 6.6 Counterfactual evaluation

- **The problem.** "Did the agent make the right map choice?" requires evaluating both the taken and not-taken paths.
- **Mitigations.** Counterfactual evaluator runs both branches via simulation and compares. Used for offline analysis and for training-data weighting (high-impact decisions weighted higher).
- **Open question.** Whether counterfactual estimates feed back into training or stay observational. Default: observational only — using them for training risks variance amplification.

### 6.7 Local minima

- **The problem.** The agent settles into a single dominant strategy that wins at moderate ascension but caps out.
- **Mitigations.** (a) Diverse starting points (multiple training seeds). (b) Population-based training in Phase 5 to maintain strategic diversity. (c) Explicit win-rate-by-archetype tracking; alarm on collapse.
- **Open question.** Whether population-based training is worth its compute cost on this domain. Defer until Phase 5 evidence.

### 6.8 Stability under curriculum scaling

- **The problem.** Adding new content (encounters, cards) can destabilize previously trained policies.
- **Mitigations.** (a) Replay buffer retention of historical content. (b) New-content ramp: start with small probability, scale up. (c) Regression battery on every commit catches large drops.
- **Open question.** Whether to train one policy across all content or to maintain content-versioned policy variants. Default: one policy, retrained on patches.

---

## 7. Recommended Tech Stack

Each component has one **default** and at most one **fallback**. Options proliferation is a project killer.

| Component | Default | Fallback | Rationale |
|---|---|---|---|
| **RL framework** | Custom MCTS in C++ + PyTorch trainer | TorchRL | Existing libs over-general; our action mask shape and game-specific search wants tight code |
| **Network framework** | PyTorch | — | De facto standard; broadest research support |
| **Distributed training** | PyTorch DDP | DeepSpeed (Phase 5) | Single-node is enough until Phase 4+ |
| **Orchestration** | Slurm or k8s (whichever org has) | — | Don't build new infra |
| **Inference serving** | ONNX Runtime in-process via Python | TorchServe or Triton | In-process is simplest for our latency target |
| **Replay storage** | RocksDB local + Parquet on S3-equivalent | LMDB | Standard, well-debugged |
| **Experiment tracking** | Weights & Biases | MLflow | W&B has better team-collaboration UX |
| **Observability** | Prometheus + Grafana | Datadog | Open standard; portable |
| **Dataset tooling** | Apache Arrow / Parquet | — | Python+C# both have first-class support |
| **Simulator integration** | C# headless ↔ shared memory IPC ↔ Python | gRPC | Shared memory wins on latency for hot path |
| **Game source integration** | Out-of-tree mod via `Core/Modding` | Direct fork | Mods rebase cleaner across patches |
| **Reproducibility** | Pinned `requirements.txt` + lockfile + Docker image per phase | — | Standard discipline |
| **Code analysis** | Reuse existing `tools/ast-analyzer` | — | Already builds AST analysis docs |

**Languages:**
- **C#** for engine modifications, headless port, mods.
- **C++** for performance-critical search, possibly per-card mechanics if profiling demands.
- **Python** for training, evaluation, tooling, orchestration.
- **Rust** is tempting but adds a polyglot tax we don't need. Skip unless a hard-perf C# bottleneck appears.

---

## 8. Deliverables: Roadmap, Team, Risks

### 8.1 Proposed 12–24 month implementation roadmap

| Months | Phase / milestone | Owners | Gating metric |
|---|---|---|---|
| 0–2 | Headless port + determinism + replay infra | Sim eng, 1 RL eng | 10⁵ combats/day single-host; bit-identical replay |
| 2–6 | Phase 1 combat policy | RL leads + Sim | 95% A0 normal-combat win rate, 90% expectimax agreement |
| 6–10 | Phase 2 card-pick + run-value | RL leads | 70% A0 full-run win rate (lead character) |
| 10–14 | Phase 3 full run planning | RL leads + Eval | 85% A0 + 70% A10–A15 (lead character) |
| 14–17 | Phase 4 multi-character | Whole team | 80% A0 + 60% A10 (every character) |
| 17–24 | Phase 5 superhuman + patch loop | Whole team | >50% A20 every character; 2-week patch fine-tune |

Months 0–12 are the committed scope. Months 12–24 are the planned-but-uncommitted scope; the 12-month review re-plans them.

### 8.2 Recommended team composition

**Initial team (months 0–6):**
- 1 Research Lead (RL background; sets architecture, owns research direction).
- 2 RL Engineers (training infra, network design, search).
- 2 Systems Engineers (headless port, simulation, distributed infra).
- 1 Simulator/Game Engineer (C#, Godot internals, port to headless).
- 1 Evaluation Engineer (regression battery, dashboards, exploit detection).

**Adds at month 6+:**
- +1 RL Engineer (Phase 2 card-pick complexity).
- +1 Systems Engineer (rollout cluster scaling).

**Adds at month 12+:**
- +1 Research Engineer (Phase 5 model-based / world-model work).
- +1 Eval Engineer (held-out content design, calibration analysis).

**External advisors:** one game-design SME (Megacrit if accessible; otherwise an STS expert) consulted at major phase boundaries.

### 8.3 Milestones and gating metrics

Already itemized per phase in §3 and §8.1. The specific principle: **do not soften a gate to advance a phase.** If Phase 2's gate is 70% win rate and you have 65%, the answer is more time on Phase 2, not advancing to Phase 3 with a weaker foundation.

### 8.4 Highest-risk technical assumptions

1. **Headless port is achievable in <2 months.** Risk: Godot dependencies are deeper than expected (e.g., card animations gate state transitions). Mitigation: time-box, fall back to running real Godot headless mode.
2. **Determinism is achievable.** Risk: undocumented sources of nondeterminism (multi-threaded shuffles, timing-dependent code). Mitigation: differential testing; treat any divergence as a first-class bug.
3. **MCTS scales to combat action sequences.** Risk: per-turn play sequences explode the tree. Mitigation: action grouping, value-guided pruning, throughput compensates.
4. **Shared embeddings transfer across characters.** Risk: characters are mechanically distinct enough that shared parameters interfere. Mitigation: per-character heads, gradient projection.
5. **Throughput targets.** 10⁸ combat-steps/day is aggressive; if missed by 5x, the curriculum has to compress. Mitigation: profile early, optimize hot path, reimplement only where it matters.
6. **Patch cadence.** STS2 will receive content updates. We engineer for re-fit, but very frequent patches (>1/month) erode the agent. Mitigation: card-text subnetwork; budget for ongoing fine-tuning.

### 8.5 What to prototype immediately

In the first 2 weeks, regardless of long-term plan:

1. **Headless C# Core spike.** Try to compile and run one combat headless. Time-boxed; if fundamental blockers appear, escalate.
2. **Differential test scaffold.** Random seed + random actions, run in Godot vs. headless, diff. Even before a full headless build, this catches the easy cases.
3. **Network proof-of-concept on existing prototype.** Train a tiny policy network on the existing C++ expectimax outputs; verify the gradient flow, the data pipeline, and the eval harness end-to-end on the trivial encounter. This validates the AlphaZero ladder before we invest in the full headless port.
4. **Replay schema v0.** A binary format that can serialize one combat. Even minimal — we'll iterate, but having a schema prevents schema sprawl.
5. **Evaluation suite v0.** 100 fixed seeds, the existing prototype's solution as ground truth. Gates everything that comes after.

### 8.6 What to delay intentionally

- **Multi-character work.** Until Phase 4. Tempting to "validate transfer" early; resist.
- **World models.** Until Phase 5. They are sexy and complicated; they earn their keep only if simulation throughput becomes the binding constraint, which it won't until run-level search demands it.
- **Population-based training.** Until Phase 5 evidence of mode collapse.
- **Macro-actions / hierarchical RL beyond decision-type dispatch.** Until Phase 5 evidence the flat per-decision policy is insufficient.
- **Deep custom MCTS optimizations** (virtual loss, transposition tables in MCTS, etc.) — until profiling shows they help.
- **Multi-node distributed training.** Until single-node is provably sample-bound.
- **Imitation learning from human replays.** Until a high-quality dataset is available.
- **End-to-end joint training of all heads.** Until each head trained-in-isolation reaches competence.

### 8.7 Indicators that the architecture is scaling correctly

- **Win rate climbs monotonically** at each phase boundary on held-out evals; small dips are normal, sustained dips are not.
- **Search-budget vs. quality curve** is monotonic and saturates predictably (suggests the value head is well-calibrated).
- **New-content fine-tune time** is bounded and decreasing patch-over-patch (suggests the encoder is generalizing).
- **Combat policy degradation when meta-policy explores new decks** is small (<5% local win rate) (suggests OOD robustness).
- **Worker fleet utilization** is high (>80%) and uniform (suggests no infrastructure bottleneck).
- **Replay buffer ingest rate ≥ training consumption rate** (suggests we are not data-starved).
- **Determinism regression suite** is green at every commit (suggests engineering discipline holding).

### 8.8 Warning signs that the approach will fail and should be redesigned

- **Win rate plateau before phase gate.** If Phase 2 plateaus at 50% for >1 month with no architectural change improving it, the underlying representation is wrong. Redesign before continuing.
- **Calibration breakdown.** Predicted run-value diverges from empirical win rate by >20% in any meaningful decile. Suggests the value function is not learning the right thing.
- **Catastrophic forgetting** between phases despite anti-forgetting measures. Suggests the architecture cannot share representations cleanly; consider per-phase models.
- **Determinism loss.** Differential test shows divergence we cannot trace. Suggests the headless port is fundamentally fragile; consider running in Godot headless mode.
- **Sim throughput floor**. We hit a hard ceiling at <10× the prototype's throughput despite 10× the cores. Suggests engine architecture, not sim, is the bottleneck.
- **Patch fine-tune time grows** rather than shrinks across patches. Suggests the agent is over-fitting to specific content rather than generalizing; the encoder needs redesign (probably more reliance on card-text and less on token IDs).
- **Exploit incidence rises**. If exploit detection counts grow with training, the agent is overfitting to engine bugs rather than learning the game. Pause and audit before resuming training.

### 8.9 The kill criteria

These trigger redesign or project pause, not just concern:

1. Win rate at end of Phase 2 < 50% on lead character A0.
2. Sim throughput at end of Phase 1 < 5× the prototype's headless throughput.
3. Determinism cannot be achieved in 4 months.
4. Multi-character generalization (Phase 4) fails to transfer (worse than per-character training in isolation).
5. Per-patch fine-tune compute exceeds 50% of original training compute by Phase 5.

If two of these fire, escalate to a full architecture review. If three fire, the approach is wrong.

---

## Appendix A — Open questions for the team to resolve before kickoff

1. **Reimplementation scope.** Will we commit to running C# Core headless, or is the long-term plan a full C++ reimplementation? If C++, the throughput plan changes; if C#, the staffing plan changes.
2. **Megacrit relationship.** Is there an option for Megacrit to expose an official headless / automation API? This could collapse the first 2 months of work to weeks.
3. **Hosted vs. self-hosted compute.** GPU + CPU fleet; cloud or on-prem. Affects cost, latency, and orchestration choice.
4. **Patch cadence assumption.** What is the expected STS2 patch cadence? Affects how aggressively we engineer for adaptation.
5. **Dataset sharing.** Will training data, model checkpoints, and replays be shared externally (research community) or kept internal? Affects choice of model architectures (some published work makes assumptions about reproducibility).
6. **Goal definition.** "Beat A20 every character at >50%" is *a* superhuman bar. Is it *the* bar? Or is the goal a daily-climb agent, a tutorial-coach agent, a content-balance-evaluation agent? These are different products with different architectures.
7. **Exploit policy.** When the agent finds a degenerate but technically-allowed strategy, do we patch the agent (bias against it) or accept it? Affects evaluation methodology.

These should be answered in writing before Phase 1 work begins.

---

## Appendix B — What the existing prototype directly contributes vs. what gets discarded

**Contributes.**
- `CompactState` design pattern → `CompactState` becomes the verifier-side type for offline analysis and oracle generation.
- Expectimax search → becomes the gold-standard oracle against which the network's policy is regression-tested on tractable states.
- `legal_actions` enumeration → reused directly as the action mask source.
- `transition::apply_player_action` and `resolve_end_turn_pre_draw` → may be reused as the C++ fast path if profiling demands later. Otherwise, the C# engine takes over.
- `tools/seed-pinner` pattern → generalized into a per-component regression battery.
- `tools/ast-analyzer` → directly useful for engine refactor reviews; keep building on it.
- The 252-test suite → keeps regression-protecting the core mechanics through any future refactor.

**Discarded (or relegated to test-only).**
- The CLI rendering — the agent does not read ANSI.
- The interactive input loop — agent uses a hook protocol.
- The bag-of-counts representation as the *primary* state encoding — outgrown by Phase 1 mid-deck variance; persists as a verifier representation.
- The 4-card hardcoded counted list — replaced by full token table.
- Two-fixed-enemy `std::array<EnemyState, 2>` — replaced by variable-length enemy sequence.

The discards aren't waste. They are the early prototype validating the determinism-first, hashable-state, exact-transition discipline. That discipline is the asset; the specific data structures are scaffolding.

---

## End

**The single decision that matters most.** Whether to invest in a deep headless port of the C# engine or to reimplement in C++ from scratch. Get this right and the rest is execution. Get it wrong and we are paying a tax for two years.

**Recommendation.** Headless C# Core, with C++ reimplementation reserved as a targeted optimization for the combat hot path if and only if profiling demands it. The team's existing C++ port is a forcing function in the wrong direction — it makes us feel productive while we duplicate work that the source code already does correctly.

**Single most important early experiment.** Two weeks: get one full combat running headless, deterministically, save/restore identical, against the existing prototype's encounter. If that works, the project is real. If it doesn't, we know we have a hard problem before we have committed.
