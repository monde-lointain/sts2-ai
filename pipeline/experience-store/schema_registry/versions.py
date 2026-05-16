"""SchemaVersion value class — single source-of-truth for wire-version.

Replaces scattered `(major, minor)` tuples and `_PHASE1_MAJOR/_MINOR` consts.
PHASE1 is locked by contracts/schemas/trajectory/trajectory.proto:3-4.
"""

from __future__ import annotations

import dataclasses


@dataclasses.dataclass(frozen=True, slots=True)
class SchemaVersion:
    major: int
    minor: int

    def __post_init__(self) -> None:
        if not isinstance(self.major, int) or not isinstance(self.minor, int):
            raise TypeError(
                f"SchemaVersion requires int major/minor; got "
                f"{type(self.major).__name__}/{type(self.minor).__name__}"
            )
        if self.major < 0 or self.minor < 0:
            raise ValueError(
                f"SchemaVersion components must be >= 0; got ({self.major}, {self.minor})"
            )

    def as_tuple(self) -> tuple[int, int]:
        return (self.major, self.minor)

    def as_fields(self) -> dict[str, int]:
        return {"major": self.major, "minor": self.minor}

    @classmethod
    def from_fields(cls, fields: dict) -> SchemaVersion:
        return cls(int(fields["major"]), int(fields["minor"]))

    @classmethod
    def from_tuple(cls, t: tuple[int, int]) -> SchemaVersion:
        return cls(int(t[0]), int(t[1]))

    def __str__(self) -> str:
        return f"{self.major}.{self.minor}"


# Phase-1A canonical wire version.
PHASE1 = SchemaVersion(1, 0)

# Phase-1A v1.1 — additive minor bump per ADR-019 Decision 4 (gold_shadow_price,
# max_hp_shadow_price appended to MacroContext). Current write target.
PHASE1_1 = SchemaVersion(1, 1)
