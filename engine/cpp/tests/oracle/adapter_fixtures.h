#pragma once

#include <cstdint>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>
#include <vector>

// Test-only helper: loads a D3 fixture .blob file into a byte vector.
// Resolves paths relative to the source tree (engine/headless/test/fixtures/
// state-blobs/<subdir>/state.blob).
//
// The fixture corpus is byte-locked per the Q1 stability contract; tests
// must read but never write these files.

namespace sts2::oracle::adapter::tests {

inline std::filesystem::path fixtures_root() {
  // Tests run with cwd = build dir; the source tree is under PROJECT_SOURCE_DIR.
  // CMake target defines no preprocessor symbols, so we walk up from the
  // current source file's path: this header lives at
  //   <root>/engine/cpp/tests/oracle/adapter_fixtures.h
  // Fixture root:
  //   <root>/engine/headless/test/fixtures/state-blobs/
  const std::filesystem::path here(__FILE__);
  // here = .../engine/cpp/tests/oracle/adapter_fixtures.h
  // parent_path() x 4 = .../  (project root)
  std::filesystem::path root =
      here.parent_path().parent_path().parent_path().parent_path().parent_path();
  return root / "engine" / "headless" / "test" / "fixtures" / "state-blobs";
}

inline std::vector<std::uint8_t> load_fixture_blob(const std::string& subdir) {
  const auto path = fixtures_root() / subdir / "state.blob";
  std::ifstream f(path, std::ios::binary);
  if (!f) {
    throw std::runtime_error("fixture not found: " + path.string());
  }
  f.seekg(0, std::ios::end);
  const std::streamsize sz = f.tellg();
  f.seekg(0, std::ios::beg);
  std::vector<std::uint8_t> bytes(static_cast<std::size_t>(sz));
  f.read(reinterpret_cast<char*>(bytes.data()), sz);
  if (!f) {
    throw std::runtime_error("fixture read failed: " + path.string());
  }
  return bytes;
}

}  // namespace sts2::oracle::adapter::tests
