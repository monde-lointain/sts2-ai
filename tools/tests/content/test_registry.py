import copy
import json
import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools" / "content"))

import validate_registry  # pyright: ignore[reportMissingImports]  # path-injected above

REGISTRY_PATH = ROOT / "contracts" / "registry" / "phase1-silent.json"
SCHEMA_PATH = ROOT / "contracts" / "registry" / "schema.json"


def load_registry():
    with REGISTRY_PATH.open(encoding="utf-8") as handle:
        return json.load(handle)


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


if __name__ == "__main__":
    unittest.main()
