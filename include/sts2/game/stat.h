#pragma once

#include <cstdint>

// Strong type wrapping uint8_t for character stats (hp, block, strength, weak,
// energy, dark_strike_base, ritual_amount). Encapsulates the truncating
// arithmetic that previously appeared as static_cast<uint8_t>(a + b) at every
// mutation site.

namespace sts2::game {

class Stat {
 public:
  constexpr Stat() noexcept = default;
  constexpr explicit Stat(int v) noexcept : v_(static_cast<uint8_t>(v)) {}

  [[nodiscard]] constexpr int value() const noexcept { return v_; }
  [[nodiscard]] constexpr uint8_t raw() const noexcept { return v_; }

  constexpr Stat& operator+=(int rhs) noexcept {
    v_ = static_cast<uint8_t>(v_ + rhs);
    return *this;
  }
  constexpr Stat& operator-=(int rhs) noexcept {
    v_ = static_cast<uint8_t>(v_ - rhs);
    return *this;
  }

  bool operator==(const Stat&) const = default;
  auto operator<=>(const Stat&) const = default;

 private:
  uint8_t v_ = 0;
};

}  // namespace sts2::game
