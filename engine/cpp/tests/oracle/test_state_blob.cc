#include <gtest/gtest.h>

#include <array>
#include <cstdint>
#include <span>
#include <string>
#include <vector>

#include "oracle/adapter/sha256_internal.h"
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
  EXPECT_EQ(bytes.size(),
            5762U);  // pinned by fixture metadata.json (wave-38/B v4 regen)

  const ParsedStateBlob blob = read_state_blob(bytes);
  EXPECT_EQ(blob.magic, sts2::oracle::adapter::kStateCodecMagic);
  EXPECT_EQ(blob.schema, sts2::oracle::adapter::kStateCodecSchemaV4);
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

// ---------------------------------------------------------------------------
// Synthetic v4 wire-path tests. These hand-roll the binary blob to cover the
// AppliesPowers read path (all production fixtures have AppliesCount=0, giving
// zero positive coverage of Target reads before this wave).
//
// Helper is local to this TU (anonymous namespace); ~70 LOC. If reused in
// future tests, extract to a shared test helper — deferred per R13 scope.
// ---------------------------------------------------------------------------

namespace {  // NOLINT(google-build-namespaces) — inner anon inside outer anon

// Emit a u32 little-endian.
static void push_u32(std::vector<std::uint8_t>& v, std::uint32_t val) {
  v.push_back(static_cast<std::uint8_t>(val & 0xFFU));
  v.push_back(static_cast<std::uint8_t>((val >> 8) & 0xFFU));
  v.push_back(static_cast<std::uint8_t>((val >> 16) & 0xFFU));
  v.push_back(static_cast<std::uint8_t>((val >> 24) & 0xFFU));
}

// Emit a u16 little-endian.
static void push_u16(std::vector<std::uint8_t>& v, std::uint16_t val) {
  v.push_back(static_cast<std::uint8_t>(val & 0xFFU));
  v.push_back(static_cast<std::uint8_t>((val >> 8) & 0xFFU));
}

// Emit a i32 little-endian.
static void push_i32(std::vector<std::uint8_t>& v, std::int32_t val) {
  push_u32(v, static_cast<std::uint32_t>(val));
}

// Emit a u32-length-prefixed UTF-8 string.
static void push_lp_string_u32(std::vector<std::uint8_t>& v,
                               const std::string& s) {
  push_u32(v, static_cast<std::uint32_t>(s.size()));
  v.insert(v.end(), s.begin(), s.end());
}

// Build a v4 state blob with exactly 1 enemy carrying the given intent.
// Player is minimal (no powers, no intent). Card piles are empty.
// Rng + Tokens sections are minimal. The SHA-256 trailer is computed correctly.
struct AppliesPowerSpec {
  std::string power_id;
  std::int32_t stacks = 0;
  std::int32_t target = 0;
};

static std::vector<std::uint8_t> build_synthetic_blob_with_intent(
    std::int32_t kind, std::int32_t damage, std::int32_t hits,
    const std::vector<AppliesPowerSpec>& applies, std::int32_t self_block_gain,
    const std::string& move_id) {
  // Build CombatState section payload.
  std::vector<std::uint8_t> cs;

  // TurnCounter, Phase.
  push_i32(cs, 1);
  push_i32(cs, 0);

  // Player creature (id=0, name="Player", hp=70, max_hp=70, block=0,
  //   power_count=0, intent_present=0, is_player=1).
  push_u32(cs, 0U);                  // id
  push_lp_string_u32(cs, "Player");  // name
  push_i32(cs, 70);                  // current_hp
  push_i32(cs, 70);                  // max_hp
  push_i32(cs, 0);                   // block
  push_i32(cs, 0);                   // power_count
  cs.push_back(0U);                  // intent_present = false
  cs.push_back(1U);                  // is_player = true

  // EnemyCount = 1.
  push_i32(cs, 1);

  // Enemy creature (id=1, name="Enemy", hp=50, max_hp=50, block=0,
  //   power_count=0, intent_present=1, then intent, is_player=0).
  push_u32(cs, 1U);                 // id
  push_lp_string_u32(cs, "Enemy");  // name
  push_i32(cs, 50);                 // current_hp
  push_i32(cs, 50);                 // max_hp
  push_i32(cs, 0);                  // block
  push_i32(cs, 0);                  // power_count
  cs.push_back(1U);                 // intent_present = true

  // MonsterIntent v4 layout.
  push_i32(cs, kind);
  push_i32(cs, damage);
  push_i32(cs, hits);
  push_i32(cs, static_cast<std::int32_t>(applies.size()));
  for (const auto& ap : applies) {
    push_lp_string_u32(cs, ap.power_id);
    push_i32(cs, ap.stacks);
    push_i32(cs, ap.target);
  }
  push_i32(cs, self_block_gain);
  push_lp_string_u32(cs, move_id);

  cs.push_back(0U);  // is_player = false (enemy)

  // Energy, BaseEnergyPerTurn, HandDrawSize.
  push_i32(cs, 3);
  push_i32(cs, 3);
  push_i32(cs, 5);
  // 4 empty card piles: DrawPile, HandPile, DiscardPile, ExhaustPile.
  for (int i = 0; i < 4; ++i) {
    push_i32(cs, 0);
  }
  // PlayerRngCounter, MonsterRngCounter.
  push_i32(cs, 0);
  push_i32(cs, 0);
  // AttacksPlayedThisTurn, CardsDrawnThisCombat (v2).
  push_i32(cs, 0);
  push_i32(cs, 0);
  // LastSpentEnergy, ExhaustedShivCount (v3).
  push_i32(cs, 0);
  push_i32(cs, 0);

  // Build minimal Rng section payload.
  std::vector<std::uint8_t> rng;
  rng.push_back(1U);  // run_blob_present=1
  push_u32(rng, 0U);  // run_blob_len=0
  rng.push_back(1U);  // player_blob_present=1
  push_u32(rng, 0U);  // player_blob_len=0

  // Build minimal Tokens section payload (count=0).
  std::vector<std::uint8_t> tokens;
  push_u32(tokens, 0U);

  // Build minimal ManifestStamp.
  std::vector<std::uint8_t> stamp;
  // git_sha: u8-prefixed ""
  stamp.push_back(0U);
  // build_id: u16-prefixed ""
  push_u16(stamp, 0U);
  // content_hash: 32 zero bytes
  for (int i = 0; i < 32; ++i) {
    stamp.push_back(0U);
  }

  // Assemble the full blob (pre-trailer).
  std::vector<std::uint8_t> blob;
  // Header: magic, schema=4, header_size=stamp.size().
  push_u32(blob, 0x53435443U);  // kStateCodecMagic
  push_u16(blob, 4U);           // schema v4
  push_u16(blob, static_cast<std::uint16_t>(stamp.size()));
  blob.insert(blob.end(), stamp.begin(), stamp.end());

  // Rng section.
  push_u16(blob, 0U);  // section_id = Rng
  push_u32(blob, static_cast<std::uint32_t>(rng.size()));
  blob.insert(blob.end(), rng.begin(), rng.end());

  // Tokens section.
  push_u16(blob, 1U);  // section_id = Tokens
  push_u32(blob, static_cast<std::uint32_t>(tokens.size()));
  blob.insert(blob.end(), tokens.begin(), tokens.end());

  // CombatState section.
  push_u16(blob, 2U);  // section_id = CombatState
  push_u32(blob, static_cast<std::uint32_t>(cs.size()));
  blob.insert(blob.end(), cs.begin(), cs.end());

  // Terminator.
  push_u16(blob, 0xFFFFU);

  // Trailer: trailer_magic + sha256(blob[0..trailer_magic_offset)).
  // SHA-256 is computed over bytes BEFORE the trailer magic, per reader:
  //   computed = sha256(bytes.subspan(0, trailer_start))
  // where trailer_start is the offset of the first trailer byte (magic).
  const auto hash = sts2::oracle::adapter::detail::sha256(
      std::span<const std::uint8_t>(blob.data(), blob.size()));
  push_u32(blob, 0x53544354U);  // kStateCodecTrailerMagic — AFTER hash input
  blob.insert(blob.end(), hash.begin(), hash.end());

  return blob;
}

}  // namespace

TEST(MonsterIntentV4Wire, AppliesCount1_TargetSelf_RoundTrips) {
  // Construct a synthetic v4 blob with one creature whose intent has
  // AppliesPowers=[{Ritual, 3, Self=0}], SelfBlockGain=0, MoveId="RITUAL_MOVE".
  const auto bytes = build_synthetic_blob_with_intent(
      /*kind=*/4 /*Buff*/, /*damage=*/0, /*hits=*/0,
      /*applies=*/{{"Ritual", 3, 0 /*Self*/}}, /*self_block_gain=*/0,
      /*move_id=*/"RITUAL_MOVE");
  const ParsedStateBlob blob = read_state_blob(bytes);
  ASSERT_EQ(blob.combat_state.enemies.size(), 1U);
  const auto& cr = blob.combat_state.enemies[0];
  ASSERT_TRUE(cr.intent_present);
  ASSERT_EQ(cr.intent.applies_powers.size(), 1U);
  EXPECT_EQ(cr.intent.applies_powers[0].power_id, "Ritual");
  EXPECT_EQ(cr.intent.applies_powers[0].stacks, 3);
  EXPECT_EQ(cr.intent.applies_powers[0].target, 0);  // Self
  EXPECT_EQ(cr.intent.self_block_gain, 0);
  EXPECT_EQ(cr.intent.move_id, "RITUAL_MOVE");
}

TEST(MonsterIntentV4Wire, AppliesCount2_TargetMix_SelfBlockGain_RoundTrips) {
  // AppliesPowers=[{Frail,2,Player=1},{Weak,2,Player=1}], SelfBlockGain=5.
  const auto bytes = build_synthetic_blob_with_intent(
      /*kind=*/2 /*AttackDefend*/, /*damage=*/8, /*hits=*/1,
      /*applies=*/{{"Frail", 2, 1}, {"Weak", 2, 1}}, /*self_block_gain=*/5,
      /*move_id=*/"POUNCE_MOVE");
  const ParsedStateBlob blob = read_state_blob(bytes);
  ASSERT_EQ(blob.combat_state.enemies.size(), 1U);
  const auto& cr = blob.combat_state.enemies[0];
  ASSERT_TRUE(cr.intent_present);
  ASSERT_EQ(cr.intent.applies_powers.size(), 2U);
  EXPECT_EQ(cr.intent.applies_powers[0].power_id, "Frail");
  EXPECT_EQ(cr.intent.applies_powers[0].target, 1);  // Player
  EXPECT_EQ(cr.intent.applies_powers[1].power_id, "Weak");
  EXPECT_EQ(cr.intent.applies_powers[1].target, 1);  // Player
  EXPECT_EQ(cr.intent.self_block_gain, 5);
  EXPECT_EQ(cr.intent.move_id, "POUNCE_MOVE");
}

}  // namespace
