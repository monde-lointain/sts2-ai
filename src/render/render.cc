#include "sts2/render/render.h"

#include <sstream>
#include <string>

#include "render/render_internal.h"
#include "sts2/game/combat.h"
#include "sts2/game/damage.h"
#include "sts2/game/power.h"
#include "sts2/game/powers.h"
#include "sts2/game/types.h"
#include "sts2/render/ansi.h"
#include "sts2/render/bar.h"
#include "sts2/render/glyphs.h"

namespace sts2::render::detail {

std::string repeat_utf8(const char* utf8_glyph, int count) {
  std::string s;
  for (int i = 0; i < count; ++i) s += utf8_glyph;
  return s;
}

std::string spaces(std::size_t n) { return std::string(n, ' '); }

const char* power_color(sts2::game::PowerKind) { return ansi::kReset; }

const char* power_name(sts2::game::PowerKind kind) {
  switch (kind) {
    case sts2::game::PowerKind::Weak:
      return "Weak";
    case sts2::game::PowerKind::Strength:
      return "Str";
    case sts2::game::PowerKind::Ritual:
      return "Ritual";
  }
  return "";
}

std::string format_powers(const std::vector<sts2::game::Power>& ps) {
  if (ps.empty()) return {};
  std::ostringstream os;
  bool first = true;
  for (const auto& p : ps) {
    if (!first) os << ", ";
    first = false;
    os << power_color(p.kind) << power_name(p.kind) << ' ' << p.amount
       << ansi::kReset;
  }
  return os.str();
}

std::string format_intent(const sts2::game::Enemy& e) {
  std::ostringstream os;
  switch (e.current_move) {
    case sts2::game::MoveId::Incantation:
      os << ansi::kMagenta << glyphs::kArrowUp << "Buff" << ansi::kReset;
      break;
    case sts2::game::MoveId::DarkStrike:
      os << ansi::kRed << glyphs::kSwords << ' '
         << sts2::damage::compute_outgoing(e.vitals.powers, e.dark_strike_base)
         << ansi::kReset;
      break;
  }
  return os.str();
}

std::size_t max_enemy_name_len(const std::vector<sts2::game::Enemy>& es) {
  std::size_t m = 0;
  for (const auto& e : es) {
    if (e.vitals.hp > 0 && e.name.size() > m) m = e.name.size();
  }
  return m;
}

int total_deck_size(const sts2::game::Player& p) {
  return static_cast<int>(p.draw_pile.size() + p.hand.size() +
                          p.discard_pile.size() + p.exhaust_pile.size());
}

}  // namespace sts2::render::detail

namespace sts2::render {

void render_combat(const sts2::game::Combat& c, std::ostream& out) {
  out << ansi::kDim
      << detail::repeat_utf8(glyphs::kSeparator, detail::kSeparatorLen)
      << ansi::kReset << "\n";

  out << "  Round " << c.round() << "  " << ansi::kCyan << "Energy "
      << c.player().energy << "/" << c.player().max_energy << ansi::kReset
      << "  Draw " << c.player().draw_pile.size() << "  Discard "
      << c.player().discard_pile.size() << "\n";

  out << "  " << ansi::kBold << "The Silent" << ansi::kReset << "  HP "
      << ansi::kRed
      << render::hp_bar(c.player().vitals.hp, c.player().vitals.max_hp,
                        detail::kPlayerHpBarWidth)
      << ansi::kReset << " " << c.player().vitals.hp << "/"
      << c.player().vitals.max_hp;
  if (c.player().vitals.block > 0) {
    out << "  " << ansi::kBlue << c.player().vitals.block << ansi::kReset
        << " blk";
  }
  out << "  Deck " << detail::total_deck_size(c.player());
  if (!c.player().vitals.powers.empty()) {
    out << "  " << detail::format_powers(c.player().vitals.powers);
  }
  out << "\n";

  out << "    " << ansi::kYellow << glyphs::kRelicDiamond
      << " Ring of the Snake" << ansi::kReset << ansi::kDim
      << ": At the start of each combat, draw 2 additional cards."
      << ansi::kReset << "\n\n";

  std::size_t name_width = detail::max_enemy_name_len(c.enemies());
  std::size_t display_idx = 0;
  for (std::size_t i = 0; i < c.enemies().size(); ++i) {
    const sts2::game::Enemy& e = c.enemies()[i];
    if (e.vitals.hp <= 0) continue;
    out << "  [" << display_idx++ << "] " << ansi::kBold << e.name
        << ansi::kReset << detail::spaces(name_width - e.name.size())
        << "   HP " << ansi::kRed
        << render::hp_bar(e.vitals.hp, e.vitals.max_hp,
                          detail::kEnemyHpBarWidth)
        << ansi::kReset << " " << e.vitals.hp << "/" << e.vitals.max_hp;
    if (e.vitals.block > 0) {
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
    const sts2::game::Card& card = c.player().hand[i];
    bool playable = card.cost <= c.player().energy;
    const char* bullet_color = playable ? ansi::kGreen : ansi::kDim;
    const char* bullet =
        playable ? glyphs::kBulletFilled : glyphs::kBulletHollow;
    const char* type_color =
        (card.type == sts2::game::CardType::Attack) ? ansi::kRed : ansi::kBlue;
    out << "  " << bullet_color << bullet << ansi::kReset << " [" << i << "] "
        << type_color << card.name << ansi::kReset << " (" << ansi::kCyan
        << card.cost << ansi::kReset << ") " << card.short_stats;
    if (card.target == sts2::game::TargetType::AnyEnemy) {
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
