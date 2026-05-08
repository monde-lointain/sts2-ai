#include "sts2/app/args.h"

#include <cstdint>
#include <ostream>
#include <random>
#include <string>

namespace sts2::app {

bool parse_uint64(const std::string& s, std::uint64_t& out) {
  if (s.empty()) return false;
  std::uint64_t v = 0;
  for (char ch : s) {
    if (ch < '0' || ch > '9') return false;
    std::uint64_t next = v * 10 + static_cast<std::uint64_t>(ch - '0');
    if (next < v) return false;
    v = next;
  }
  out = v;
  return true;
}

bool parse_args(int argc, char** argv, std::uint64_t& seed_out,
                bool& seed_provided, std::ostream& err) {
  seed_provided = false;
  for (int i = 1; i < argc; ++i) {
    std::string arg = argv[i];
    if (arg == "--seed") {
      if (i + 1 >= argc) {
        err << "error: --seed requires a value\n";
        return false;
      }
      if (!parse_uint64(argv[i + 1], seed_out)) {
        err << "error: --seed value '" << argv[i + 1]
            << "' is not a valid uint64\n";
        return false;
      }
      seed_provided = true;
      ++i;
    } else {
      err << "error: unknown argument '" << arg << "'\n";
      return false;
    }
  }
  return true;
}

std::uint64_t random_seed() {
  std::random_device rd;
  std::uint64_t hi = static_cast<std::uint64_t>(rd());
  std::uint64_t lo = static_cast<std::uint64_t>(rd());
  return (hi << 32) | lo;
}

}  // namespace sts2::app
