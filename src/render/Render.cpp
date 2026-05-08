#include "render/Render.h"

#include <iomanip>
#include <sstream>
#include <string>

#include "game/Cards.h"
#include "game/Combat.h"
#include "game/Power.h"
#include "game/Powers.h"
#include "game/Types.h"
#include "render/Ansi.h"
#include "render/Bar.h"

namespace {

constexpr int kHpBarWidth = 16;
constexpr int kSeparatorLen = 56;

std::string repeat_utf8(const char* utf8_glyph, int count) {
    std::string s;
    for (int i = 0; i < count; ++i) s += utf8_glyph;
    return s;
}

const char* power_color(PowerKind kind) {
    switch (kind) {
        case PowerKind::Weak:     return ansi::kRed;
        case PowerKind::Strength: return ansi::kGreen;
        case PowerKind::Ritual:   return ansi::kGreen;
    }
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
    os << ansi::kDim << "(";
    bool first = true;
    for (const auto& p : ps) {
        if (!first) os << ", ";
        first = false;
        os << power_color(p.kind) << power_name(p.kind) << ' ' << p.amount << ansi::kDim;
    }
    os << ")" << ansi::kReset;
    return os.str();
}

int dark_strike_preview(const Enemy& e) {
    int dmg = e.dark_strike_base + powers::amount(e.powers, PowerKind::Strength);
    if (powers::amount(e.powers, PowerKind::Weak) > 0) dmg = static_cast<int>(dmg * 0.75);
    return dmg < 0 ? 0 : dmg;
}

std::string format_intent(const Enemy& e) {
    if (e.hp <= 0) {
        std::ostringstream os; os << ansi::kDim << "(slain)" << ansi::kReset; return os.str();
    }
    std::ostringstream os;
    switch (e.current_move) {
        case MoveId::Incantation:
            os << ansi::kMagenta << "BUFF" << ansi::kReset;
            break;
        case MoveId::DarkStrike:
            os << ansi::kRed << "ATK " << dark_strike_preview(e) << ansi::kReset;
            break;
    }
    return os.str();
}

}

namespace render {

std::string card_inline_stats(int card_id) {
    switch (card_id) {
        case cards::IdStrike:     return "6dmg";
        case cards::IdDefend:     return "5blk";
        case cards::IdNeutralize: return "3dmg + Weak 1";
        case cards::IdSurvivor:   return "8blk, discard 1";
    }
    return "";
}

void render_combat(const Combat& c, std::ostream& out) {
    const char* sep = "\xe2\x95\x90";

    out << ansi::kDim << repeat_utf8(sep, kSeparatorLen) << ansi::kReset << "\n";

    out << "  Round " << c.round
        << "   " << ansi::kCyan << "Energy " << c.player.energy << "/" << c.player.max_energy << ansi::kReset
        << "   Draw " << c.player.draw_pile.size()
        << "   Discard " << c.player.discard_pile.size()
        << "   Exhaust " << c.player.exhaust_pile.size()
        << "\n\n";

    out << "  " << ansi::kBold << "Silent" << ansi::kReset
        << "   HP " << ansi::kRed << hp_bar(c.player.hp, c.player.max_hp, kHpBarWidth) << ansi::kReset
        << " " << c.player.hp << "/" << c.player.max_hp
        << "   " << ansi::kBlue << c.player.block << " blk" << ansi::kReset;
    if (!c.player.powers.empty()) {
        out << "   " << format_powers(c.player.powers);
    }
    out << "\n\n";

    for (size_t i = 0; i < c.enemies.size(); ++i) {
        const Enemy& e = c.enemies[i];
        out << "  [" << i << "] " << ansi::kBold << e.name << ansi::kReset
            << "   HP " << ansi::kRed << hp_bar(e.hp, e.max_hp, kHpBarWidth) << ansi::kReset
            << " " << e.hp << "/" << e.max_hp
            << "   " << ansi::kBlue << e.block << " blk" << ansi::kReset
            << "   " << format_intent(e);
        if (!e.powers.empty() && e.hp > 0) {
            out << "   " << format_powers(e.powers);
        }
        out << "\n";
    }
    out << "\n";

    for (size_t i = 0; i < c.player.hand.size(); ++i) {
        const Card& card = c.player.hand[i];
        bool playable = card.cost <= c.player.energy;
        const char* bullet_color = playable ? ansi::kGreen : ansi::kDim;
        const char* bullet = playable ? "\xe2\x97\x8f" : "\xe2\x97\x8b";
        const char* type_color = (card.type == CardType::Attack) ? ansi::kRed : ansi::kBlue;
        out << "  " << bullet_color << bullet << ansi::kReset
            << " [" << i << "] "
            << type_color << std::left << std::setw(12) << card.name << ansi::kReset
            << " (" << ansi::kCyan << card.cost << ansi::kReset << ") "
            << card_inline_stats(card.id)
            << "\n";
    }
    out << "\n";
}

}
