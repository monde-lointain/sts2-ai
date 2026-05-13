#pragma once

#include <cstddef>
#include <cstdint>
#include <map>
#include <stdexcept>
#include <string>
#include <string_view>
#include <variant>

// Minimal JSON parser supporting the Q2-ADR-003 request/response shapes:
//   - flat objects + one level of nested objects
//   - values: string, double, int64, bool, nested object
//   - no arrays, no null, no scientific notation in the request schema
//
// Hand-rolled per Q2-ADR-001 (no nlohmann/json dependency). Scope-limited:
// any production extension to support arrays / null / e-notation must update
// this header and add coverage in test_verify_server.cc.
//
// Errors throw std::runtime_error with a position-tagged message. The
// handler boundary in server.cc catches and translates to
// reason=kMalformedRequest.

namespace sts2::oracle::verify_server::detail {

class JsonValue;

using JsonObject = std::map<std::string, JsonValue, std::less<>>;

// Discriminated union. Order matters for variant index (kept stable for
// any future visitor that switches on index() rather than holds_alternative).
//   0: string
//   1: double
//   2: int64
//   3: bool
//   4: object
class JsonValue {
 public:
  enum class Kind : std::uint8_t {
    kString = 0,
    kDouble = 1,
    kInt64 = 2,
    kBool = 3,
    kObject = 4,
  };

  JsonValue() : v_(std::string{}) {}
  explicit JsonValue(std::string s) : v_(std::move(s)) {}
  explicit JsonValue(double d) : v_(d) {}
  explicit JsonValue(std::int64_t i) : v_(i) {}
  explicit JsonValue(bool b) : v_(b) {}
  explicit JsonValue(JsonObject o) : v_(std::move(o)) {}

  [[nodiscard]] Kind kind() const noexcept {
    return static_cast<Kind>(v_.index());
  }

  [[nodiscard]] const std::string& as_string() const { return std::get<0>(v_); }
  [[nodiscard]] double as_double() const { return std::get<1>(v_); }
  [[nodiscard]] std::int64_t as_int64() const { return std::get<2>(v_); }
  [[nodiscard]] bool as_bool() const { return std::get<3>(v_); }
  [[nodiscard]] const JsonObject& as_object() const { return std::get<4>(v_); }

 private:
  std::variant<std::string, double, std::int64_t, bool, JsonObject> v_;
};

// Parses a JSON object. The input must be a single top-level object;
// surrounding whitespace is tolerated. Throws std::runtime_error on any
// syntactic deviation from the scope above.
[[nodiscard]] JsonObject parse_object(std::string_view text);

// Helpers: lookup-with-type-check. Throw std::runtime_error if the key is
// missing OR the value is the wrong kind.
[[nodiscard]] const std::string& require_string(const JsonObject& obj,
                                                std::string_view key);
[[nodiscard]] std::int64_t require_int64(const JsonObject& obj,
                                         std::string_view key);
[[nodiscard]] const JsonObject& require_object(const JsonObject& obj,
                                               std::string_view key);

// Serializer helpers. The wire output is a flat-friendly hand-rolled
// formatter (no general JSON emitter); writers append fragments into a
// std::string buffer.
//
// Strings: escape only the characters JSON requires (\", \\, \b, \f, \n,
// \r, \t, plus \u00XX for control bytes < 0x20). All field values we emit
// are ASCII (build SHAs are lowercase hex; algorithm_sha is a fixed marker;
// card_id is from a closed enum; encounter_id / monster_ids are validated by
// the adapter; diagnostic strings come from std::exception::what(), which is
// implementation-defined but the standard library throws-with-ASCII), so
// the escape table only needs to cover the required minimum.
void append_json_string(std::string& out, std::string_view s);

// max_digits10 (17) preserves round-tripping for doubles. Used for both
// `expected_hp` and `expected_rounds` in the verified response.
void append_json_double(std::string& out, double d);

}  // namespace sts2::oracle::verify_server::detail
