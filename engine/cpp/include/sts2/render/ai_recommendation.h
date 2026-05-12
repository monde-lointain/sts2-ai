#pragma once

#include <iosfwd>

#include "sts2/ai/recommend.h"

namespace sts2::game {
class Combat;
}  // namespace sts2::game

namespace sts2::render {

// Render the AI's recommendation block beneath the battle UI. Format:
//   > AI: Play <Card>[ -> <enemy_name> [<idx>]]   E[HP]=42.7  E[turns]=8.2
//     PV: Strike->0, Defend, Strike->1, EndTurn (then chance)
//
// For Survivor recommendations, append "(suggest discarding <Card>)".
// For combat_over=true, render nothing (or a single confirmation line).
void render_ai_recommendation(const sts2::ai::Recommendation& rec,
                              const sts2::game::Combat& combat,
                              std::ostream& out);

}  // namespace sts2::render
