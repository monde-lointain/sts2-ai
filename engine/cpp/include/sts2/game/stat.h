#pragma once

#include <algorithm>
#include <cassert>
#include <cstdint>
#include <ostream>

// Strong type for character stats (hp, block, strength, weak, energy,
// dark_strike_base, ritual_amount). Backing matches the original Godot source's
// PowerModel.SetAmount semantics: int storage, +/-1e9 saturation, allows
// negative. Zero-clamping for HP/block/energy lives at the caller layer (see
// damage_calc.h::apply_to_defender), not inside Stat.

namespace sts2::game {

class Stat {
 public:
  static constexpr int kMaxClamp = 999'999'999;

  constexpr Stat() noexcept = default;
  constexpr explicit Stat(int v) noexcept
      : v_(std::clamp(v, -kMaxClamp, kMaxClamp)) {}

  [[nodiscard]] constexpr int value() const noexcept { return v_; }

  // Byte view for the search-hash packer. Caller guarantees [0, 255]; values
  // outside that range indicate the search domain has expanded past what the
  // 8-bit-per-stat hash layout assumes.
  [[nodiscard]] constexpr uint8_t pack8() const noexcept {
    assert(v_ >= 0 && v_ <= 255);
    return static_cast<uint8_t>(v_);
  }

  constexpr Stat& operator+=(int rhs) noexcept {
    v_ = std::clamp(v_ + rhs, -kMaxClamp, kMaxClamp);
    return *this;
  }
  constexpr Stat& operator-=(int rhs) noexcept {
    v_ = std::clamp(v_ - rhs, -kMaxClamp, kMaxClamp);
    return *this;
  }

  bool operator==(const Stat&) const = default;
  auto operator<=>(const Stat&) const = default;

 private:
  int32_t v_ = 0;
};

inline std::ostream& operator<<(std::ostream& os, Stat s) {
  return os << s.value();
}

}  // namespace sts2::game
