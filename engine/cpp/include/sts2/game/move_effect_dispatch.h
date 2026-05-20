#pragma once

#include <concepts>
#include <cstdint>

#include "sts2/game/monster_moves.h"
#include "sts2/game/types.h"

namespace sts2::game {

template <typename T>
concept MoveEffectTarget =
    requires(T t, int32_t v, PowerKind pk, MoveEffectKind ek) {
      { t.attack_player(v) } -> std::convertible_to<bool>;
      t.gain_self_block(v);
      t.add_self_power(pk, v);
      t.add_player_frail(v);
      t.add_player_weak(v);
      t.add_player_vulnerable(v);
      t.add_player_discard_slimed(v);
      t.unsupported(ek);
    };

template <MoveEffectTarget Target>
void apply_move_effect(const monster_moves::MoveEffect& fx,
                       Target& t) noexcept {
  switch (fx.kind) {
    case MoveEffectKind::kAttack:
      (void)t.attack_player(fx.value);
      break;
    case MoveEffectKind::kDefend:
    case MoveEffectKind::kBlockSelf:
      t.gain_self_block(fx.value);
      break;
    case MoveEffectKind::kBuffSelf:
    case MoveEffectKind::kBuffEnemy:
      t.add_self_power(fx.power_kind, fx.value);
      break;
    case MoveEffectKind::kDebuffPlayer:
      switch (fx.power_kind) {
        case PowerKind::kFrail:
          t.add_player_frail(fx.value);
          break;
        case PowerKind::kWeak:
          t.add_player_weak(fx.value);
          break;
        case PowerKind::kVulnerable:
          t.add_player_vulnerable(fx.value);
          break;
        case PowerKind::kStrength:
        case PowerKind::kRitual:
        case PowerKind::kCurlUp:
          // Not used as player debuffs in Phase-1.
          break;
      }
      break;
    case MoveEffectKind::kAddStatusCard:
      t.add_player_discard_slimed(fx.value);
      break;
    case MoveEffectKind::kNone:
      break;
  }
}

}  // namespace sts2::game
