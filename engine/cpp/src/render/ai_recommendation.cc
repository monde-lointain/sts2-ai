#include "sts2/render/ai_recommendation.h"

#include <cstddef>
#include <iomanip>
#include <ios>
#include <ostream>
#include <vector>

#include "render/render_internal.h"
#include "sts2/ai/recommend.h"
#include "sts2/game/card_effects.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemy.h"
#include "sts2/game/index_types.h"
#include "sts2/game/types.h"
#include "sts2/input/input.h"
#include "sts2/render/ansi.h"

namespace sts2::render {

namespace {

// Display string for a CardId. Handles kNone (which the metadata table does
// not cover) so callers like the survivor-discard path can print "(none)".
const char* card_id_name(sts2::game::CardId id) {
  if (id == sts2::game::CardId::kNone) {
    return "(none)";
  }
  return sts2::game::card_effects::card_effect_for(id).name.data();
}

void write_pv_step(std::ostream& out, const sts2::ai::PvStep& step,
                   const sts2::game::Combat& combat) {
  if (step.kind == sts2::ai::PvStep::kEndTurn) {
    out << "EndTurn";
    return;
  }
  out << card_id_name(step.card_id);
  if (step.target_idx.valid()) {
    if (step.target_idx.in_range(combat.enemies())) {
      const int disp = detail::display_index_of(combat, step.target_idx);
      if (disp >= 0) {
        out << " -> [" << disp << "] "
            << step.target_idx.at(combat.enemies()).name;
      } else {
        // Slot is dead in current state; fall back to slot index for
        // diagnostics.
        out << " -> [" << step.target_idx.raw() << "] "
            << step.target_idx.at(combat.enemies()).name;
      }
    } else {
      out << " -> " << step.target_idx.raw();
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
    out << "Play ";
    const sts2::game::HandIndex card_idx = rec.action.card_idx;
    if (card_idx.valid() && static_cast<std::size_t>(card_idx.raw()) <
                                combat.player().hand.size()) {
      out << combat.player().hand.at(card_idx).name;
    } else {
      out << "(none)";
    }
    if (sts2::game::is_alive(combat.enemies(), rec.target_idx)) {
      const auto& enemy = rec.target_idx.at(combat.enemies());
      const int disp = detail::display_index_of(combat, rec.target_idx);
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
      if (!first) {
        out << ", ";
      }
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
