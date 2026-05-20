#include "sts2/render/render.h"

#include <span>
#include <sstream>
#include <string>

#include "render/render_internal.h"
#include "sts2/game/combat.h"
#include "sts2/game/damage.h"
#include "sts2/game/index_types.h"
#include "sts2/game/monster_moves.h"
#include "sts2/game/power.h"
#include "sts2/game/types.h"
#include "sts2/render/ansi.h"
#include "sts2/render/bar.h"
#include "sts2/render/glyphs.h"

namespace sts2::render::detail {

std::string repeat_utf8(const char* utf8_glyph, int count) {
  std::string s;
  for (int i = 0; i < count; ++i) {
    s += utf8_glyph;
  }
  return s;
}

// Braced-init would invoke std::initializer_list<char> ctor and narrow count.
// NOLINTNEXTLINE(modernize-return-braced-init-list)
std::string spaces(std::size_t n) { return std::string(n, ' '); }

const char* power_name(sts2::game::PowerKind kind) {
  switch (kind) {
    case sts2::game::PowerKind::kWeak:
      return "Weak";
    case sts2::game::PowerKind::kStrength:
      return "Str";
    case sts2::game::PowerKind::kRitual:
      return "Ritual";
    // Wave-17 reserved PowerKinds: no display name yet.
    case sts2::game::PowerKind::kCurlUp:
      return "CurlUp";
    case sts2::game::PowerKind::kFrail:
      return "Frail";
    case sts2::game::PowerKind::kVulnerable:
      return "Vulnerable";
  }
  return "";
}

std::string format_powers(std::span<const sts2::game::Power> ps) {
  if (ps.empty()) {
    return {};
  }
  std::ostringstream os;
  bool first = true;
  for (const auto& p : ps) {
    if (!first) {
      os << ", ";
    }
    first = false;
    os << ansi::kReset << power_name(p.kind) << ' ' << p.amount << ansi::kReset;
  }
  return os.str();
}

std::string format_intent(const sts2::game::Enemy& e) {
  namespace mm = sts2::game::monster_moves;
  const auto kind_idx = static_cast<std::size_t>(e.kind);
  if (kind_idx >= mm::kMonsterKindCount) {
    return {};
  }
  const uint8_t move_idx = mm::find_move_index(e.kind, e.current_move);
  if (move_idx == 0xFF) {
    return {};
  }
  const mm::MonsterMove& move =
      mm::kMonsterMoveTables[kind_idx].moves[move_idx];

  std::ostringstream os;
  bool first = true;
  for (uint8_t i = 0; i < move.effect_count; ++i) {
    const mm::MoveEffect& eff = move.effects[i];
    std::ostringstream tok;
    switch (eff.kind) {
      case sts2::game::MoveEffectKind::kAttack:
        tok << ansi::kRed << glyphs::kSwords
            << sts2::damage::compute_outgoing(e.vitals.powers, eff.value)
            << ansi::kReset;
        break;
      case sts2::game::MoveEffectKind::kDefend:
      case sts2::game::MoveEffectKind::kBlockSelf:
        tok << ansi::kBlue << glyphs::kShield << "DEF" << ansi::kReset;
        break;
      case sts2::game::MoveEffectKind::kBuffSelf:
      case sts2::game::MoveEffectKind::kBuffEnemy:
        tok << ansi::kMagenta << glyphs::kArrowUp << "Buff" << ansi::kReset;
        break;
      case sts2::game::MoveEffectKind::kDebuffPlayer:
        tok << ansi::kYellow << glyphs::kArrowDown << "Debuff" << ansi::kReset;
        break;
      case sts2::game::MoveEffectKind::kAddStatusCard:
        tok << ansi::kYellow << glyphs::kArrowDown << "Cards" << ansi::kReset;
        break;
      case sts2::game::MoveEffectKind::kNone:
        continue;
    }
    if (!first) {
      os << ' ';
    }
    first = false;
    os << tok.str();
  }
  return os.str();
}

std::size_t max_enemy_name_len(const std::vector<sts2::game::Enemy>& es) {
  std::size_t m = 0;
  for (const auto& e : es) {
    if (e.vitals.hp > sts2::game::Stat{0} && e.name.size() > m) {
      m = e.name.size();
    }
  }
  return m;
}

int display_index_of(const sts2::game::Combat& combat,
                     sts2::game::EnemySlot slot) {
  if (!sts2::game::is_alive(combat.enemies(), slot)) {
    return -1;
  }
  int display = 0;
  for (int i = 0; i < slot.raw(); ++i) {
    const sts2::game::EnemySlot s{i};
    if (s.in_range(combat.enemies()) &&
        sts2::game::is_alive(s.at(combat.enemies()))) {
      ++display;
    }
  }
  return display;
}

}  // namespace sts2::render::detail

namespace sts2::render {

void render_combat(const sts2::game::Combat& c, std::ostream& out) {
  out << ansi::kDim
      << detail::repeat_utf8(glyphs::kSeparator, detail::kSeparatorLen)
      << ansi::kReset << "\n";

  out << "  Round " << c.round() << "  " << ansi::kCyan << "Energy "
      << c.player().energy << "/" << sts2::game::Combat::kPlayerMaxEnergy
      << ansi::kReset << "  Draw " << c.player().deck.draw_size()
      << "  Discard " << c.player().deck.discard_size() << "\n";

  out << "  " << ansi::kBold << "The Silent" << ansi::kReset << "  HP "
      << ansi::kRed
      << render::hp_bar(c.player().vitals.hp.value(),
                        c.player().vitals.max_hp.value(),
                        detail::kPlayerHpBarWidth)
      << ansi::kReset << " " << c.player().vitals.hp << "/"
      << c.player().vitals.max_hp;
  if (c.player().vitals.block > sts2::game::Stat{0}) {
    out << "  " << ansi::kBlue << c.player().vitals.block << ansi::kReset
        << " blk";
  }
  out << "  Deck "
      << static_cast<int>(c.player().deck.total_size() +
                          c.player().hand.size());
  const auto player_powers = c.player().vitals.powers;
  if (!player_powers.empty()) {
    out << "  " << detail::format_powers(player_powers);
  }
  out << "\n";

  out << "    " << ansi::kYellow << glyphs::kRelicDiamond
      << " Ring of the Snake" << ansi::kReset << ansi::kDim
      << ": At the start of each combat, draw 2 additional cards."
      << ansi::kReset << "\n\n";

  std::size_t name_width = detail::max_enemy_name_len(c.enemies());
  std::size_t display_idx = 0;
  for (sts2::game::EnemySlot slot : c.alive_enemy_indices()) {
    const sts2::game::Enemy& e = slot.at(c.enemies());
    out << "  [" << display_idx++ << "] " << ansi::kBold << e.name
        << ansi::kReset << detail::spaces(name_width - e.name.size())
        << "   HP " << ansi::kRed
        << render::hp_bar(e.vitals.hp.value(), e.vitals.max_hp.value(),
                          detail::kEnemyHpBarWidth)
        << ansi::kReset << " " << e.vitals.hp << "/" << e.vitals.max_hp;
    if (e.vitals.block > sts2::game::Stat{0}) {
      out << "  " << ansi::kBlue << e.vitals.block << ansi::kReset << " blk";
    }
    out << "   " << detail::format_intent(e);
    if (!e.vitals.powers.empty()) {
      out << "  " << detail::format_powers(e.vitals.powers);
    }
    out << "\n";
  }
  out << "\n";

  for (std::size_t i = 0; i < c.player().hand.size(); ++i) {
    const sts2::game::Card& card =
        c.player().hand.at(sts2::game::HandIndex{static_cast<int>(i)});
    bool playable = card.cost <= c.player().energy;
    const char* bullet_color = playable ? ansi::kGreen : ansi::kDim;
    const char* bullet =
        playable ? glyphs::kBulletFilled : glyphs::kBulletHollow;
    const char* type_color =
        (card.type == sts2::game::CardType::kAttack) ? ansi::kRed : ansi::kBlue;
    out << "  " << bullet_color << bullet << ansi::kReset << " [" << i << "] "
        << type_color << card.name << ansi::kReset << " (" << ansi::kCyan
        << card.cost << ansi::kReset << ") " << card.short_stats;
    if (card.target == sts2::game::TargetType::kAnyEnemy) {
      out << "  " << ansi::kYellow << glyphs::kArrowRight << ansi::kReset;
    }
    out << "\n";
    for (const std::string& line : card.description) {
      out << "      " << ansi::kDim << line << ansi::kReset << "\n";
    }
  }
  out << "\n";
}

}  // namespace sts2::render
