import copy
import json
import pathlib
import sys
import unittest
import warnings

ROOT = pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools" / "content"))

import validate_registry  # pyright: ignore[reportMissingImports]  # path-injected above

REGISTRY_PATH = ROOT / "contracts" / "registry" / "phase1-silent.json"
SCHEMA_PATH = ROOT / "contracts" / "registry" / "schema.json"
Q1_FIXTURE_PATH = ROOT / "engine" / "headless" / "test" / "fixtures" / "q4-manifest-phase1.json"


def load_registry():
    with REGISTRY_PATH.open(encoding="utf-8") as handle:
        return json.load(handle)


def _schema_minor(registry: dict) -> int:
    return registry.get("manifest", {}).get("schema_version", {}).get("minor", 0)


class RegistryInvariantsTest(unittest.TestCase):
    def test_phase1_registry_is_valid(self):
        self.assertEqual([], validate_registry.validate(load_registry()))

    def test_schema_is_json(self):
        schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
        self.assertEqual("https://json-schema.org/draft/2020-12/schema", schema["$schema"])

    def test_duplicate_token_ids_are_rejected(self):
        registry = load_registry()
        registry["tokens"][1]["token_id"] = registry["tokens"][0]["token_id"]
        errors = validate_registry.validate(registry)
        self.assertTrue(any("duplicate token_id" in error for error in errors), errors)

    def test_deprecated_ids_cannot_be_reused_by_active_tokens(self):
        registry = load_registry()
        token = registry["tokens"][0]
        registry["deprecation_log"].append(
            {
                "token_id": token["token_id"],
                "deprecated_in_version": "phase1-silent.9",
                "reason": "test",
            }
        )
        errors = validate_registry.validate(registry)
        self.assertTrue(any("reused deprecated token_id" in error for error in errors), errors)

    def test_missing_token_references_are_rejected(self):
        registry = load_registry()
        registry["tokens"][0]["references"].append("card:MissingCard")
        registry["card_dsl"][0]["token"] = "card:MissingCard"
        errors = validate_registry.validate(registry)
        self.assertTrue(any("missing token reference" in error for error in errors), errors)
        self.assertTrue(any("card_dsl token missing" in error for error in errors), errors)

    def test_deprecation_log_versions_are_monotonic(self):
        registry = load_registry()
        registry["deprecation_log"] = [
            {"token_id": 9000, "deprecated_in_version": "phase1-silent.2", "reason": "later"},
            {"token_id": 9001, "deprecated_in_version": "phase1-silent.1", "reason": "earlier"},
        ]
        registry["tokens"].extend(
            [copy.deepcopy(registry["tokens"][0]), copy.deepcopy(registry["tokens"][0])]
        )
        registry["tokens"][-2].update(
            {
                "token_id": 9000,
                "token": "card:DeprecatedA",
                "name": "DeprecatedA",
                "deprecated_in": "phase1-silent.2",
            }
        )
        registry["tokens"][-1].update(
            {
                "token_id": 9001,
                "token": "card:DeprecatedB",
                "name": "DeprecatedB",
                "deprecated_in": "phase1-silent.1",
            }
        )
        errors = validate_registry.validate(registry)
        self.assertTrue(any("deprecation_log not monotonic" in error for error in errors), errors)

    def test_card_dsl_covers_and_parses_card_tokens(self):
        self.assertEqual([], validate_registry.validate_card_dsl(load_registry()))


# ---------------------------------------------------------------------------
# New invariant tests (wave-45/B.2)
# ---------------------------------------------------------------------------


def test_dsl_coherence():
    """DSL-coherence invariant: per-op field requirements.

    Skip at minor==0 (pre-fan-in stubs present).
    Assert no errors at minor==1.
    """
    registry = load_registry()
    minor = _schema_minor(registry)
    assert minor == 1, f"schema_version.minor must be 1 (post-wave-45/B.1 baseline); got {minor}"
    errors = validate_registry.validate(registry)
    coherence_errors = [e for e in errors if e.startswith("dsl_coherence:")]
    assert coherence_errors == [], coherence_errors


def test_no_stub_dsl():
    """Stub-rejection invariant: zero op=='stub' entries post-fan-in.

    Skip at minor==0 (98 stubs expected in current main).
    """
    registry = load_registry()
    minor = _schema_minor(registry)
    assert minor == 1, f"schema_version.minor must be 1 (post-wave-45/B.1 baseline); got {minor}"
    stub_count = sum(
        1
        for record in registry.get("card_dsl", [])
        for effect in record.get("effects", [])
        if effect.get("op") == "stub"
    )
    assert stub_count == 0, f"{stub_count} stub op(s) remain at minor==1"


def test_unknown_count_bounded():
    """Unknown-tolerance invariant: op=='unknown' count <= K_UNKNOWN_MAX (25).

    Skip at minor==0.
    """
    registry = load_registry()
    minor = _schema_minor(registry)
    assert minor == 1, f"schema_version.minor must be 1 (post-wave-45/B.1 baseline); got {minor}"
    unknown_count = sum(
        1
        for record in registry.get("card_dsl", [])
        for effect in record.get("effects", [])
        if effect.get("op") == "unknown"
    )
    assert unknown_count <= validate_registry.K_UNKNOWN_MAX, (
        f"{unknown_count} unknown ops exceed K_UNKNOWN_MAX={validate_registry.K_UNKNOWN_MAX}"
    )


def test_coverage_field_present():
    """Coverage-field invariant: every card_dsl entry has a valid coverage key.

    Skip at minor==0 (stub entries don't carry coverage).
    """
    registry = load_registry()
    minor = _schema_minor(registry)
    assert minor == 1, f"schema_version.minor must be 1 (post-wave-45/B.1 baseline); got {minor}"
    missing = [
        record.get("token", "<unknown>")
        for record in registry.get("card_dsl", [])
        if "coverage" not in record
    ]
    assert missing == [], f"card_dsl entries missing coverage field: {missing[:5]}"


def test_canonical_vs_q1_fixture_token_set():
    """Token-set comparison: canonical registry vs Q1 fixture (warn-only, Phase 3a ratchet).

    Emits warnings for each kind with a non-zero delta.
    Test always passes at this ratchet stage.
    """
    registry = load_registry()
    with Q1_FIXTURE_PATH.open(encoding="utf-8") as fh:
        q1 = json.load(fh)

    # Q1 fixture uses plain name strings; canonical uses "kind:Name" token strings.
    # Build per-kind sets from canonical.
    canonical_kinds: dict[str, set[str]] = {}
    for token in registry.get("tokens", []):
        kind = token.get("kind", "")
        # Exclude special tokens (per Amendment §4 n/a row).
        if kind == "special":
            continue
        canonical_kinds.setdefault(kind, set()).add(token["token"])

    # Q1 fixture key mapping: cards→card, relics→relic, powers→power,
    # monsters→enemy, potions→potion. No encounters key — treated as empty.
    q1_key_to_kind = {
        "cards": "card",
        "relics": "relic",
        "powers": "power",
        "monsters": "enemy",
        "potions": "potion",
    }

    for q1_key, kind in q1_key_to_kind.items():
        q1_names: set[str] = set(q1.get(q1_key, []))
        # Canonical tokens are prefixed "kind:Name"; strip prefix for comparison.
        canonical_names: set[str] = {t.split(":", 1)[1] for t in canonical_kinds.get(kind, set())}
        delta_canonical_only = canonical_names - q1_names
        delta_q1_only = q1_names - canonical_names
        if delta_canonical_only or delta_q1_only:
            warnings.warn(
                f"WARN: kind={kind} "
                f"delta_canonical-Q1={sorted(delta_canonical_only)[:5]} "
                f"delta_Q1-canonical={sorted(delta_q1_only)[:5]}",
                UserWarning,
                stacklevel=1,
            )
    # Always passes at Phase 3a (warn-only).


if __name__ == "__main__":
    unittest.main()
