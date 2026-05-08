#include "render/Render.h"

#include <sstream>
#include <string>

#include "game/Combat.h"
#include "game/Damage.h"
#include "game/Power.h"
#include "game/Powers.h"
#include "game/Types.h"
#include "render/Ansi.h"
#include "render/Bar.h"
#include "render/Glyphs.h"

namespace {

constexpr int kPlayerHpBarWidth = 20;
constexpr int kEnemyHpBarWidth = 16;
constexpr int kSeparatorLen = 60;

std::string repeat_utf8(const char* utf8_glyph, int count) {
    std::string s;
    for (int i = 0; i < count; ++i) s += utf8_glyph;
    return s;
}

std::string spaces(size_t n) {
    return std::string(n, ' ');
}

const char* power_color(PowerKind) {
    return ansi::kReset;
}

const char* power_name(PowerKind kind) {
    switch (kind) {
        case PowerKind::Weak:     return "Weak";
        case PowerKind::Strength: return "Str";
        case PowerKind::Ritual:   return "Ritual";
    }
    return "";
}

std::string format_powers(const std::vector<Power>& ps) {
    if (ps.empty()) return {};
    std::ostringstream os;
    bool first = true;
    for (const auto& p : ps) {
        if (!first) os << ", ";
        first = false;
        os << power_color(p.kind) << power_name(p.kind) << ' ' << p.amount << ansi::kReset;
    }
    return os.str();
}

std::string format_intent(const Enemy& e) {
    std::ostringstream os;
    switch (e.current_move) {
        case MoveId::Incantation:
            os << ansi::kMagenta << glyphs::kArrowUp << "Buff" << ansi::kReset;
            break;
        case MoveId::DarkStrike:
            os << ansi::kRed << glyphs::kSwords << ' '
               << damage::compute_outgoing(e.powers, e.dark_strike_base) << ansi::kReset;
            break;
    }
    return os.str();
}

size_t max_enemy_name_len(const std::vector<Enemy>& es) {
    size_t m = 0;
    for (const auto& e : es) {
        if (e.hp > 0 && e.name.size() > m) m = e.name.size();
    }
    return m;
}

int total_deck_size(const Player& p) {
    return static_cast<int>(p.draw_pile.size() + p.hand.size()
                          + p.discard_pile.size() + p.exhaust_pile.size());
}

}

namespace render {

void render_combat(const Combat& c, std::ostream& out) {
    out << ansi::kDim << repeat_utf8(glyphs::kSeparator, kSeparatorLen) << ansi::kReset << "\n";

    out << "  Round " << c.round
        << "  " << ansi::kCyan << "Energy " << c.player.energy << "/" << c.player.max_energy << ansi::kReset
        << "  Draw " << c.player.draw_pile.size()
        << "  Discard " << c.player.discard_pile.size()
        << "\n";

    out << "  " << ansi::kBold << "The Silent" << ansi::kReset
        << "  HP " << ansi::kRed << render::hp_bar(c.player.hp, c.player.max_hp, kPlayerHpBarWidth) << ansi::kReset
        << " " << c.player.hp << "/" << c.player.max_hp;
    if (c.player.block > 0) {
        out << "  " << ansi::kBlue << c.player.block << ansi::kReset << " blk";
    }
    out << "  Deck " << total_deck_size(c.player);
    if (!c.player.powers.empty()) {
        out << "  " << format_powers(c.player.powers);
    }
    out << "\n";

    out << "    " << ansi::kYellow << glyphs::kRelicDiamond << " Ring of the Snake" << ansi::kReset
        << ansi::kDim << ": At the start of each combat, draw 2 additional cards." << ansi::kReset
        << "\n\n";

    size_t name_width = max_enemy_name_len(c.enemies);
    size_t display_idx = 0;
    for (size_t i = 0; i < c.enemies.size(); ++i) {
        const Enemy& e = c.enemies[i];
        if (e.hp <= 0) continue;
        out << "  [" << display_idx++ << "] " << ansi::kBold << e.name << ansi::kReset
            << spaces(name_width - e.name.size())
            << "   HP " << ansi::kRed << render::hp_bar(e.hp, e.max_hp, kEnemyHpBarWidth) << ansi::kReset
            << " " << e.hp << "/" << e.max_hp;
        if (e.block > 0) {
            out << "  " << ansi::kBlue << e.block << ansi::kReset << " blk";
        }
        out << "   " << format_intent(e);
        if (!e.powers.empty()) {
            out << "  " << format_powers(e.powers);
        }
        out << "\n";
    }
    out << "\n";

    for (size_t i = 0; i < c.player.hand.size(); ++i) {
        const Card& card = c.player.hand[i];
        bool playable = card.cost <= c.player.energy;
        const char* bullet_color = playable ? ansi::kGreen : ansi::kDim;
        const char* bullet = playable ? glyphs::kBulletFilled : glyphs::kBulletHollow;
        const char* type_color = (card.type == CardType::Attack) ? ansi::kRed : ansi::kBlue;
        out << "  " << bullet_color << bullet << ansi::kReset
            << " [" << i << "] "
            << type_color << card.name << ansi::kReset
            << " (" << ansi::kCyan << card.cost << ansi::kReset << ") "
            << card.short_stats;
        if (card.target == TargetType::AnyEnemy) {
            out << "  " << ansi::kYellow << glyphs::kArrowRight << ansi::kReset;
        }
        out << "\n";
        for (const std::string& line : card.description) {
            out << "      " << ansi::kDim << line << ansi::kReset << "\n";
        }
    }
    out << "\n";
}

}
