"""Synthetic Trajectory writer for Phase-1A CULTISTS_NORMAL golden (S0.E).

Builds D1-shaped Trajectory protobufs with deterministic-from-seed content
matching Q3-ADR-005 (degenerate combat samples). Used by:

  - `tests/test_phase1a_cultists_golden.py` reads the committed golden.
  - The golden binary itself is produced by running this module's CLI once:
    `python synthetic_writer.py --steps 8 --output tests/data/cultists_smoke_episode.bin`
    The result is bit-identical for the same seed + steps (the
    `random.Random(seed).randbytes(...)` and pure-fixed-value fields make
    serialization deterministic given the canonical proto field ordering).

Shape per dispatch prompt:
  - schema_version = (1, 1)  # ADR-019 v1.1 (sp(gold)+sp(MaxHP) appended)
  - trajectory_id = "phase-1a-cultists-smoke"
  - episode_id = "smoke-episode-001"
  - seed = 0xCAFEBABE
  - model_version = "phase-1a-stub-v0"
  - sampling_mode = "synthetic"
  - generator = "q3-synthetic-writer"
  - steps[i]: COMBAT + POLICY_VISIBLE, degenerate sample
    (survived=True, hp_delta=-0.05, probability_weight=1.0),
    reward=1.0 iff terminal else 0.0, terminal=(i == N-1),
    rich_state=256 random-but-seeded bytes, legal_action_ids=[0,1,2,3],
    search_policy=[0.4,0.3,0.2,0.1], action_taken=i%4,
    combat_outcome_summary(expected_hp_delta=-0.05, survival_probability=1.0),
    macro_context(hp_shadow_price=1.0, risk_tolerance=0.5,
    derivation_method="phase-1a-stub", gold_shadow_price=0.0,
    max_hp_shadow_price=0.0),  # v1.1 zero-stubs per ADR-019 Phase-1
    resource_deltas(hp_delta=-0.05), reward_context(room_type="monster").
"""

from __future__ import annotations

import argparse
import random
import sys
from pathlib import Path

# Allow direct CLI invocation without PYTHONPATH gymnastics: the
# `pipeline/experience-store/` dir is the canonical import root (per
# conftest.py). This mirrors service.py's sys.path injection.
_ROOT = Path(__file__).resolve().parents[1]
if str(_ROOT) not in sys.path:
    sys.path.insert(0, str(_ROOT))

from proto import DecisionType, ObservabilityRegime, Trajectory  # noqa: E402

DEFAULT_SEED = 0xCAFEBABE
DEFAULT_TRAJECTORY_ID = "phase-1a-cultists-smoke"
DEFAULT_EPISODE_ID = "smoke-episode-001"
DEFAULT_MODEL_VERSION = "phase-1a-stub-v0"
DEFAULT_SAMPLING_MODE = "synthetic"
DEFAULT_GENERATOR = "q3-synthetic-writer"
DEFAULT_STEPS = 8
RICH_STATE_BYTES = 256


def build_trajectory(
    *,
    n_steps: int = DEFAULT_STEPS,
    seed: int = DEFAULT_SEED,
    trajectory_id: str = DEFAULT_TRAJECTORY_ID,
    episode_id: str = DEFAULT_EPISODE_ID,
    model_version: str = DEFAULT_MODEL_VERSION,
    sampling_mode: str = DEFAULT_SAMPLING_MODE,
    generator: str = DEFAULT_GENERATOR,
) -> Trajectory:
    """Build a deterministic D1-shaped CULTISTS_NORMAL trajectory."""
    if n_steps <= 0:
        raise ValueError(f"n_steps must be > 0; got {n_steps}")

    t = Trajectory()
    t.schema_version.major = 1
    t.schema_version.minor = 1
    t.trajectory_id = trajectory_id
    t.episode_id = episode_id
    t.seed = int(seed)
    t.model_version = model_version
    t.sampling_mode = sampling_mode
    t.generator = generator

    rng = random.Random(seed)

    for i in range(n_steps):
        step = t.steps.add()
        step.decision_type = DecisionType.DECISION_TYPE_COMBAT
        step.observability_regime = (
            ObservabilityRegime.OBSERVABILITY_REGIME_POLICY_VISIBLE
        )
        step.terminal = (i == n_steps - 1)
        step.reward = 1.0 if step.terminal else 0.0
        step.action_taken = i % 4

        # 256-byte synthetic rich_state; seeded RNG keeps it stable.
        step.rich_state = bytes(rng.randint(0, 255) for _ in range(RICH_STATE_BYTES))

        step.legal_action_ids.extend([0, 1, 2, 3])
        step.search_policy.extend([0.4, 0.3, 0.2, 0.1])

        # Degenerate combat sample per Q3-ADR-005.
        sample = step.combat_outcome_samples.add()
        sample.survived = True
        sample.hp_delta = -0.05
        sample.probability_weight = 1.0

        step.combat_outcome_summary.expected_hp_delta = -0.05
        step.combat_outcome_summary.survival_probability = 1.0

        step.macro_context.hp_shadow_price = 1.0
        step.macro_context.risk_tolerance = 0.5
        step.macro_context.derivation_method = "phase-1a-stub"
        # ADR-019 v1.1: zero-stub sp(gold) + sp(MaxHP) (Phase-1 derivation).
        step.macro_context.gold_shadow_price = 0.0
        step.macro_context.max_hp_shadow_price = 0.0

        step.resource_deltas.hp_delta = -0.05

        step.reward_context.room_type = "monster"

    return t


def write_trajectory(trajectory: Trajectory, output_path: Path) -> int:
    """Serialize and write; returns bytes written."""
    blob = trajectory.SerializeToString()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(blob)
    return len(blob)


def _parse_seed(raw: str) -> int:
    """Accept decimal or 0xhex seeds; raise ValueError on overflow."""
    value = int(raw, 0)
    if value < 0 or value > 0xFFFFFFFFFFFFFFFF:
        raise ValueError(f"seed must fit in uint64; got {raw}")
    return value


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Emit a D1-shaped synthetic Trajectory binary (CULTISTS_NORMAL)."
    )
    parser.add_argument(
        "--steps", type=int, default=DEFAULT_STEPS, help="step count (default: 8)"
    )
    parser.add_argument(
        "--seed",
        type=_parse_seed,
        default=DEFAULT_SEED,
        help="uint64 RNG seed (default: 0xCAFEBABE)",
    )
    parser.add_argument(
        "--trajectory-id",
        type=str,
        default=DEFAULT_TRAJECTORY_ID,
    )
    parser.add_argument(
        "--episode-id", type=str, default=DEFAULT_EPISODE_ID
    )
    parser.add_argument(
        "--output", type=Path, required=True, help="output binary path"
    )
    args = parser.parse_args(argv)

    trajectory = build_trajectory(
        n_steps=args.steps,
        seed=args.seed,
        trajectory_id=args.trajectory_id,
        episode_id=args.episode_id,
    )
    n_bytes = write_trajectory(trajectory, args.output)
    print(f"wrote {n_bytes} bytes to {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
