#pragma once

#include <string>
#include <string_view>

// RFC 4648 §4 base64 decoder. Standard alphabet (A-Z a-z 0-9 + /). Padding
// with '=' is tolerated but not required (no-padding inputs are accepted as
// long as the input length resolves to a whole number of output bytes per
// the 4-chars-to-3-bytes mapping).
//
// Whitespace inside the input is rejected (the request schema generates
// base64 from a binary blob; line-wrapped variants are not part of the
// wire shape).

namespace sts2::oracle::verify_server::detail {

// Throws std::runtime_error on any character outside the standard alphabet,
// any padding character that isn't a trailing '=' (at most 2), or any
// input length that doesn't decode to a whole-byte payload.
[[nodiscard]] std::string base64_decode_impl(std::string_view input);

}  // namespace sts2::oracle::verify_server::detail
