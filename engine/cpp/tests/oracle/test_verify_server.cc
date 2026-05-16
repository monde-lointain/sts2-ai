#include <gtest/gtest.h>

#include <array>
#include <cmath>
#include <cstdint>
#include <iostream>
#include <span>
#include <string>
#include <string_view>
#include <vector>

#include "sts2/oracle/verify_server/protocol.h"
#include "sts2/oracle/verify_server/server.h"
#include "tests/oracle/adapter_fixtures.h"
#include "tests/oracle/envelope_test_helpers.h"

// In-process gtests for sts2::oracle_verify_server (Q2-ADR-003). Tests call
// handle_request directly — NO socket bind in tests (the AF_UNIX wrapper
// lives in tools/oracle-verify-server/main.cc and is exercised manually).
//
// Cases:
//   1. HappyPathCultistsNormal  (DISABLED — Search::solve over CULTISTS_NORMAL
//      is ~6 min Debug / ~3 min Release; joins make ci-slow wave gate via
//      --gtest_filter='*DISABLED_*'.)
//   2. RejectPathFossilStalker  (NON-DISABLED; adapter rejects pre-search.)
//   3. MalformedRequestNotJson  (NON-DISABLED.)
//   4. MalformedBlob            (NON-DISABLED.)
//   5. ProtocolVersionMismatch  (NON-DISABLED.)
//
// Synthetic envelope construction reuses the shared
// envelope_test_helpers.h scaffolding factored out of test_envelope.cc.

namespace {

using sts2::oracle::adapter::tests::load_fixture_blob;
using sts2::oracle::adapter::tests::envelope::encode_envelope;
using sts2::oracle::adapter::tests::envelope::EnvelopeFields;
using sts2::oracle::verify_server::handle_request;

// ---------------------------------------------------------------------------
// Helpers.
// ---------------------------------------------------------------------------

[[nodiscard]] std::string base64_encode(std::span<const std::uint8_t> bytes) {
  static constexpr char kAlphabet[] =
      "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  std::string out;
  out.reserve(((bytes.size() + 2U) / 3U) * 4U);
  std::size_t i = 0;
  while (i + 3U <= bytes.size()) {
    const std::uint32_t v = (static_cast<std::uint32_t>(bytes[i]) << 16) |
                            (static_cast<std::uint32_t>(bytes[i + 1]) << 8) |
                            (static_cast<std::uint32_t>(bytes[i + 2]));
    out.push_back(kAlphabet[(v >> 18) & 0x3FU]);
    out.push_back(kAlphabet[(v >> 12) & 0x3FU]);
    out.push_back(kAlphabet[(v >> 6) & 0x3FU]);
    out.push_back(kAlphabet[v & 0x3FU]);
    i += 3;
  }
  const std::size_t rem = bytes.size() - i;
  if (rem == 1U) {
    const std::uint32_t v = static_cast<std::uint32_t>(bytes[i]) << 16;
    out.push_back(kAlphabet[(v >> 18) & 0x3FU]);
    out.push_back(kAlphabet[(v >> 12) & 0x3FU]);
    out.push_back('=');
    out.push_back('=');
  } else if (rem == 2U) {
    const std::uint32_t v = (static_cast<std::uint32_t>(bytes[i]) << 16) |
                            (static_cast<std::uint32_t>(bytes[i + 1]) << 8);
    out.push_back(kAlphabet[(v >> 18) & 0x3FU]);
    out.push_back(kAlphabet[(v >> 12) & 0x3FU]);
    out.push_back(kAlphabet[(v >> 6) & 0x3FU]);
    out.push_back('=');
  }
  return out;
}

// Wraps a payload (the M1-binary state.blob bytes from a D3 fixture) in a
// synthetic v0.1 StateBlobEnvelope, base64-encodes the wrapped bytes, and
// builds a JSON verify request around them.
[[nodiscard]] std::string make_request_json(
    std::span<const std::uint8_t> payload, std::string_view protocol_version) {
  EnvelopeFields fields;
  fields.payload.assign(payload.begin(), payload.end());
  const auto envelope_bytes = encode_envelope(fields);
  const std::string b64 = base64_encode(std::span<const std::uint8_t>(
      envelope_bytes.data(), envelope_bytes.size()));
  std::string out;
  out.reserve(b64.size() + 256);
  out.append(R"({"protocol_version":")");
  out.append(protocol_version);
  out.append(R"(","state_blob_b64":")");
  out.append(b64);
  out.append(
      R"(","budget":{"max_states_expanded":100000,"deadline_ms":1000}})");
  return out;
}

// Tiny structural-only JSON helper: returns the substring between the quotes
// of the FIRST top-level "<key>":"<value>" pair. Coarse — fine for tests
// where the field appears once in a known shape.
[[nodiscard]] std::string find_string_field(std::string_view json,
                                            std::string_view key) {
  const std::string needle =
      std::string("\"") + std::string(key) + std::string("\":\"");
  const auto k = json.find(needle);
  if (k == std::string_view::npos) {
    return {};
  }
  const auto value_start = k + needle.size();
  const auto value_end = json.find('"', value_start);
  if (value_end == std::string_view::npos) {
    return {};
  }
  return std::string(json.substr(value_start, value_end - value_start));
}

[[nodiscard]] bool find_bool_field(std::string_view json,
                                   std::string_view key) {
  const std::string needle = std::string("\"") + std::string(key) + "\":";
  const auto k = json.find(needle);
  if (k == std::string_view::npos) {
    return false;
  }
  const auto value_start = k + needle.size();
  return json.compare(value_start, 4, "true") == 0;
}

// Naive double extractor for "expected_hp":<num>. Stops at ',', ']', or '}'.
[[nodiscard]] double find_double_field(std::string_view json,
                                       std::string_view key) {
  const std::string needle = std::string("\"") + std::string(key) + "\":";
  const auto k = json.find(needle);
  if (k == std::string_view::npos) {
    return std::nan("");
  }
  const auto value_start = k + needle.size();
  auto value_end = value_start;
  while (value_end < json.size() && json[value_end] != ',' &&
         json[value_end] != '}' && json[value_end] != ']') {
    ++value_end;
  }
  const std::string tok(json.substr(value_start, value_end - value_start));
  try {
    return std::stod(tok);
  } catch (...) {
    return std::nan("");
  }
}

}  // namespace

// ---------------------------------------------------------------------------
// 1. Happy path (DISABLED — slow Search::solve).
// ---------------------------------------------------------------------------
TEST(VerifyServer, DISABLED_HappyPathCultistsNormal) {
  const auto payload = load_fixture_blob("01-cultists-normal-seed42");
  const std::string req = make_request_json(
      std::span<const std::uint8_t>(payload.data(), payload.size()), "1");
  const std::string resp = handle_request(req);

  // Print the response so the manual `--gtest_also_run_disabled_tests` run
  // produces immediately-readable evidence of a verified=true outcome.
  std::cerr << "VerifyServer.HappyPathCultistsNormal response: " << resp
            << '\n';

  EXPECT_TRUE(find_bool_field(resp, "verified"));
  EXPECT_EQ(find_string_field(resp, "protocol_version"), "1");
  EXPECT_EQ(find_string_field(resp, "kind"), "play_card");

  // Pinned to fixture #1 (CULTISTS_NORMAL, seed 42) — matches
  // AdapterRoundtrip.DISABLED_Fixture1_AdapterPlusSearch_PinnedAgreement
  // (test_adapter_roundtrip.cc kFixture1ExpectedHp = 60.774403172281517).
  // The S4 dispatch quoted 40.9083 (the seed 0xC0FFEE
  // CultistsSearchPins value), but that pin is for make_starter_combat —
  // a different boot path than D3 fixture #1. The verify-server consumes
  // the fixture bytes via from_blob_payload, so the 60.7744 pin is the
  // correct dual-path anchor here. See S4-A dispatch-report deferral note.
  const double hp = find_double_field(resp, "expected_hp");
  EXPECT_NEAR(hp, 60.774403172281517, 1e-3)
      << "Search::solve over fixture #1 CompactState should match the "
         "existing AdapterRoundtrip pin (60.7744)";
  EXPECT_TRUE(find_bool_field(resp, "expansion_complete"));
  // simulator_manifest_echo carries the parsed-envelope schema fields.
  // schema_major == 0 && schema_minor == 1 means the synthetic envelope
  // round-tripped through parse_envelope and was forwarded into the
  // verified response (Q1-ADR-013 traceability anchor).
  const std::string& body = resp;
  // schema_major:0 appears in two places (the envelope is the only one
  // emitted), pin the literal substring directly.
  EXPECT_NE(body.find(R"("schema_major":0)"), std::string::npos);
  EXPECT_NE(body.find(R"("schema_minor":1)"), std::string::npos);
}

// ---------------------------------------------------------------------------
// 2. Adapter-reject path (fixture #2 — FossilStalkerElite).
// ---------------------------------------------------------------------------
TEST(VerifyServer, RejectPathFossilStalker) {
  const auto payload = load_fixture_blob("02-fossil-stalker-elite-seed42");
  const std::string req = make_request_json(
      std::span<const std::uint8_t>(payload.data(), payload.size()), "1");
  const std::string resp = handle_request(req);
  EXPECT_FALSE(find_bool_field(resp, "verified"));
  EXPECT_EQ(find_string_field(resp, "reason"), "encounter_not_in_cpp_engine");
  EXPECT_EQ(find_string_field(resp, "encounter_id"), "FossilStalkerElite");
}

// ---------------------------------------------------------------------------
// 3. JSON parse failure.
// ---------------------------------------------------------------------------
TEST(VerifyServer, MalformedRequestNotJson) {
  const std::string resp = handle_request("{this is not json");
  EXPECT_FALSE(find_bool_field(resp, "verified"));
  EXPECT_EQ(find_string_field(resp, "reason"), "malformed_request");
  EXPECT_NE(resp.find(R"("exception_what")"), std::string::npos);
}

// ---------------------------------------------------------------------------
// 4. Malformed M1 blob (valid request JSON, garbage state-blob bytes).
// ---------------------------------------------------------------------------
TEST(VerifyServer, MalformedBlob) {
  // 0xDE 0xAD 0xBE 0xEF base64 = "3q2+7w==".
  const std::string req =
      R"({"protocol_version":"1","state_blob_b64":"3q2+7w==","budget":{)"
      R"("max_states_expanded":1,"deadline_ms":1}})";
  const std::string resp = handle_request(req);
  EXPECT_FALSE(find_bool_field(resp, "verified"));
  EXPECT_EQ(find_string_field(resp, "reason"), "malformed_blob");
  EXPECT_NE(resp.find(R"("exception_what")"), std::string::npos);
}

// ---------------------------------------------------------------------------
// 5. Protocol version mismatch.
// ---------------------------------------------------------------------------
TEST(VerifyServer, ProtocolVersionMismatch) {
  // Use a real fixture payload so the request is otherwise valid — the only
  // failure mode under test is protocol_version != "1".
  const auto payload = load_fixture_blob("01-cultists-normal-seed42");
  const std::string req = make_request_json(
      std::span<const std::uint8_t>(payload.data(), payload.size()), "2");
  const std::string resp = handle_request(req);
  EXPECT_FALSE(find_bool_field(resp, "verified"));
  EXPECT_EQ(find_string_field(resp, "reason"), "protocol_version_mismatch");
  EXPECT_EQ(find_string_field(resp, "expected"), "1");
  EXPECT_EQ(find_string_field(resp, "got"), "2");
}
