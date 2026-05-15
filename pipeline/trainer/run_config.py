"""Run-config submodule: identity, validation, and provenance bootstrap.

Loads ``pipeline/trainer/config/local.json``, validates required keys, builds
frozen dataclasses consumed by every other Q10 submodule, and captures a
``RunProvenance`` snapshot (`code_sha`, dirty flag, host, timestamp, seed,
parent artifact, run_id) before any mutable training state starts.

Constraints: no third-party deps; ULID is implemented inline. See
``pipeline/trainer/docs/specs/modules/run-config.md``.
"""
from __future__ import annotations

import os
import secrets
import socket
import subprocess
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

from pipeline.common.service_host import load_config


# ---------------------------------------------------------------------------
# ULID (inline; no external dep). 26-char Crockford-base32, 48-bit ms
# timestamp + 80-bit randomness. Spec: https://github.com/ulid/spec.
# ---------------------------------------------------------------------------
_CROCKFORD = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"


def _new_ulid() -> str:
    """Return a fresh 26-char Crockford-base32 ULID."""
    ts_ms = int(time.time() * 1000) & ((1 << 48) - 1)
    rand = secrets.randbits(80)
    value = (ts_ms << 80) | rand  # 128 bits total
    out = []
    for _ in range(26):
        out.append(_CROCKFORD[value & 0x1F])
        value >>= 5
    return "".join(reversed(out))


# ---------------------------------------------------------------------------
# Frozen dataclasses
# ---------------------------------------------------------------------------
@dataclass(frozen=True)
class NetworkConfig:
    expected_token_count: Optional[int]
    d_model: int
    n_layers: int
    n_heads: int
    ffn_dim: int
    max_seq_len: int
    max_action_space: int = 100


@dataclass(frozen=True)
class OptimConfig:
    lr: float
    weight_decay: float
    warmup_steps: int
    total_steps: int
    grad_clip: float


@dataclass(frozen=True)
class LossWeights:
    policy: float
    combat_sample: float
    combat_summary: float
    hp_frac_aux: float
    kl_beta: float


@dataclass(frozen=True)
class CheckpointConfig:
    every_n_steps: int
    every_m_minutes: int


@dataclass(frozen=True)
class WandbConfig:
    enabled: bool


@dataclass(frozen=True)
class RunConfig:
    # Inherited service keys
    service: str
    port: int
    data_dir: str
    # Q10-specific top-level
    q3_url: str
    q5_url: str
    parent_artifact_id: Optional[str]
    seed: int
    refuse_on_dirty: bool
    sampling_mode: str
    prefetch_queue_size: int
    batch_size: int
    # Nested
    network: NetworkConfig
    optim: OptimConfig
    loss_weights: LossWeights
    checkpoint: CheckpointConfig
    wandb: WandbConfig
    # Identity
    run_id: str

    @classmethod
    def load(cls, path: Path) -> "RunConfig":
        """Read + validate JSON, return frozen instance. Mints ``run_id`` once."""
        raw = load_config(path)  # asserts service/port/data_dir
        _require(
            raw,
            path,
            keys=[
                "q3_url",
                "q5_url",
                "parent_artifact_id",  # nullable but must be present
                "seed",
                "refuse_on_dirty",
                "network",
                "optim",
                "loss_weights",
                "checkpoint",
                "wandb_enabled",
                "sampling_mode",
                "prefetch_queue_size",
                "batch_size",
            ],
        )
        network = _build_network(raw["network"], path)
        optim = _build_optim(raw["optim"], path)
        loss = _build_loss(raw["loss_weights"], path)
        ckpt = _build_ckpt(raw["checkpoint"], path)
        wandb_cfg = WandbConfig(enabled=bool(raw["wandb_enabled"]))
        return cls(
            service=str(raw["service"]),
            port=int(raw["port"]),
            data_dir=str(raw["data_dir"]),
            q3_url=str(raw["q3_url"]),
            q5_url=str(raw["q5_url"]),
            parent_artifact_id=(
                None if raw["parent_artifact_id"] is None else str(raw["parent_artifact_id"])
            ),
            seed=int(raw["seed"]),
            refuse_on_dirty=bool(raw["refuse_on_dirty"]),
            sampling_mode=str(raw["sampling_mode"]),
            prefetch_queue_size=int(raw["prefetch_queue_size"]),
            batch_size=int(raw["batch_size"]),
            network=network,
            optim=optim,
            loss_weights=loss,
            checkpoint=ckpt,
            wandb=wandb_cfg,
            run_id=_new_ulid(),
        )


@dataclass(frozen=True)
class RunProvenance:
    code_sha: str
    run_dirty: bool
    start_ts_ns: int
    host: str
    seed: int
    parent_artifact_id: Optional[str]
    run_id: str

    @classmethod
    def capture(
        cls, config: RunConfig, parent_artifact_id: Optional[str]
    ) -> "RunProvenance":
        code_sha = _git(["rev-parse", "HEAD"])
        porcelain = _git(["status", "--porcelain"])
        run_dirty = bool(porcelain.strip())
        if run_dirty and config.refuse_on_dirty:
            raise RuntimeError(
                "refuse_on_dirty=True: working tree is dirty (git status non-empty)"
            )
        return cls(
            code_sha=code_sha,
            run_dirty=run_dirty,
            start_ts_ns=time.time_ns(),
            host=socket.gethostname(),
            seed=config.seed,
            parent_artifact_id=parent_artifact_id,
            run_id=config.run_id,
        )


# ---------------------------------------------------------------------------
# seed_everything
# ---------------------------------------------------------------------------
def seed_everything(seed: int) -> None:
    """Seed Python random, NumPy, torch (CPU+CUDA best-effort) and PYTHONHASHSEED."""
    import random

    os.environ["PYTHONHASHSEED"] = str(seed)
    random.seed(seed)
    try:
        import numpy as np  # type: ignore
        np.random.seed(seed)
    except ImportError:
        pass
    try:
        import torch  # type: ignore
        torch.manual_seed(seed)
        if torch.cuda.is_available():
            torch.cuda.manual_seed_all(seed)
    except ImportError:
        pass


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
def _require(raw: dict, path: Path, *, keys: list[str]) -> None:
    missing = [k for k in keys if k not in raw]
    if missing:
        raise ValueError(f"{path}: missing {', '.join(missing)}")


def _build_network(block: dict, path: Path) -> NetworkConfig:
    _require(
        block,
        path,
        keys=[
            "expected_token_count",
            "d_model",
            "n_layers",
            "n_heads",
            "ffn_dim",
            "max_seq_len",
        ],
    )
    return NetworkConfig(
        expected_token_count=(
            None if block["expected_token_count"] is None else int(block["expected_token_count"])
        ),
        d_model=int(block["d_model"]),
        n_layers=int(block["n_layers"]),
        n_heads=int(block["n_heads"]),
        ffn_dim=int(block["ffn_dim"]),
        max_seq_len=int(block["max_seq_len"]),
        max_action_space=int(block.get("max_action_space", 100)),
    )


def _build_optim(block: dict, path: Path) -> OptimConfig:
    _require(
        block,
        path,
        keys=["lr", "weight_decay", "warmup_steps", "total_steps", "grad_clip"],
    )
    return OptimConfig(
        lr=float(block["lr"]),
        weight_decay=float(block["weight_decay"]),
        warmup_steps=int(block["warmup_steps"]),
        total_steps=int(block["total_steps"]),
        grad_clip=float(block["grad_clip"]),
    )


def _build_loss(block: dict, path: Path) -> LossWeights:
    _require(
        block,
        path,
        keys=["policy", "combat_sample", "combat_summary", "hp_frac_aux", "kl_beta"],
    )
    return LossWeights(
        policy=float(block["policy"]),
        combat_sample=float(block["combat_sample"]),
        combat_summary=float(block["combat_summary"]),
        hp_frac_aux=float(block["hp_frac_aux"]),
        kl_beta=float(block["kl_beta"]),
    )


def _build_ckpt(block: dict, path: Path) -> CheckpointConfig:
    _require(block, path, keys=["every_n_steps", "every_m_minutes"])
    return CheckpointConfig(
        every_n_steps=int(block["every_n_steps"]),
        every_m_minutes=int(block["every_m_minutes"]),
    )


def _git(args: list[str]) -> str:
    """Run a git subprocess; raise if non-zero."""
    res = subprocess.run(
        ["git", *args], capture_output=True, text=True, check=False
    )
    if res.returncode != 0:
        raise RuntimeError(
            f"git {' '.join(args)} failed (rc={res.returncode}): {res.stderr.strip()}"
        )
    return res.stdout.strip()


__all__ = [
    "RunConfig",
    "RunProvenance",
    "NetworkConfig",
    "OptimConfig",
    "LossWeights",
    "CheckpointConfig",
    "WandbConfig",
    "seed_everything",
]
