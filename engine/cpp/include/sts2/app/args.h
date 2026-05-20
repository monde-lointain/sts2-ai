#pragma once

// Internal helpers for main.cc argument parsing. Test-only header. Not part of
// the public sts2::simulator API.

#include <cstdint>
#include <iosfwd>
#include <optional>
#include <string>

namespace sts2::app {

bool parse_uint64(const std::string& s, std::uint64_t& out);
// Parses `--seed <uint64>` and `--scenario <path>`. Either, both, or neither
// may be present. On error, writes a message to `err` and returns false.
// On success, populates seed_out + seed_provided (false if --seed absent) and
// scenario_path_out (nullopt if --scenario absent).
bool parse_args(int argc, char** argv, std::uint64_t& seed_out,
                bool& seed_provided,
                std::optional<std::string>& scenario_path_out,
                std::ostream& err);
std::uint64_t random_seed();

}  // namespace sts2::app
