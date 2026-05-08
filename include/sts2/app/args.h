#pragma once

// Internal helpers for main.cc argument parsing. Test-only header. Not part of the public sts2::simulator API.

#include <cstdint>
#include <iosfwd>
#include <string>

namespace app {

bool parse_uint64(const std::string& s, std::uint64_t& out);
bool parse_args(int argc, char** argv, std::uint64_t& seed_out, bool& seed_provided, std::ostream& err);
std::uint64_t random_seed();

}
