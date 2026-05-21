#pragma once

#include <array>
#include <cstdint>
#include <span>
#include <stdexcept>
#include <string>
#include <vector>

// M1 binary state-blob reader (per state-codec.md, schema v4).
//
// Q2 consumes the `payload` bytes of a StateBlobEnvelope (or, for D3 fixture
// .blob files, the raw blob bytes themselves) via `read_state_blob`. The
// resulting ParsedStateBlob carries the manifest stamp + parsed CombatState
// section; Rng and Tokens are passed through as opaque byte runs since Q2's
// CompactState projection doesn't need them.
//
// All multi-byte integers are little-endian; strings are length-prefixed
// UTF-8 with field-specific prefix widths (u8 / u16 / u32). The reader
// validates magic, schema, the 0xFFFF section terminator, the trailer magic,
// and recomputes SHA-256 over [start_of_blob, start_of_trailer_magic) for
// tamper detection. Any failure throws StateCodecError loudly — never a
// partially-decoded ParsedStateBlob.

namespace sts2::oracle::adapter {

// Hard-coded wire constants pinned by state-codec.md.
inline constexpr std::uint32_t kStateCodecMagic = 0x53435443U;         // "STCT"
inline constexpr std::uint32_t kStateCodecTrailerMagic = 0x53544354U;  // "TCTS"
inline constexpr std::uint16_t kStateCodecSchemaV4 = 4U;  // (0<<8)|4
inline constexpr std::uint16_t kSectionTerminator = 0xFFFFU;
inline constexpr std::size_t kStateCodecTrailerSizeBytes = 36U;

class StateCodecError : public std::runtime_error {
 public:
  using std::runtime_error::runtime_error;
};

struct ManifestStamp {
  std::string git_sha;                          // u8-length-prefixed UTF-8
  std::string build_id;                         // u16-length-prefixed UTF-8
  std::array<std::uint8_t, 32> content_hash{};  // raw 32 bytes (SHA-256)

  bool operator==(const ManifestStamp&) const = default;
};

// Per state-codec.md §Per-section codecs `PowerInstance`.
struct ParsedPowerInstance {
  std::string model_id;  // lp-utf8 (u32 prefix)
  std::int32_t stacks = 0;
  std::uint32_t source_creature_id = 0;
  bool just_applied = false;
};

// Per state-codec.md §Per-section codecs `MonsterIntent.AppliesPowers[]`.
// PowerTarget enum (ADR-032): Self=0, Player=1.
struct ParsedAppliesPower {
  std::string power_id;  // lp-utf8 (u32 prefix)
  std::int32_t stacks = 0;
  std::int32_t target = 0;  // PowerTarget enum; NEW at v4
};

// Per state-codec.md §Per-section codecs `MonsterIntent`.
// MoveId was appended at v2 (Stream-B-T3).
// AppliesPowers[].Target + SelfBlockGain added at v4 (ADR-032).
struct ParsedMonsterIntent {
  std::int32_t kind = 0;
  std::int32_t damage_per_hit = 0;
  std::int32_t hit_count = 0;
  std::vector<ParsedAppliesPower> applies_powers;
  std::int32_t self_block_gain = 0;  // NEW at v4
  std::string move_id;               // lp-utf8 (u32 prefix)
};

// Per state-codec.md §Per-section codecs `Creature`.
struct ParsedCreature {
  std::uint32_t id = 0;
  std::string name;  // lp-utf8 (u32 prefix)
  std::int32_t current_hp = 0;
  std::int32_t max_hp = 0;
  std::int32_t block = 0;
  std::vector<ParsedPowerInstance> powers;
  bool intent_present = false;
  ParsedMonsterIntent intent;
  bool is_player = false;
};

// Per state-codec.md §Per-section codecs `CardInstance`.
struct ParsedCardInstance {
  std::uint32_t instance_id = 0;
  std::string model_id;  // lp-utf8 (u32 prefix)
  std::int32_t upgrade_level = 0;
  bool cost_override_present = false;
  std::int32_t cost_override = 0;
};

// Per state-codec.md §Per-section codecs `CombatState`. Field order pinned
// at schema v3; MonsterIntent widened at v4 (ADR-032). Future minor bumps
// append fields tail-side.
struct ParsedCombatState {
  std::int32_t turn_counter = 0;
  std::int32_t phase = 0;
  ParsedCreature player;
  std::int32_t enemy_count = 0;
  std::vector<ParsedCreature> enemies;
  std::int32_t energy = 0;
  std::int32_t base_energy_per_turn = 0;
  std::int32_t hand_draw_size = 0;
  std::vector<ParsedCardInstance> draw_pile;
  std::vector<ParsedCardInstance> hand_pile;
  std::vector<ParsedCardInstance> discard_pile;
  std::vector<ParsedCardInstance> exhaust_pile;
  std::int32_t player_rng_counter = 0;
  std::int32_t monster_rng_counter = 0;
  std::int32_t attacks_played_this_turn = 0;  // v2
  std::int32_t cards_drawn_this_combat = 0;   // v2
  std::int32_t last_spent_energy = 0;         // v3
  std::int32_t exhausted_shiv_count = 0;      // v3
};

struct ParsedStateBlob {
  std::uint32_t magic = 0;
  std::uint16_t schema = 0;
  ManifestStamp stamp;
  std::vector<std::uint8_t> rng_section_bytes;
  std::vector<std::uint8_t> tokens_section_bytes;
  ParsedCombatState combat_state;
  std::array<std::uint8_t, 32> trailer_sha256{};
};

[[nodiscard]] ParsedStateBlob read_state_blob(
    std::span<const std::uint8_t> bytes);

}  // namespace sts2::oracle::adapter
