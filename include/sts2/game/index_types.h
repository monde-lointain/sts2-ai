#pragma once

#include <cstddef>

// Strong index types used throughout the engine and AI layers. EnemySlot
// references a slot in Combat::enemies(); HandIndex references a card in the
// player's hand. Both expose a `none()` sentinel — prefer `.valid()` over
// raw-int comparisons.

namespace sts2::game {

class EnemySlot {
 public:
  constexpr explicit EnemySlot(int v) noexcept : v_(v) {}

  [[nodiscard]] constexpr bool valid() const noexcept { return v_ >= 0; }
  [[nodiscard]] constexpr int raw() const noexcept { return v_; }

  template <typename C>
  [[nodiscard]] constexpr bool in_range(const C& c) const noexcept {
    return v_ >= 0 && static_cast<std::size_t>(v_) < c.size();
  }

  // Returns ref into c. Bind to a named variable, not a temporary expression
  // (e.g., `auto s = EnemySlot{idx}; auto& elem = s.at(v);`) to avoid GCC 13
  // -Wdangling-reference false positives.
  template <typename C>
  [[nodiscard]] constexpr auto& at(C& c) const noexcept {
    return c[static_cast<std::size_t>(v_)];
  }

  static constexpr EnemySlot none() noexcept { return EnemySlot{-1}; }

  bool operator==(const EnemySlot&) const = default;

 private:
  int v_;
};

static_assert(!EnemySlot::none().valid(), "EnemySlot::none() must be invalid");

class HandIndex {
 public:
  constexpr explicit HandIndex(int v) noexcept : v_(v) {}

  [[nodiscard]] constexpr bool valid() const noexcept { return v_ >= 0; }
  [[nodiscard]] constexpr int raw() const noexcept { return v_; }

  template <typename C>
  [[nodiscard]] constexpr bool in_range(const C& c) const noexcept {
    return v_ >= 0 && static_cast<std::size_t>(v_) < c.size();
  }

  // Returns ref into c. Bind to a named variable, not a temporary expression
  // (e.g., `auto h = HandIndex{idx}; auto& elem = h.at(v);`) to avoid GCC 13
  // -Wdangling-reference false positives.
  template <typename C>
  [[nodiscard]] constexpr auto& at(C& c) const noexcept {
    return c[static_cast<std::size_t>(v_)];
  }

  static constexpr HandIndex none() noexcept { return HandIndex{-1}; }

  bool operator==(const HandIndex&) const = default;

 private:
  int v_;
};

static_assert(!HandIndex::none().valid(), "HandIndex::none() must be invalid");

}  // namespace sts2::game
