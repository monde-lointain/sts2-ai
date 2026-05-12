import unittest

from tools.schema.compatibility import compare_contracts, parse_proto_contract


BASE_PROTO = """
syntax = "proto3";
// sts2.schema.major: 0
// sts2.schema.minor: 0
package sts2.q3.v0;
message Trajectory {
  SchemaVersion schema_version = 1;
  string trajectory_id = 2;
}
message SchemaVersion {
  uint32 major = 1;
  uint32 minor = 2;
}
"""

MINOR_ADDITION_PROTO = """
syntax = "proto3";
// sts2.schema.major: 0
// sts2.schema.minor: 1
package sts2.q3.v0;
message Trajectory {
  SchemaVersion schema_version = 1;
  string trajectory_id = 2;
  string source = 3;
}
message SchemaVersion {
  uint32 major = 1;
  uint32 minor = 2;
}
"""

MAJOR_MISMATCH_PROTO = """
syntax = "proto3";
// sts2.schema.major: 1
// sts2.schema.minor: 0
package sts2.q3.v1;
message Trajectory {
  SchemaVersion schema_version = 1;
  string trajectory_id = 2;
}
message SchemaVersion {
  uint32 major = 1;
  uint32 minor = 2;
}
"""

FIELD_REMOVAL_PROTO = """
syntax = "proto3";
// sts2.schema.major: 0
// sts2.schema.minor: 1
package sts2.q3.v0;
message Trajectory {
  SchemaVersion schema_version = 1;
}
message SchemaVersion {
  uint32 major = 1;
  uint32 minor = 2;
}
"""


class CompatibilityTest(unittest.TestCase):
    def test_minor_field_addition_is_compatible(self):
        baseline = parse_proto_contract("trajectory.proto", BASE_PROTO)
        candidate = parse_proto_contract("trajectory.proto", MINOR_ADDITION_PROTO)
        self.assertTrue(compare_contracts(baseline, candidate).ok)

    def test_major_mismatch_is_rejected(self):
        baseline = parse_proto_contract("trajectory.proto", BASE_PROTO)
        candidate = parse_proto_contract("trajectory.proto", MAJOR_MISMATCH_PROTO)
        result = compare_contracts(baseline, candidate)
        self.assertFalse(result.ok)
        self.assertIn("major version changed", "\n".join(result.messages))

    def test_field_removal_without_major_bump_is_rejected(self):
        baseline = parse_proto_contract("trajectory.proto", BASE_PROTO)
        candidate = parse_proto_contract("trajectory.proto", FIELD_REMOVAL_PROTO)
        result = compare_contracts(baseline, candidate)
        self.assertFalse(result.ok)
        self.assertIn("removed field", "\n".join(result.messages))


if __name__ == "__main__":
    unittest.main()
