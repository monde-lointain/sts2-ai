#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <string_view>

// Q2 verify-server wire protocol (Q2-ADR-003). JSON-over-Unix-socket cold-path
// request/response shape for Q12 (forward-laid; Q12 does not exist yet).
//
// The wire dialect is hand-rolled JSON (no nlohmann/json dependency, per
// Q2-ADR-001). Scope: flat object + one level of nested object; string,
// double, int64, bool values; no arrays in v1. See protocol.cc for the
// parser/serializer + base64 decoder implementations.

namespace sts2::oracle::verify_server {

inline constexpr std::string_view kProtocolVersion = "1";

// Reject reasons. Vocabulary fixed by Q2-ADR-003 §Phase-1A.
enum class RejectReason : std::uint8_t {
  kEncounterNotInCppEngine,   // AdapterReject (Q2-ADR-002).
  kBudgetExceeded,            // Reserved for Q12; never emitted in Phase-1A.
  kMalformedBlob,             // parse_envelope / read_state_blob threw.
  kMalformedRequest,          // JSON parse failure / missing fields.
  kProtocolVersionMismatch,   // request.protocol_version != "1".
  kUnknownPowerDiagnostic,    // Reserved (forward-laid).
};

[[nodiscard]] std::string_view reject_reason_to_wire(RejectReason r) noexcept;

// Request shape (per Q2-ADR-003).
//   {"protocol_version": "1",
//    "state_blob_b64": "<base64 of StateBlobEnvelope bytes>",
//    "budget": {"max_states_expanded": 100000, "deadline_ms": 1000}}
struct VerifyRequest {
  std::string protocol_version;
  std::string state_blob_b64;
  // Phase-1A: budget fields parsed but NOT enforced (Q2-ADR-003).
  std::int64_t budget_max_states_expanded = 0;
  std::int64_t budget_deadline_ms = 0;
};

// Action payload mirroring sts2::ai::transition::Action for the wire.
//   kind ∈ {"play_card", "end_turn"}
//   card_id is the canonical game::CardId .name from card_effects (e.g. "Strike")
struct ActionPayload {
  std::string kind;           // "play_card" or "end_turn"
  std::string card_id;        // populated for "play_card" only
  std::int32_t target_idx = 0;  // populated for "play_card" only
};

// Simulator manifest echoed from the parsed envelope (Q1-ADR-013
// cross-quantum-traceability anchor).
struct SimulatorManifestEcho {
  std::uint32_t schema_major = 0;
  std::uint32_t schema_minor = 0;
  std::string game_version;
  std::string simulator_build_sha;
  std::string registry_sha;
};

// verified=true response body.
struct VerifiedResponse {
  double expected_hp = 0.0;
  double expected_rounds = 0.0;
  ActionPayload action;
  bool expansion_complete = true;
  std::int64_t states_expanded = 0;
  std::string algorithm_sha;
  SimulatorManifestEcho simulator_manifest_echo;
};

// verified=false response body. `diagnostic_json` is a pre-serialized JSON
// object payload (no enclosing braces removed); the serializer writes it
// verbatim into the response under the "diagnostic" key.
struct RejectedResponse {
  RejectReason reason = RejectReason::kMalformedRequest;
  std::string diagnostic_json;  // raw JSON object literal, e.g. {"k":"v"}
};

// Parses a JSON request. Throws std::runtime_error on parse failure or
// missing required fields. Callers at the handler boundary catch and
// translate into a RejectedResponse with reason=kMalformedRequest.
[[nodiscard]] VerifyRequest parse_request(std::string_view request_json);

// Serializers. Each returns a complete JSON document terminated by no
// trailing whitespace.
[[nodiscard]] std::string serialize_verified_response(
    const VerifiedResponse& body);
[[nodiscard]] std::string serialize_rejected_response(
    const RejectedResponse& body);

// Base64 (RFC 4648 §4, standard alphabet, no URL-safe variant, with or
// without trailing '=' padding) decode. Throws std::runtime_error on
// invalid characters or invalid padding.
[[nodiscard]] std::string base64_decode(std::string_view input);

}  // namespace sts2::oracle::verify_server
