from __future__ import annotations

import re

VERSION_RE = re.compile(r"^phase1-silent\.(\d+)$")

K_UNKNOWN_MAX: int = 25

_VALID_COVERAGE = {"extracted", "noop", "unknown_dominant", "stub"}
_VALID_ATTACK_TARGETS = {"single", "all_enemies", "random_enemies"}
_VALID_APPLY_TARGETS = {"self", "single", "all_enemies"}


def _version_index(value: str) -> int:
    match = VERSION_RE.match(value)
    return int(match.group(1)) if match else -1


def _schema_minor(registry: dict) -> int | None:
    sv = registry.get("manifest", {}).get("schema_version", {})
    return sv.get("minor")


def validate(registry: dict) -> list[str]:
    errors: list[str] = []
    tokens = registry.get("tokens", [])
    token_ids: set[int] = set()
    token_names: set[str] = set()
    for token in tokens:
        token_id = token["token_id"]
        name = token["token"]
        if token_id in token_ids:
            errors.append(f"duplicate token_id {token_id}")
        if name in token_names:
            errors.append(f"duplicate token {name}")
        token_ids.add(token_id)
        token_names.add(name)

    deprecated_ids = {entry["token_id"] for entry in registry.get("deprecation_log", [])}
    for token in tokens:
        if token["token_id"] in deprecated_ids and not token.get("deprecated_in"):
            errors.append(f"reused deprecated token_id {token['token_id']}")
        for reference in token.get("references", []):
            if reference not in token_names:
                errors.append(f"missing token reference {reference}")

    seen_versions = [
        _version_index(entry["deprecated_in_version"])
        for entry in registry.get("deprecation_log", [])
    ]
    if seen_versions != sorted(seen_versions):
        errors.append("deprecation_log not monotonic")

    errors.extend(validate_card_dsl(registry))
    errors.extend(validate_schema_version(registry))
    return errors


def validate_schema_version(registry: dict) -> list[str]:
    errors: list[str] = []
    minor = _schema_minor(registry)
    if minor != 1:
        errors.append(f"schema_version.minor must be 1 (post-wave-45/B.1 baseline); got {minor}")
    return errors


def validate_card_dsl(registry: dict) -> list[str]:
    errors: list[str] = []
    tokens = {token["token"] for token in registry.get("tokens", [])}
    card_tokens = {
        token["token"] for token in registry.get("tokens", []) if token["kind"] == "card"
    }
    minor = _schema_minor(registry)
    dsl_tokens = set()
    for record in registry.get("card_dsl", []):
        token = record.get("token")
        dsl_tokens.add(token)
        if token not in tokens:
            errors.append(f"card_dsl token missing {token}")
        if "cost" not in record or "type" not in record or "effects" not in record:
            errors.append(f"card_dsl malformed {token}")
        for effect in record.get("effects", []):
            if "op" not in effect:
                errors.append(f"card_dsl effect malformed {token}")
    missing = sorted(card_tokens - dsl_tokens)
    if missing:
        errors.append(f"card_dsl missing cards {', '.join(missing[:5])}")

    # New invariants — only enforced at minor==1 (post-Q4-B1 fan-in)
    if minor == 1:
        errors.extend(_validate_dsl_coherence(registry))
        errors.extend(_validate_stub_rejection(registry))
        errors.extend(_validate_unknown_tolerance(registry))
        errors.extend(_validate_coverage_field(registry))

    return errors


# ---------------------------------------------------------------------------
# New invariant helpers
# ---------------------------------------------------------------------------


def _has_base(effect: dict) -> bool:
    return "base_var" in effect or "base" in effect


def _validate_dsl_coherence(registry: dict) -> list[str]:
    """Invariant 1 — per-op field requirements for every effect in card_dsl."""
    errors: list[str] = []
    for record in registry.get("card_dsl", []):
        token = record.get("token", "<unknown>")
        for effect in record.get("effects", []):
            op = effect.get("op")
            if op == "attack":
                if not _has_base(effect):
                    errors.append(f"dsl_coherence: {token} attack missing base/base_var")
                target = effect.get("target")
                if target not in _VALID_ATTACK_TARGETS:
                    errors.append(f"dsl_coherence: {token} attack invalid target {target!r}")
                if target == "random_enemies" and "hits_var" not in effect:
                    errors.append(f"dsl_coherence: {token} attack random_enemies missing hits_var")
            elif op == "block_self":
                if not _has_base(effect):
                    errors.append(f"dsl_coherence: {token} block_self missing base/base_var")
            elif op == "apply_power":
                if "power" not in effect:
                    errors.append(f"dsl_coherence: {token} apply_power missing power")
                if not _has_base(effect):
                    errors.append(f"dsl_coherence: {token} apply_power missing base/base_var")
                target = effect.get("target")
                if target not in _VALID_APPLY_TARGETS:
                    errors.append(f"dsl_coherence: {token} apply_power invalid target {target!r}")
            elif op == "draw":
                if not _has_base(effect):
                    errors.append(f"dsl_coherence: {token} draw missing base/base_var")
            elif op == "gain_energy":
                if "base_var" not in effect:
                    errors.append(f"dsl_coherence: {token} gain_energy missing base_var")
            elif op == "create_shivs":
                if not _has_base(effect):
                    errors.append(f"dsl_coherence: {token} create_shivs missing base/base_var")
            elif op == "noop":
                pass  # no required fields
            elif op == "unknown":
                if "source" not in effect:
                    errors.append(f"dsl_coherence: {token} unknown missing source")
                if "upstream_line" not in effect:
                    errors.append(f"dsl_coherence: {token} unknown missing upstream_line")
            elif op == "stub":
                errors.append(f"dsl_coherence: {token} uses rejected op 'stub'")
            elif op == "extracted":
                errors.append(f"dsl_coherence: {token} uses rejected op 'extracted' (sentinel)")
            else:
                errors.append(f"dsl_coherence: {token} unknown extractor op {op!r}")
    return errors


def _validate_stub_rejection(registry: dict) -> list[str]:
    """Invariant 2 — zero stub ops post-fan-in."""
    stub_tokens = [
        record.get("token", "<unknown>")
        for record in registry.get("card_dsl", [])
        for effect in record.get("effects", [])
        if effect.get("op") == "stub"
    ]
    if stub_tokens:
        listed = ", ".join(sorted(set(stub_tokens))[:10])
        return [f"stub_rejection: {len(stub_tokens)} stub op(s) found; affected cards: {listed}"]
    return []


def _validate_unknown_tolerance(registry: dict) -> list[str]:
    """Invariant 3 — unknown op count must not exceed K_UNKNOWN_MAX."""
    unknown_count = sum(
        1
        for record in registry.get("card_dsl", [])
        for effect in record.get("effects", [])
        if effect.get("op") == "unknown"
    )
    if unknown_count > K_UNKNOWN_MAX:
        return [
            f"unknown_tolerance: {unknown_count} unknown ops exceed K_UNKNOWN_MAX={K_UNKNOWN_MAX}"
        ]
    return []


def _validate_coverage_field(registry: dict) -> list[str]:
    """Invariant 4 — every card_dsl entry must have a valid coverage field."""
    errors: list[str] = []
    for record in registry.get("card_dsl", []):
        token = record.get("token", "<unknown>")
        coverage = record.get("coverage")
        if coverage is None:
            errors.append(f"coverage_field: {token} missing coverage")
        elif coverage not in _VALID_COVERAGE:
            errors.append(f"coverage_field: {token} invalid coverage {coverage!r}")
    return errors
