#pragma once

// Internal helpers for render.cc. Test-only header. Not part of the public sts2::simulator API.

#include <cstddef>
#include <string>
#include <vector>

#include "sts2/game/enemy.h"
#include "sts2/game/player.h"
#include "sts2/game/power.h"

namespace render::detail {

inline constexpr int kPlayerHpBarWidth = 20;
inline constexpr int kEnemyHpBarWidth = 16;
inline constexpr int kSeparatorLen = 60;

std::string repeat_utf8(const char* utf8_glyph, int count);
std::string spaces(std::size_t n);
const char* power_color(PowerKind kind);
const char* power_name(PowerKind kind);
std::string format_powers(const std::vector<Power>& ps);
std::string format_intent(const Enemy& e);
std::size_t max_enemy_name_len(const std::vector<Enemy>& es);
int total_deck_size(const Player& p);

}
