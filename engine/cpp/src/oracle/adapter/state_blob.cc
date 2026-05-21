#include "sts2/oracle/adapter/state_blob.h"

#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <vector>

#include "binary_cursor_internal.h"
#include "sha256_internal.h"

// M1 binary state-blob reader. Wire layout pinned by
// engine/headless/docs/specs/modules/state-codec.md §Section-table layout
// (schema v4, ADR-032). The C# reference is
// src/Sts2Headless.Adapters/StateCodec/StateCodec.cs; we mirror Read* helpers
// here. Failures throw StateCodecError loudly — never a partially-decoded
// ParsedStateBlob.

namespace sts2::oracle::adapter {

namespace {

using detail::BinaryCursor;

constexpr std::uint16_t kSectionIdRng = 0U;
constexpr std::uint16_t kSectionIdTokens = 1U;
constexpr std::uint16_t kSectionIdCombatState = 2U;

ParsedPowerInstance read_power_instance(BinaryCursor& c) {
  ParsedPowerInstance p;
  p.model_id =
      c.read_lp_string<StateCodecError, std::uint32_t>("PowerInstance.ModelId");
  p.stacks = c.read_i32<StateCodecError>("PowerInstance.Stacks");
  p.source_creature_id =
      c.read_u32<StateCodecError>("PowerInstance.SourceCreatureId");
  // bool is 1-byte on the wire.
  p.just_applied =
      c.read_u8<StateCodecError>("PowerInstance.JustApplied") != 0U;
  return p;
}

ParsedMonsterIntent read_monster_intent(BinaryCursor& c) {
  ParsedMonsterIntent intent;
  intent.kind = c.read_i32<StateCodecError>("MonsterIntent.Kind");
  intent.damage_per_hit =
      c.read_i32<StateCodecError>("MonsterIntent.DamagePerHit");
  intent.hit_count = c.read_i32<StateCodecError>("MonsterIntent.HitCount");
  const std::int32_t applies_count =
      c.read_i32<StateCodecError>("MonsterIntent.AppliesCount");
  if (applies_count < 0) {
    throw StateCodecError("MonsterIntent.AppliesCount negative");
  }
  intent.applies_powers.reserve(static_cast<std::size_t>(applies_count));
  for (std::int32_t i = 0; i < applies_count; ++i) {
    ParsedAppliesPower ap;
    ap.power_id = c.read_lp_string<StateCodecError, std::uint32_t>(
        "MonsterIntent.Applies.PowerId");
    ap.stacks = c.read_i32<StateCodecError>("MonsterIntent.Applies.Stacks");
    ap.target = c.read_i32<StateCodecError>("MonsterIntent.Applies.Target");
    // PowerTarget enum (ADR-032): Self=0, Player=1.
    if (ap.target != 0 && ap.target != 1) {
      throw StateCodecError(
          "MonsterIntent.Applies.Target out of PowerTarget enum range");
    }
    intent.applies_powers.push_back(std::move(ap));
  }
  intent.self_block_gain =
      c.read_i32<StateCodecError>("MonsterIntent.SelfBlockGain");
  // MoveId appended at v2 (Stream-B-T3). Required for schema v3+.
  intent.move_id =
      c.read_lp_string<StateCodecError, std::uint32_t>("MonsterIntent.MoveId");
  return intent;
}

ParsedCreature read_creature(BinaryCursor& c) {
  ParsedCreature cr;
  cr.id = c.read_u32<StateCodecError>("Creature.Id");
  cr.name = c.read_lp_string<StateCodecError, std::uint32_t>("Creature.Name");
  cr.current_hp = c.read_i32<StateCodecError>("Creature.CurrentHp");
  cr.max_hp = c.read_i32<StateCodecError>("Creature.MaxHp");
  cr.block = c.read_i32<StateCodecError>("Creature.Block");
  const std::int32_t power_count =
      c.read_i32<StateCodecError>("Creature.PowerCount");
  if (power_count < 0) {
    throw StateCodecError("Creature.PowerCount negative");
  }
  cr.powers.reserve(static_cast<std::size_t>(power_count));
  for (std::int32_t i = 0; i < power_count; ++i) {
    cr.powers.push_back(read_power_instance(c));
  }
  const std::uint8_t intent_present_byte =
      c.read_u8<StateCodecError>("Creature.IntentPresent");
  cr.intent_present = intent_present_byte != 0U;
  if (cr.intent_present) {
    cr.intent = read_monster_intent(c);
  }
  cr.is_player = c.read_u8<StateCodecError>("Creature.IsPlayer") != 0U;
  return cr;
}

ParsedCardInstance read_card_instance(BinaryCursor& c) {
  ParsedCardInstance card;
  card.instance_id = c.read_u32<StateCodecError>("CardInstance.InstanceId");
  card.model_id =
      c.read_lp_string<StateCodecError, std::uint32_t>("CardInstance.ModelId");
  card.upgrade_level = c.read_i32<StateCodecError>("CardInstance.UpgradeLevel");
  card.cost_override_present =
      c.read_u8<StateCodecError>("CardInstance.CostOverridePresent") != 0U;
  if (card.cost_override_present) {
    card.cost_override =
        c.read_i32<StateCodecError>("CardInstance.CostOverride");
  }
  return card;
}

std::vector<ParsedCardInstance> read_card_pile(BinaryCursor& c,
                                               const char* what) {
  const std::int32_t count = c.read_i32<StateCodecError>(what);
  if (count < 0) {
    throw StateCodecError(std::string(what) + ": negative count");
  }
  std::vector<ParsedCardInstance> pile;
  pile.reserve(static_cast<std::size_t>(count));
  for (std::int32_t i = 0; i < count; ++i) {
    pile.push_back(read_card_instance(c));
  }
  return pile;
}

ParsedCombatState read_combat_state(std::span<const std::uint8_t> bytes) {
  BinaryCursor c(bytes);
  ParsedCombatState s;
  s.turn_counter = c.read_i32<StateCodecError>("CombatState.TurnCounter");
  s.phase = c.read_i32<StateCodecError>("CombatState.Phase");
  s.player = read_creature(c);
  s.enemy_count = c.read_i32<StateCodecError>("CombatState.EnemyCount");
  if (s.enemy_count < 0) {
    throw StateCodecError("CombatState.EnemyCount negative");
  }
  s.enemies.reserve(static_cast<std::size_t>(s.enemy_count));
  for (std::int32_t i = 0; i < s.enemy_count; ++i) {
    s.enemies.push_back(read_creature(c));
  }
  s.energy = c.read_i32<StateCodecError>("CombatState.Energy");
  s.base_energy_per_turn =
      c.read_i32<StateCodecError>("CombatState.BaseEnergyPerTurn");
  s.hand_draw_size = c.read_i32<StateCodecError>("CombatState.HandDrawSize");
  s.draw_pile = read_card_pile(c, "CombatState.DrawPile.Count");
  s.hand_pile = read_card_pile(c, "CombatState.HandPile.Count");
  s.discard_pile = read_card_pile(c, "CombatState.DiscardPile.Count");
  s.exhaust_pile = read_card_pile(c, "CombatState.ExhaustPile.Count");
  s.player_rng_counter =
      c.read_i32<StateCodecError>("CombatState.PlayerRngCounter");
  s.monster_rng_counter =
      c.read_i32<StateCodecError>("CombatState.MonsterRngCounter");
  s.attacks_played_this_turn =
      c.read_i32<StateCodecError>("CombatState.AttacksPlayedThisTurn");
  s.cards_drawn_this_combat =
      c.read_i32<StateCodecError>("CombatState.CardsDrawnThisCombat");
  s.last_spent_energy =
      c.read_i32<StateCodecError>("CombatState.LastSpentEnergy");
  s.exhausted_shiv_count =
      c.read_i32<StateCodecError>("CombatState.ExhaustedShivCount");
  if (!c.exhausted()) {
    // Trailing bytes inside a CombatState section indicate a producer-side
    // schema bump we don't recognize. Reject loudly per state-codec.md
    // Invariant #3.
    throw StateCodecError(
        "CombatState section has trailing bytes — schema mismatch");
  }
  return s;
}

ManifestStamp read_manifest_stamp(BinaryCursor& c, std::size_t header_size) {
  const std::size_t start = c.pos();
  ManifestStamp stamp;
  stamp.git_sha =
      c.read_lp_string<StateCodecError, std::uint8_t>("ManifestStamp.GitSha");
  stamp.build_id =
      c.read_lp_string<StateCodecError, std::uint16_t>("ManifestStamp.BuildId");
  c.read_bytes_into<StateCodecError>(stamp.content_hash.data(), 32,
                                     "ManifestStamp.ContentHash");
  const std::size_t consumed = c.pos() - start;
  if (consumed != header_size) {
    throw StateCodecError("ManifestStamp size mismatch with header_size");
  }
  return stamp;
}

}  // namespace

ParsedStateBlob read_state_blob(std::span<const std::uint8_t> bytes) {
  if (bytes.size() < kStateCodecTrailerSizeBytes) {
    throw StateCodecError("blob too small for trailer");
  }
  BinaryCursor c(bytes);
  ParsedStateBlob blob;

  // Header.
  blob.magic = c.read_u32<StateCodecError>("header.magic");
  if (blob.magic != kStateCodecMagic) {
    throw StateCodecError("header.magic mismatch (expected 0x53435443)");
  }
  blob.schema = c.read_u16<StateCodecError>("header.schema");
  if (blob.schema != kStateCodecSchemaV4) {
    throw StateCodecError(
        "header.schema unsupported (Q2 Phase-1A wants v4 minor)");
  }
  const std::uint16_t header_size =
      c.read_u16<StateCodecError>("header.header_size");
  blob.stamp = read_manifest_stamp(c, header_size);

  // Sections. Canonical order: Rng -> Tokens -> CombatState -> 0xFFFF.
  bool seen_rng = false;
  bool seen_tokens = false;
  bool seen_combat = false;
  while (true) {
    const std::uint16_t section_id = c.read_u16<StateCodecError>("section.id");
    if (section_id == kSectionTerminator) {
      break;
    }
    const std::uint32_t section_size =
        c.read_u32<StateCodecError>("section.size");
    auto body = c.read_span<StateCodecError>(section_size, "section.body");

    switch (section_id) {
      case kSectionIdRng:
        if (seen_rng) {
          throw StateCodecError("duplicate Rng section");
        }
        blob.rng_section_bytes.assign(body.begin(), body.end());
        seen_rng = true;
        break;
      case kSectionIdTokens:
        if (seen_tokens) {
          throw StateCodecError("duplicate Tokens section");
        }
        blob.tokens_section_bytes.assign(body.begin(), body.end());
        seen_tokens = true;
        break;
      case kSectionIdCombatState:
        if (seen_combat) {
          throw StateCodecError("duplicate CombatState section");
        }
        blob.combat_state = read_combat_state(body);
        seen_combat = true;
        break;
      default:
        // Unknown section ids are "unsupported" per state-codec.md
        // §Section ids -- reject loudly.
        throw StateCodecError("unknown section id");
    }
  }
  if (!seen_rng || !seen_tokens || !seen_combat) {
    throw StateCodecError(
        "missing required section (Rng / Tokens / CombatState)");
  }

  // Trailer (36 bytes: magic + sha256). Read & validate.
  const std::size_t trailer_start = c.pos();
  if (bytes.size() - trailer_start != kStateCodecTrailerSizeBytes) {
    throw StateCodecError("trailer size mismatch (expected 36 bytes)");
  }
  const std::uint32_t trailer_magic =
      c.read_u32<StateCodecError>("trailer.magic");
  if (trailer_magic != kStateCodecTrailerMagic) {
    throw StateCodecError("trailer.magic mismatch (expected 0x53544354)");
  }
  c.read_bytes_into<StateCodecError>(blob.trailer_sha256.data(), 32,
                                     "trailer.sha256");

  // Recompute sha256 over [start_of_blob, start_of_trailer_magic).
  const auto computed = detail::sha256(bytes.subspan(0, trailer_start));
  if (computed != blob.trailer_sha256) {
    throw StateCodecError("trailer sha256 mismatch");
  }

  if (!c.exhausted()) {
    throw StateCodecError("unexpected trailing bytes after trailer");
  }
  return blob;
}

}  // namespace sts2::oracle::adapter
