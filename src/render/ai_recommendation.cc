#include "sts2/render/ai_recommendation.h"

#include <cstddef>
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
  return "(none)";
}

bool target_is_live_enemy(const sts2::game::Combat& combat, int idx) {
  if (idx < 0) return false;
  const auto& es = combat.enemies();
  if (static_cast<std::size_t>(idx) >= es.size()) return false;
  return es[static_cast<std::size_t>(idx)].vitals.hp > 0;
}

void write_pv_step(std::ostream& out, const sts2::ai::PvStep& step) {
  if (step.kind == sts2::ai::PvStep::kEndTurn) {
    out << "EndTurn";
    return;
  }
  out << card_id_name(step.card_id);
  if (step.target_idx >= 0) {
    out << "->" << step.target_idx;
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
    sts2::game::CardId played_id = sts2::game::CardId::kNone;
    if (rec.action.card_idx >= 0 &&
        static_cast<std::size_t>(rec.action.card_idx) < hand.size()) {
      played_id = hand[static_cast<std::size_t>(rec.action.card_idx)].id;
    }
    out << "Play " << card_id_name(played_id);
    if (target_is_live_enemy(combat, rec.target_idx)) {
      const auto& enemy =
          combat.enemies()[static_cast<std::size_t>(rec.target_idx)];
      out << " -> " << enemy.name << " [" << rec.target_idx << "]";
    }
    if (rec.survivor_discard_id != sts2::game::CardId::kNone) {
      out << "  (suggest discarding " << card_id_name(rec.survivor_discard_id)
          << ")";
    }
  }

  out << "   " << ansi::kYellow << std::fixed << std::setprecision(1)
      << "E[HP]=" << rec.expected_hp << "  E[turns]=" << rec.expected_rounds
      << ansi::kReset << "\n";

  out << "  PV: ";
  if (rec.principal_variation.empty()) {
    out << "(none)";
  } else {
    bool first = true;
    for (const auto& step : rec.principal_variation) {
      if (!first) out << ", ";
      first = false;
      write_pv_step(out, step);
    }
    if (rec.principal_variation.back().kind == sts2::ai::PvStep::kEndTurn) {
      out << " (then chance)";
    }
  }
  out << "\n";
}

}  // namespace sts2::render
