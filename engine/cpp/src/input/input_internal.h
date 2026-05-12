#pragma once

// Internal helpers for input.cc. Test-only header. Not part of the public
// sts2::simulator API.

#include <string>

namespace sts2::input::detail {

std::string trim(const std::string& s);
bool parse_nonneg_int(const std::string& s, int& out);

}  // namespace sts2::input::detail
