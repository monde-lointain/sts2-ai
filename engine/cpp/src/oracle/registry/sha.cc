#include "sts2/oracle/registry/sha.h"

#include <cstdint>
#include <cstring>
#include <fstream>
#include <ios>
#include <span>
#include <stdexcept>
#include <string>
#include <vector>

#include "sha256_internal.h"

#ifndef STS2_CONTRACTS_ROOT
#error \
    "STS2_CONTRACTS_ROOT must be defined by the build (absolute path of contracts/)"
#endif

namespace sts2::oracle::registry {

std::string compute_registry_sha256(const std::filesystem::path& path) {
  std::ifstream in(path, std::ios::binary | std::ios::ate);
  if (!in) {
    throw std::runtime_error("compute_registry_sha256: cannot open file: " +
                             path.string());
  }
  const std::streamsize size = in.tellg();
  if (size < 0) {
    throw std::runtime_error("compute_registry_sha256: tellg failed on file: " +
                             path.string());
  }
  in.seekg(0, std::ios::beg);
  std::vector<std::uint8_t> bytes(static_cast<std::size_t>(size));
  if (size > 0) {
    // ifstream::read takes char*; bytes vector is uint8_t — reinterpret is
    // safe (object representation, not aliasing UB).
    in.read(reinterpret_cast<char*>(bytes.data()), size);
    if (!in) {
      throw std::runtime_error("compute_registry_sha256: read error on file: " +
                               path.string());
    }
  }
  const auto digest = detail::sha256(std::span<const std::uint8_t>(bytes));
  return detail::to_hex_lower(std::span<const std::uint8_t>(digest));
}

std::string current_phase1_registry_sha256() {
  // STS2_CONTRACTS_ROOT is the absolute path of `contracts/`, injected by
  // CMake (see engine/cpp/src/oracle/registry/CMakeLists.txt). Phase-1A
  // registry layout per Q2-ADR-005.
  const std::filesystem::path p = std::filesystem::path(STS2_CONTRACTS_ROOT) /
                                  "registry" / "phase1-silent.json";
  return compute_registry_sha256(p);
}

}  // namespace sts2::oracle::registry
