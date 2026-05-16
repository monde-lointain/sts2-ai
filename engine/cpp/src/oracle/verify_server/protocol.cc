#include "sts2/oracle/verify_server/protocol.h"

#include <array>
#include <cctype>
#include <cstdint>
#include <cstdio>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>

#include "oracle/verify_server/base64_internal.h"
#include "oracle/verify_server/json_internal.h"

namespace sts2::oracle::verify_server {

namespace {

// --------------------------------------------------------------------------
// JSON parser (subset; see json_internal.h).
// --------------------------------------------------------------------------
class JsonParser {
 public:
  explicit JsonParser(std::string_view text) : text_(text) {}

  detail::JsonObject parse_top_object() {
    skip_ws();
    auto obj = parse_object();
    skip_ws();
    if (pos_ != text_.size()) {
      throw_at("trailing content after top-level object");
    }
    return obj;
  }

 private:
  std::string_view text_;
  std::size_t pos_ = 0;

  [[noreturn]] void throw_at(std::string_view msg) const {
    std::ostringstream os;
    os << "verify-server JSON parse error at byte " << pos_ << ": " << msg;
    throw std::runtime_error(os.str());
  }

  void skip_ws() {
    while (pos_ < text_.size()) {
      const char c = text_[pos_];
      if (c == ' ' || c == '\t' || c == '\n' || c == '\r') {
        ++pos_;
      } else {
        return;
      }
    }
  }

  void expect(char c) {
    skip_ws();
    if (pos_ >= text_.size() || text_[pos_] != c) {
      std::ostringstream os;
      os << "expected '" << c << "'";
      throw_at(os.str());
    }
    ++pos_;
  }

  detail::JsonObject parse_object() {
    expect('{');
    detail::JsonObject out;
    skip_ws();
    if (pos_ < text_.size() && text_[pos_] == '}') {
      ++pos_;
      return out;
    }
    while (true) {
      skip_ws();
      std::string key = parse_string();
      expect(':');
      skip_ws();
      detail::JsonValue value = parse_value();
      out.emplace(std::move(key), std::move(value));
      skip_ws();
      if (pos_ >= text_.size()) {
        throw_at("unexpected EOF inside object");
      }
      const char c = text_[pos_];
      if (c == ',') {
        ++pos_;
        continue;
      }
      if (c == '}') {
        ++pos_;
        return out;
      }
      throw_at("expected ',' or '}'");
    }
  }

  detail::JsonValue parse_value() {
    skip_ws();
    if (pos_ >= text_.size()) {
      throw_at("unexpected EOF expecting value");
    }
    const char c = text_[pos_];
    if (c == '"') {
      return detail::JsonValue(parse_string());
    }
    if (c == '{') {
      return detail::JsonValue(parse_object());
    }
    if (c == 't' || c == 'f') {
      return detail::JsonValue(parse_bool());
    }
    if (c == '-' || (c >= '0' && c <= '9')) {
      return parse_number();
    }
    throw_at("unrecognized value start");
  }

  std::string parse_string() {
    expect('"');
    std::string out;
    while (pos_ < text_.size()) {
      const char c = text_[pos_];
      if (c == '"') {
        ++pos_;
        return out;
      }
      if (c == '\\') {
        ++pos_;
        if (pos_ >= text_.size()) {
          throw_at("EOF inside escape");
        }
        const char e = text_[pos_++];
        switch (e) {
          case '"':
            out.push_back('"');
            break;
          case '\\':
            out.push_back('\\');
            break;
          case '/':
            out.push_back('/');
            break;
          case 'b':
            out.push_back('\b');
            break;
          case 'f':
            out.push_back('\f');
            break;
          case 'n':
            out.push_back('\n');
            break;
          case 'r':
            out.push_back('\r');
            break;
          case 't':
            out.push_back('\t');
            break;
          case 'u': {
            if (pos_ + 4 > text_.size()) {
              throw_at("truncated \\u escape");
            }
            std::uint32_t code = 0;
            for (int i = 0; i < 4; ++i) {
              const char h = text_[pos_ + static_cast<std::size_t>(i)];
              std::uint32_t nibble = 0;
              if (h >= '0' && h <= '9') {
                nibble = static_cast<std::uint32_t>(h - '0');
              } else if (h >= 'a' && h <= 'f') {
                nibble = static_cast<std::uint32_t>(h - 'a' + 10);
              } else if (h >= 'A' && h <= 'F') {
                nibble = static_cast<std::uint32_t>(h - 'A' + 10);
              } else {
                throw_at("invalid hex in \\u escape");
              }
              code = (code << 4) | nibble;
            }
            pos_ += 4;
            // Encode codepoint as UTF-8. Surrogate pairs not supported in
            // Phase-1A (request schema does not require them).
            if (code < 0x80U) {
              out.push_back(static_cast<char>(code));
            } else if (code < 0x800U) {
              out.push_back(static_cast<char>(0xC0U | (code >> 6)));
              out.push_back(static_cast<char>(0x80U | (code & 0x3FU)));
            } else if (code >= 0xD800U && code <= 0xDFFFU) {
              throw_at("surrogate pair \\u escapes unsupported");
            } else {
              out.push_back(static_cast<char>(0xE0U | (code >> 12)));
              out.push_back(static_cast<char>(0x80U | ((code >> 6) & 0x3FU)));
              out.push_back(static_cast<char>(0x80U | (code & 0x3FU)));
            }
            break;
          }
          default:
            throw_at("unknown escape character");
        }
        continue;
      }
      if (static_cast<unsigned char>(c) < 0x20) {
        throw_at("unescaped control character in string");
      }
      out.push_back(c);
      ++pos_;
    }
    throw_at("EOF inside string");
  }

  bool parse_bool() {
    if (text_.compare(pos_, 4, "true") == 0) {
      pos_ += 4;
      return true;
    }
    if (text_.compare(pos_, 5, "false") == 0) {
      pos_ += 5;
      return false;
    }
    throw_at("invalid bool literal");
  }

  // Numbers: signed integers parse to int64; anything with a '.' parses to
  // double. Scientific notation is rejected (not in Phase-1A request schema).
  detail::JsonValue parse_number() {
    const std::size_t start = pos_;
    bool is_float = false;
    if (text_[pos_] == '-') {
      ++pos_;
    }
    while (pos_ < text_.size()) {
      const char c = text_[pos_];
      if (c >= '0' && c <= '9') {
        ++pos_;
      } else if (c == '.') {
        is_float = true;
        ++pos_;
      } else if (c == 'e' || c == 'E') {
        // Reject e-notation in numbers per the Phase-1A scope. If a future
        // request needs it (e.g. ints emitted by another language), this
        // branch needs to fall through to is_float and consume the exponent.
        throw_at("exponential notation unsupported");
      } else {
        break;
      }
    }
    if (pos_ == start || (pos_ == start + 1 && text_[start] == '-')) {
      throw_at("empty number");
    }
    const std::string tok(text_.substr(start, pos_ - start));
    if (is_float) {
      try {
        const double d = std::stod(tok);
        return detail::JsonValue(d);
      } catch (const std::exception& e) {
        std::ostringstream os;
        os << "invalid double literal: " << e.what();
        throw_at(os.str());
      }
    }
    try {
      const std::int64_t i = std::stoll(tok);
      return detail::JsonValue(i);
    } catch (const std::exception& e) {
      std::ostringstream os;
      os << "invalid int64 literal: " << e.what();
      throw_at(os.str());
    }
  }
};

}  // namespace

namespace detail {

JsonObject parse_object(std::string_view text) {
  JsonParser p(text);
  return p.parse_top_object();
}

const std::string& require_string(const JsonObject& obj, std::string_view key) {
  const auto it = obj.find(key);
  if (it == obj.end()) {
    throw std::runtime_error("verify-server: missing required string field '" +
                             std::string(key) + "'");
  }
  if (it->second.kind() != JsonValue::Kind::kString) {
    throw std::runtime_error("verify-server: field '" + std::string(key) +
                             "' is not a string");
  }
  return it->second.as_string();
}

std::int64_t require_int64(const JsonObject& obj, std::string_view key) {
  const auto it = obj.find(key);
  if (it == obj.end()) {
    throw std::runtime_error("verify-server: missing required int64 field '" +
                             std::string(key) + "'");
  }
  if (it->second.kind() != JsonValue::Kind::kInt64) {
    throw std::runtime_error("verify-server: field '" + std::string(key) +
                             "' is not an int64");
  }
  return it->second.as_int64();
}

const JsonObject& require_object(const JsonObject& obj, std::string_view key) {
  const auto it = obj.find(key);
  if (it == obj.end()) {
    throw std::runtime_error("verify-server: missing required object field '" +
                             std::string(key) + "'");
  }
  if (it->second.kind() != JsonValue::Kind::kObject) {
    throw std::runtime_error("verify-server: field '" + std::string(key) +
                             "' is not an object");
  }
  return it->second.as_object();
}

void append_json_string(std::string& out, std::string_view s) {
  out.push_back('"');
  for (const char c : s) {
    const auto uc = static_cast<unsigned char>(c);
    switch (c) {
      case '"':
        out.append("\\\"");
        break;
      case '\\':
        out.append("\\\\");
        break;
      case '\b':
        out.append("\\b");
        break;
      case '\f':
        out.append("\\f");
        break;
      case '\n':
        out.append("\\n");
        break;
      case '\r':
        out.append("\\r");
        break;
      case '\t':
        out.append("\\t");
        break;
      default:
        if (uc < 0x20U) {
          char buf[8];
          // NOLINTBEGIN(cppcoreguidelines-pro-type-vararg)
          // snprintf for hex escape; no std::format in project.
          std::snprintf(buf, sizeof(buf), "\\u%04x", static_cast<unsigned>(uc));
          // NOLINTEND(cppcoreguidelines-pro-type-vararg)
          out.append(buf);
        } else {
          out.push_back(c);
        }
    }
  }
  out.push_back('"');
}

void append_json_double(std::string& out, double d) {
  // max_digits10 = 17 round-trips IEEE 754 doubles in stdc++.
  char buf[64];
  // NOLINTBEGIN(cppcoreguidelines-pro-type-vararg)
  // snprintf for float precision; no std::format in project.
  const int n = std::snprintf(buf, sizeof(buf), "%.17g", d);
  // NOLINTEND(cppcoreguidelines-pro-type-vararg)
  if (n <= 0 || static_cast<std::size_t>(n) >= sizeof(buf)) {
    throw std::runtime_error("verify-server: double-format buffer overflow");
  }
  out.append(buf, static_cast<std::size_t>(n));
}

// --------------------------------------------------------------------------
// base64 decoder.
// --------------------------------------------------------------------------
namespace {

constexpr int decode_b64_char(char c) noexcept {
  if (c >= 'A' && c <= 'Z') {
    return c - 'A';
  }
  if (c >= 'a' && c <= 'z') {
    return c - 'a' + 26;
  }
  if (c >= '0' && c <= '9') {
    return c - '0' + 52;
  }
  if (c == '+') {
    return 62;
  }
  if (c == '/') {
    return 63;
  }
  return -1;
}

}  // namespace

std::string base64_decode_impl(std::string_view input) {
  // Strip trailing '=' padding (count must be 0, 1, or 2).
  std::size_t end = input.size();
  std::size_t pad = 0;
  while (end > 0 && input[end - 1] == '=') {
    --end;
    ++pad;
    if (pad > 2) {
      throw std::runtime_error("verify-server: base64 padding > 2");
    }
  }
  const std::size_t core_len = end;
  // Without padding, core_len % 4 must be 0, 2, or 3 (1 is impossible).
  if (core_len % 4U == 1U) {
    throw std::runtime_error("verify-server: base64 truncated input");
  }
  std::string out;
  out.reserve((core_len / 4U) * 3U + 3U);
  std::uint32_t buf = 0;
  int buf_bits = 0;
  for (std::size_t i = 0; i < core_len; ++i) {
    const char c = input[i];
    const int v = decode_b64_char(c);
    if (v < 0) {
      throw std::runtime_error(
          std::string("verify-server: invalid base64 character"));
    }
    buf = (buf << 6) | static_cast<std::uint32_t>(v);
    buf_bits += 6;
    if (buf_bits >= 8) {
      buf_bits -= 8;
      out.push_back(static_cast<char>((buf >> buf_bits) & 0xFFU));
    }
  }
  // Remaining bits MUST be zero (RFC 4648 §3.5). Tolerate non-canonical
  // encodings? Phase-1A: strict. The server never emits base64, so the
  // input must come from a conforming encoder.
  if (buf_bits > 0 && (buf & ((1U << buf_bits) - 1U)) != 0) {
    throw std::runtime_error("verify-server: base64 non-zero trailing bits");
  }
  return out;
}

}  // namespace detail

// --------------------------------------------------------------------------
// Public API.
// --------------------------------------------------------------------------

std::string_view reject_reason_to_wire(RejectReason r) noexcept {
  switch (r) {
    case RejectReason::kEncounterNotInCppEngine:
      return "encounter_not_in_cpp_engine";
    case RejectReason::kBudgetExceeded:
      return "budget_exceeded";
    case RejectReason::kMalformedBlob:
      return "malformed_blob";
    case RejectReason::kMalformedRequest:
      return "malformed_request";
    case RejectReason::kProtocolVersionMismatch:
      return "protocol_version_mismatch";
    case RejectReason::kUnknownPowerDiagnostic:
      return "unknown_power_diagnostic";
  }
  return "malformed_request";
}

std::string base64_decode(std::string_view input) {
  return detail::base64_decode_impl(input);
}

VerifyRequest parse_request(std::string_view request_json) {
  const detail::JsonObject obj = detail::parse_object(request_json);
  VerifyRequest req;
  req.protocol_version = detail::require_string(obj, "protocol_version");
  req.state_blob_b64 = detail::require_string(obj, "state_blob_b64");
  // budget is required by the Phase-1A request shape (fields parsed but not
  // enforced — see Q2-ADR-003).
  const auto& budget = detail::require_object(obj, "budget");
  req.budget_max_states_expanded =
      detail::require_int64(budget, "max_states_expanded");
  req.budget_deadline_ms = detail::require_int64(budget, "deadline_ms");
  return req;
}

namespace {

void append_action(std::string& out, const ActionPayload& a) {
  out.append(R"("action":{)");
  out.append(R"("kind":)");
  detail::append_json_string(out, a.kind);
  if (a.kind == "play_card") {
    out.append(R"(,"card_id":)");
    detail::append_json_string(out, a.card_id);
    out.append(R"(,"target_idx":)");
    out.append(std::to_string(a.target_idx));
  }
  out.push_back('}');
}

void append_manifest_echo(std::string& out, const SimulatorManifestEcho& m) {
  out.append(R"("simulator_manifest_echo":{)");
  out.append(R"("schema_major":)");
  out.append(std::to_string(m.schema_major));
  out.append(R"(,"schema_minor":)");
  out.append(std::to_string(m.schema_minor));
  out.append(R"(,"game_version":)");
  detail::append_json_string(out, m.game_version);
  out.append(R"(,"simulator_build_sha":)");
  detail::append_json_string(out, m.simulator_build_sha);
  out.append(R"(,"registry_sha":)");
  detail::append_json_string(out, m.registry_sha);
  out.push_back('}');
}

}  // namespace

std::string serialize_verified_response(const VerifiedResponse& body) {
  std::string out;
  out.reserve(512);
  out.push_back('{');
  out.append(R"("protocol_version":)");
  detail::append_json_string(out, kProtocolVersion);
  out.append(R"(,"verified":true,"value":{)");
  out.append(R"("expected_hp":)");
  detail::append_json_double(out, body.expected_hp);
  out.append(R"(,"expected_rounds":)");
  detail::append_json_double(out, body.expected_rounds);
  out.append("},");
  append_action(out, body.action);
  out.append(R"(,"expansion_complete":)");
  out.append(body.expansion_complete ? "true" : "false");
  out.append(R"(,"states_expanded":)");
  out.append(std::to_string(body.states_expanded));
  out.append(R"(,"algorithm_sha":)");
  detail::append_json_string(out, body.algorithm_sha);
  out.push_back(',');
  append_manifest_echo(out, body.simulator_manifest_echo);
  out.push_back('}');
  return out;
}

std::string serialize_rejected_response(const RejectedResponse& body) {
  std::string out;
  out.reserve(256);
  out.push_back('{');
  out.append(R"("protocol_version":)");
  detail::append_json_string(out, kProtocolVersion);
  out.append(R"(,"verified":false,"reason":)");
  detail::append_json_string(out, reject_reason_to_wire(body.reason));
  out.append(R"(,"diagnostic":)");
  if (body.diagnostic_json.empty()) {
    out.append("{}");
  } else {
    out.append(body.diagnostic_json);
  }
  out.push_back('}');
  return out;
}

}  // namespace sts2::oracle::verify_server
