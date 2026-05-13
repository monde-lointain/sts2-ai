#include "sts2/oracle/verify_server/server.h"

#include <cstdint>
#include <exception>
#include <sstream>
#include <string>
#include <string_view>
#include <variant>
#include <vector>

#include "oracle/verify_server/json_internal.h"
#include "sts2/ai/search.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/card_effects.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/adapter.h"
#include "sts2/oracle/adapter/diagnostic.h"
#include "sts2/oracle/adapter/envelope.h"
#include "sts2/oracle/adapter/manifest.h"
#include "sts2/oracle/verify_server/protocol.h"

namespace sts2::oracle::verify_server {

namespace {

using sts2::ai::CompactState;
using sts2::ai::Search;
using sts2::ai::SearchResult;
using sts2::ai::transition::Action;
using sts2::ai::transition::ActionKind;
using sts2::oracle::adapter::AdapterReject;
using sts2::oracle::adapter::AdapterResult;
using sts2::oracle::adapter::current_manifest;
using sts2::oracle::adapter::from_blob_payload;
using sts2::oracle::adapter::parse_envelope;
using sts2::oracle::adapter::ParsedEnvelope;
using sts2::oracle::adapter::UnsupportedEncounter;

[[nodiscard]] std::string escape_for_diagnostic(std::string_view raw) {
  // Diagnostic strings are embedded as JSON string values inside a hand-
  // assembled object literal. Reuse the protocol-side string escaper.
  std::string out;
  detail::append_json_string(out, raw);
  return out;
}

[[nodiscard]] std::string build_what_diagnostic(std::string_view what) {
  std::string out;
  out.push_back('{');
  out.append(R"("exception_what":)");
  out.append(escape_for_diagnostic(what));
  out.push_back('}');
  return out;
}

[[nodiscard]] std::string build_version_diagnostic(
    std::string_view got_version) {
  std::string out;
  out.push_back('{');
  out.append(R"("expected":")");
  out.append(kProtocolVersion);
  out.append(R"(","got":)");
  out.append(escape_for_diagnostic(got_version));
  out.push_back('}');
  return out;
}

[[nodiscard]] std::string build_unsupported_diagnostic(
    const UnsupportedEncounter& unsupported) {
  // Shape mirrors emit-sample-rows/main.cc format_diagnostic_json (proven
  // pattern). The wire monster_ids array uses JSON array syntax; this is
  // the only place the server emits an array, and the strings are validated
  // ASCII identifiers from the adapter's spawn-power expectation map.
  std::string out;
  out.push_back('{');
  out.append(R"("encounter_id":)");
  out.append(escape_for_diagnostic(unsupported.encounter_id));
  out.append(R"(,"monster_ids":[)");
  bool first = true;
  for (const auto& m : unsupported.monster_ids) {
    if (!first) {
      out.push_back(',');
    }
    out.append(escape_for_diagnostic(m));
    first = false;
  }
  out.append(R"(],"reason":)");
  out.append(escape_for_diagnostic(unsupported.reason));
  out.push_back('}');
  return out;
}

[[nodiscard]] SimulatorManifestEcho echo_from_envelope(
    const ParsedEnvelope& env) {
  return SimulatorManifestEcho{
      .schema_major = env.schema_major,
      .schema_minor = env.schema_minor,
      .game_version = env.game_version,
      .simulator_build_sha = env.simulator_build_sha,
      .registry_sha = env.registry_sha,
  };
}

[[nodiscard]] ActionPayload action_to_payload(const Action& a) {
  ActionPayload p;
  if (a.kind == ActionKind::kEndTurn) {
    p.kind = "end_turn";
    return p;
  }
  p.kind = "play_card";
  // card_id wire string mirrors the canonical CardEffect.name field
  // (game::CardId::kStrike -> "Strike", etc.). Matches emit-sample-rows
  // and the regression pin in test_cultists_search_pins.cc.
  if (a.card_id == sts2::game::CardId::kNone) {
    p.card_id = "";
  } else {
    p.card_id =
        std::string(sts2::game::card_effects::card_effect_for(a.card_id).name);
  }
  p.target_idx = a.target_idx.valid() ? a.target_idx.raw() : 0;
  return p;
}

[[nodiscard]] std::string reject(RejectReason reason,
                                 std::string diagnostic_json) {
  RejectedResponse body;
  body.reason = reason;
  body.diagnostic_json = std::move(diagnostic_json);
  return serialize_rejected_response(body);
}

}  // namespace

std::string handle_request(std::string_view request_json) {
  // -------------------------------------------------------------------------
  // 1. JSON parse + protocol-version check.
  // -------------------------------------------------------------------------
  VerifyRequest req;
  try {
    req = parse_request(request_json);
  } catch (const std::exception& e) {
    return reject(RejectReason::kMalformedRequest,
                  build_what_diagnostic(e.what()));
  }
  if (req.protocol_version != kProtocolVersion) {
    return reject(RejectReason::kProtocolVersionMismatch,
                  build_version_diagnostic(req.protocol_version));
  }

  // -------------------------------------------------------------------------
  // 2. base64 decode the state-blob field.
  // -------------------------------------------------------------------------
  std::string raw_envelope_bytes;
  try {
    raw_envelope_bytes = base64_decode(req.state_blob_b64);
  } catch (const std::exception& e) {
    // Malformed base64 is a request-shape problem (the field is not a valid
    // base64 string), not an M1-blob problem. Categorize as malformed_request.
    return reject(RejectReason::kMalformedRequest,
                  build_what_diagnostic(e.what()));
  }

  // -------------------------------------------------------------------------
  // 3. parse_envelope (StateBlobEnvelope) + extract payload + manifest echo.
  // -------------------------------------------------------------------------
  ParsedEnvelope env;
  try {
    const std::span<const std::uint8_t> bytes(
        reinterpret_cast<const std::uint8_t*>(raw_envelope_bytes.data()),
        raw_envelope_bytes.size());
    env = parse_envelope(bytes);
  } catch (const std::exception& e) {
    return reject(RejectReason::kMalformedBlob,
                  build_what_diagnostic(e.what()));
  }

  // -------------------------------------------------------------------------
  // 4. Run the adapter facade on the payload bytes.
  // -------------------------------------------------------------------------
  AdapterResult adapter_result;
  try {
    adapter_result = from_blob_payload(
        std::span<const std::uint8_t>(env.payload.data(), env.payload.size()));
  } catch (const std::exception& e) {
    return reject(RejectReason::kMalformedBlob,
                  build_what_diagnostic(e.what()));
  }

  if (adapter_result.index() == 1U) {
    const auto& reject_diag = std::get<AdapterReject>(adapter_result);
    return reject(RejectReason::kEncounterNotInCppEngine,
                  build_unsupported_diagnostic(reject_diag.unsupported));
  }

  // -------------------------------------------------------------------------
  // 5. Run Search::solve over the CompactState. Phase-1A budget fields parsed
  //    but not enforced (Q2-ADR-003); expansion_complete is always true.
  // -------------------------------------------------------------------------
  const auto& state = std::get<CompactState>(adapter_result);
  Search search;
  SearchResult result;
  try {
    result = search.solve(state);
  } catch (const std::exception& e) {
    // Search throwing is a real bug, not a wire-shape issue. Surface as a
    // malformed_blob diagnostic so the client sees the exception text.
    return reject(RejectReason::kMalformedBlob,
                  build_what_diagnostic(e.what()));
  }

  // -------------------------------------------------------------------------
  // 6. Build VerifiedResponse and serialize.
  // -------------------------------------------------------------------------
  VerifiedResponse body;
  body.expected_hp = result.score.expected_hp;
  body.expected_rounds = result.score.expected_rounds;
  body.action = action_to_payload(result.best_action);
  body.expansion_complete = true;
  body.states_expanded = static_cast<std::int64_t>(search.tt_size());
  body.algorithm_sha = current_manifest().algorithm_sha;
  body.simulator_manifest_echo = echo_from_envelope(env);
  return serialize_verified_response(body);
}

}  // namespace sts2::oracle::verify_server
