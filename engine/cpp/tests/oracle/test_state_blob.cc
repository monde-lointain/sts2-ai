#include <gtest/gtest.h>

#include <cstdint>
#include <span>
#include <string>
#include <vector>

#include "sts2/oracle/adapter/state_blob.h"
#include "tests/oracle/adapter_fixtures.h"

namespace {

using sts2::oracle::adapter::ParsedStateBlob;
using sts2::oracle::adapter::read_state_blob;
using sts2::oracle::adapter::StateCodecError;
using sts2::oracle::adapter::tests::load_fixture_blob;

// Reproduce the canonical hash as a lowercase-hex string for comparison
// against fixture metadata expected_canonical_hash_hex (which is SHA-256
// over the raw .blob bytes — equivalently, the M1 trailer hash since the
// trailer-hash range = [start, trailer_magic) and the trailer is the last
// 36 bytes; sha256(blob_bytes) != trailer_sha256 (different ranges). We
// only assert the trailer hash agrees with our recomputation in the reader
// itself; the fixture-level canonical hash is asserted in the round-trip
// test against the bytes loaded from disk).
std::string to_hex_lower(std::span<const std::uint8_t> bytes) {
  static constexpr char kHex[] = "0123456789abcdef";
  std::string out;
  out.resize(bytes.size() * 2);
  for (std::size_t i = 0; i < bytes.size(); ++i) {
    out[2U * i] = kHex[bytes[i] >> 4];
    out[(2U * i) + 1] = kHex[bytes[i] & 0x0FU];
  }
  return out;
}

TEST(StateBlobReader, Fixture1_CultistsNormal_ParsesAndValidates) {
  const auto bytes = load_fixture_blob("01-cultists-normal-seed42");
  EXPECT_EQ(bytes.size(), 5575U);  // pinned by fixture metadata.json

  const ParsedStateBlob blob = read_state_blob(bytes);
  EXPECT_EQ(blob.magic, sts2::oracle::adapter::kStateCodecMagic);
  EXPECT_EQ(blob.schema, sts2::oracle::adapter::kStateCodecSchemaV3);
  // ManifestStamp present.
  EXPECT_FALSE(blob.stamp.git_sha.empty());
  // Trailer SHA-256 is the authoritative tamper-detection per
  // state-codec.md §Q2 oracle-adapter consumption invariant #3. The reader
  // validates it internally; if we got here without throwing, it matched.
  // Surface the hex for inspection.
  EXPECT_EQ(to_hex_lower(blob.trailer_sha256).size(), 64U);
  // CombatState was parsed: 2 cultists alive at smoke fixture boot.
  EXPECT_EQ(blob.combat_state.enemy_count, 2);
  ASSERT_EQ(blob.combat_state.enemies.size(), 2U);
  EXPECT_GT(blob.combat_state.enemies[0].current_hp, 0);
  EXPECT_GT(blob.combat_state.enemies[1].current_hp, 0);
}

TEST(StateBlobReader, Fixture1_AllFixturesParse) {
  // Smoke-parse all 6 D3 fixtures — magic / schema / trailer validate. The
  // CULTISTS_NORMAL projection happens in T3+; here we just verify the
  // reader handles every fixture's wire shape.
  const std::vector<std::string> dirs = {
      "01-cultists-normal-seed42",         "02-fossil-stalker-elite-seed42",
      "03-fossil-stalker-elite-seed1337",  "04-kaiser-crab-boss-seed42",
      "05-louse-progenitor-normal-seed42", "06-small-slimes-seed42",
  };
  for (const auto& d : dirs) {
    const auto bytes = load_fixture_blob(d);
    EXPECT_NO_THROW({
      const ParsedStateBlob blob = read_state_blob(bytes);
      EXPECT_EQ(blob.magic, sts2::oracle::adapter::kStateCodecMagic);
    }) << "fixture: "
       << d;
  }
}

TEST(StateBlobReader, CorruptedPayloadByte_Rejected) {
  // Flip a byte deep inside a section body; trailer SHA-256 mismatches and
  // the reader must reject.
  auto bytes = load_fixture_blob("01-cultists-normal-seed42");
  // Pick an offset well past the header, well before the trailer.
  const std::size_t mid = bytes.size() / 2U;
  bytes[mid] ^= 0xFFU;
  EXPECT_THROW(
      { [[maybe_unused]] auto r = read_state_blob(bytes); }, StateCodecError);
}

TEST(StateBlobReader, MagicMismatch_Rejected) {
  auto bytes = load_fixture_blob("01-cultists-normal-seed42");
  // First 4 bytes = magic. Corrupt any of them.
  bytes[0] = 0x00U;
  EXPECT_THROW(
      { [[maybe_unused]] auto r = read_state_blob(bytes); }, StateCodecError);
}

TEST(StateBlobReader, TruncatedBlob_Rejected) {
  auto bytes = load_fixture_blob("01-cultists-normal-seed42");
  bytes.resize(8);  // header-magic + schema only; trailer missing.
  EXPECT_THROW(
      { [[maybe_unused]] auto r = read_state_blob(bytes); }, StateCodecError);
}

TEST(StateBlobReader, SchemaMismatch_Rejected) {
  auto bytes = load_fixture_blob("01-cultists-normal-seed42");
  // schema u16 sits at offset 4 (after the 4-byte magic). Bump to a
  // disallowed value.
  bytes[4] = 0x42U;
  bytes[5] = 0x00U;
  EXPECT_THROW(
      { [[maybe_unused]] auto r = read_state_blob(bytes); }, StateCodecError);
}

}  // namespace
