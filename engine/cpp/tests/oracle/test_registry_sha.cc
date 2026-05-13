#include <gtest/gtest.h>

#include <cstddef>
#include <filesystem>
#include <fstream>
#include <ios>
#include <stdexcept>
#include <string>
#include <string_view>
#include <system_error>

#include "sts2/oracle/registry/sha.h"

// Tests for the registry-SHA computation utility (S2-T1).
// Hermetic: each test creates + cleans up its own temp file under
// std::filesystem::temp_directory_path(). The Phase-1A registry SHA is
// validated by shape (64 lowercase-hex chars), not by exact value — the
// registry JSON content is allowed to change without breaking this test.

namespace {

using sts2::oracle::registry::compute_registry_sha256;
using sts2::oracle::registry::current_phase1_registry_sha256;

// RAII helper: writes content to a fresh temp file; deletes it on destruction.
class ScopedTempFile {
 public:
  ScopedTempFile(std::string_view tag, std::string_view content) {
    path_ = std::filesystem::temp_directory_path() /
            (std::string("sts2_registry_sha_") + std::string(tag) + ".tmp");
    std::ofstream out(path_, std::ios::binary | std::ios::trunc);
    out.write(content.data(),
              static_cast<std::streamsize>(content.size()));
  }
  ~ScopedTempFile() {
    std::error_code ec;
    std::filesystem::remove(path_, ec);
  }
  ScopedTempFile(const ScopedTempFile&) = delete;
  ScopedTempFile& operator=(const ScopedTempFile&) = delete;

  [[nodiscard]] const std::filesystem::path& path() const noexcept {
    return path_;
  }

 private:
  std::filesystem::path path_;
};

// SHA-256("hello\n") — 6 bytes: 'h','e','l','l','o',0x0a.
constexpr std::string_view kHelloNewlineSha256 =
    "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03";

// SHA-256("") — well-known empty-input digest.
constexpr std::string_view kEmptySha256 =
    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

TEST(RegistrySha, ComputeOnKnownContent_MatchesKnownDigest) {
  const ScopedTempFile f("hello", "hello\n");
  const std::string digest = compute_registry_sha256(f.path());
  EXPECT_EQ(digest, kHelloNewlineSha256);
  EXPECT_EQ(digest.size(), 64U);
}

TEST(RegistrySha, ComputeOnEmptyContent_MatchesKnownDigest) {
  const ScopedTempFile f("empty", "");
  const std::string digest = compute_registry_sha256(f.path());
  EXPECT_EQ(digest, kEmptySha256);
  EXPECT_EQ(digest.size(), 64U);
}

TEST(RegistrySha, ComputeOnMissingFile_ThrowsRuntimeError) {
  const auto missing = std::filesystem::temp_directory_path() /
                       "sts2_registry_sha_definitely_missing_xyz.tmp";
  std::error_code ec;
  std::filesystem::remove(missing, ec);  // ensure absent.
  EXPECT_THROW(compute_registry_sha256(missing), std::runtime_error);
}

TEST(RegistrySha, CurrentPhase1RegistrySha_ShapeIsLowercaseHex64) {
  const std::string digest = current_phase1_registry_sha256();
  ASSERT_EQ(digest.size(), 64U);
  for (char c : digest) {
    const bool is_hex_lower =
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
    EXPECT_TRUE(is_hex_lower) << "non-lowercase-hex char: " << c;
  }
}

TEST(RegistrySha, CurrentPhase1RegistrySha_MatchesDirectCompute) {
  const std::filesystem::path direct =
      std::filesystem::path(STS2_CONTRACTS_ROOT) / "registry" /
      "phase1-silent.json";
  ASSERT_TRUE(std::filesystem::exists(direct))
      << "phase1-silent.json missing at: " << direct.string();
  const std::string via_convenience = current_phase1_registry_sha256();
  const std::string via_compute = compute_registry_sha256(direct);
  EXPECT_EQ(via_convenience, via_compute);
}

}  // namespace
