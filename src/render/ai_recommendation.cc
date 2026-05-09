#include "sts2/render/ai_recommendation.h"

#include <cstddef>
#include <ios>
#include <iomanip>
#include <ostream>
#include <vector>

#include "sts2/ai/recommend.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemy.h"
#include "sts2/game/types.h"
#include "sts2/input/input.h"
#include "sts2/render/ansi.h"

namespace sts2::render {

namespace {

const char* card_id_name(sts2::game::CardId id) {
  switch (id) {
    case sts2::game::CardId::kStrike:
      return "Strike";
    case sts2::game::CardId::kDefend:
      return "Defend";
    case sts2::game::CardId::kNeutralize:
      return "Neutralize";
    case sts2::game::CardId::kSurvivor:
      return "Survivor";
    case sts2::game::CardId::kNone:
      return "(none)";
  }
  return "?";
}

bool target_is_live_enemy(const sts2::game::Combat& combat, int idx) {
  if (idx < 0) return false;
  const auto& es = combat.enemies();
  if (static_cast<std::size_t>(idx) >= es.size()) return false;
  return es[static_cast<std::size_t>(idx)].vitals.hp > 0;
}

// Convert an engine slot index into the display index used by the battle UI,
// which renumbers alive enemies starting from 0. Returns -1 if the slot is
// dead, out of range, or negative.
int display_index_for_slot(const sts2::game::Combat& combat, int slot_idx) {
  if (slot_idx < 0) return -1;
  const auto& es = combat.enemies();
  if (static_cast<std::size_t>(slot_idx) >= es.size()) return -1;
  if (es[static_cast<std::size_t>(slot_idx)].vitals.hp <= 0) return -1;
  int display = 0;
  for (int i = 0; i < slot_idx; ++i) {
    if (es[static_cast<std::size_t>(i)].vitals.hp > 0) ++display;
  }
  return display;
}

void write_pv_step(std::ostream& out, const sts2::ai::PvStep& step,
                   const sts2::game::Combat& combat) {
  if (step.kind == sts2::ai::PvStep::kEndTurn) {
    out << "EndTurn";
    return;
  }
  out << card_id_name(step.card_id);
  if (step.target_idx >= 0) {
    const auto& es = combat.enemies();
    if (static_cast<std::size_t>(step.target_idx) < es.size()) {
      const int disp = display_index_for_slot(combat, step.target_idx);
      if (disp >= 0) {
        out << " -> [" << disp << "] "
            << es[static_cast<std::size_t>(step.target_idx)].name;
      } else {
        // Slot is dead in current state; fall back to slot index for diagnostics.
        out << " -> [" << step.target_idx << "] "
            << es[static_cast<std::size_t>(step.target_idx)].name;
      }
    } else {
      out << " -> " << step.target_idx;
    }
  }
  if (step.survivor_discard_id != sts2::game::CardId::kNone) {
    out << " (drop " << card_id_name(step.survivor_discard_id) << ")";
  }
}

}  // namespace

void render_ai_recommendation(const sts2::ai::Recommendation& rec,
                              const sts2::game::Combat& combat,
                              std::ostream& out) {
  if (rec.combat_over) {
    out << ansi::kCyan << "> AI:" << ansi::kReset << " combat over.\n";
    return;
  }

  out << ansi::kCyan << "> AI:" << ansi::kReset << " ";

  if (rec.action.kind == sts2::input::Action::kEndTurn) {
    out << "End turn";
  } else if (rec.action.kind == sts2::input::Action::kPlayCard) {
    const auto& hand = combat.player().hand;
    out << "Play ";
    if (rec.action.card_idx >= 0 &&
        static_cast<std::size_t>(rec.action.card_idx) < hand.size()) {
      out << hand[static_cast<std::size_t>(rec.action.card_idx)].name;
    } else {
      out << "(none)";
    }
    if (target_is_live_enemy(combat, rec.target_idx)) {
      const auto& enemy =
          combat.enemies()[static_cast<std::size_t>(rec.target_idx)];
      const int disp = display_index_for_slot(combat, rec.target_idx);
      out << " -> [" << disp << "] " << enemy.name;
    }
    if (rec.survivor_discard_id != sts2::game::CardId::kNone) {
      out << "  (suggest discarding " << card_id_name(rec.survivor_discard_id)
          << ")";
    }
  }

  const std::ios::fmtflags saved_flags = out.flags();
  const std::streamsize saved_precision = out.precision();
  out << "   " << ansi::kYellow << std::fixed << std::setprecision(1)
      << "E[HP]=" << rec.expected_hp << "  E[turns]=" << rec.expected_rounds
      << ansi::kReset << "\n";
  out.flags(saved_flags);
  out.precision(saved_precision);

  out << "  PV: ";
  if (rec.principal_variation.empty()) {
    out << "(none)";
  } else {
    bool first = true;
    for (const auto& step : rec.principal_variation) {
      if (!first) out << ", ";
      first = false;
      write_pv_step(out, step, combat);
    }
    if (rec.principal_variation.back().kind == sts2::ai::PvStep::kEndTurn) {
      out << " (then chance)";
    }
  }
  out << "\n";
}

}  // namespace sts2::render
