from __future__ import annotations

import re

VERSION_RE = re.compile(r"^phase1-silent\.(\d+)$")


def _version_index(value: str) -> int:
    match = VERSION_RE.match(value)
    return int(match.group(1)) if match else -1


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
    return errors


def validate_card_dsl(registry: dict) -> list[str]:
    errors: list[str] = []
    tokens = {token["token"] for token in registry.get("tokens", [])}
    card_tokens = {
        token["token"] for token in registry.get("tokens", []) if token["kind"] == "card"
    }
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
    return errors
