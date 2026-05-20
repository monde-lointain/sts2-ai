#pragma once

#include <algorithm>
#include <array>
#include <cassert>
#include <cstddef>
#include <cstdint>

#include "sts2/game/types.h"  // PowerKind, CardId

namespace sts2::ai {

// ---------------------------------------------------------------------------
// PowerInstance — generic per-creature power entry (wave-17+).
// POD; 8 bytes (post-wave-23/J.beta); stable layout (static_assert).
//
// Layout: stacks(int32_t, 4) + kind(1) + flags(1) + curl_up_card_stamp(1)
//         + _reserved(1) = 8 B, struct alignment 4 (from int32_t stacks).
//
// Wave-23/J.beta widened stacks int16_t → int32_t to match upstream STS2's
// uniform int stat storage (Q2-ADR-014). PowerKind backing remains uint8_t
// (wave-22-fix-4/H.gamma). Net sizeof 6 → 8 (driven by int32 alignment).
// ---------------------------------------------------------------------------
struct PowerInstance {
  int32_t stacks = 0;
  sts2::game::PowerKind kind = sts2::game::PowerKind::kWeak;
  // bit 0: just_applied (used by Ritual to skip strength grant on spawn turn)
  uint8_t flags = 0;
  // Stores CurlUp card-stamp (CardId cast to uint8_t); 0 = kNone / not stored.
  // Accessed via: sts2::ai::powers::curl_up_card / set_curl_up_card.
  uint8_t curl_up_card_stamp = 0;
  uint8_t _reserved = 0;

  bool operator==(const PowerInstance&) const = default;
};
static_assert(sizeof(PowerInstance) == 8,
              "Wave-23/J.beta: PowerInstance must be 8 B (int32 stacks + "
              "1 B kind + 1 B flags + 1 B curl_up_card_stamp + 1 B _reserved)");

// Max powers stored per creature (EnemyState or player slot in CompactState).
// Sized for Phase-1 monsters; cultist uses 1 (Ritual), louse uses 1 (CurlUp),
// slimes use 0. Wave-22-fix-4/H.gamma narrowed 6→4: kMaxSpawnPowers=3 (spawn)
// + worst-case mid-combat adds (Weak/Frail) ≤ 1 currently observed; 4 absorbs
// up to spawn(1) + worst-case mid-combat(3) safely. Add_power asserts on
// overflow so future content additions surface immediately.
// Q2-ADR-013 Amendment 4 §Power-array-bound.
constexpr uint8_t kMaxPowersPerCreature = 4;  // was 6 (pre-wave-22-fix-4)

// Max enemies in a CompactState.
// Wave-21.β widens 2 → 4 to unblock the SmallSlimes (N=3) port (wave-22). The
// wave-19 Zobrist hash-only TT path keeps per-entry size at 38 B regardless
// of CompactState size, so the kMaxEnemies bump grows only the Zobrist key
// tables themselves (~1.2 MB → ~2.4 MB total; trivial vs. TT working set).
//
// Historical note: wave-17 dropped 4 → 2 when the power-array refactor grew
// sizeof(EnemyState) ~25 bytes and the pre-Zobrist TT keyed on raw bytes →
// 4 enemy slots × 85M TT entries projected ~17 GB extra → cultist OOM-kill.
// Wave-19's hash-only TT removed that coupling; wave-21 restores N=4.
//
// Zobrist table-fill order audit (wave-21.β): tables in zobrist.cc must
// APPEND new slots 2+3 to the mt19937_64 consumption sequence — slots 0+1
// preserve pre-wave-21 byte-identity (cultist + LouseProgenitor pins hold).
constexpr uint8_t kMaxEnemies = 4;

// ---------------------------------------------------------------------------
// Power helpers (find/add/set flags)
// ---------------------------------------------------------------------------
namespace powers {

[[nodiscard]] inline PowerInstance* find_power(
    std::array<PowerInstance, kMaxPowersPerCreature>& arr, uint8_t count,
    sts2::game::PowerKind kind) noexcept {
  auto it =
      std::find_if(arr.begin(), arr.begin() + count,
                   [kind](const PowerInstance& p) { return p.kind == kind; });
  return (it != arr.begin() + count) ? &*it : nullptr;
}

[[nodiscard]] inline const PowerInstance* find_power(
    const std::array<PowerInstance, kMaxPowersPerCreature>& arr, uint8_t count,
    sts2::game::PowerKind kind) noexcept {
  auto it =
      std::find_if(arr.begin(), arr.begin() + count,
                   [kind](const PowerInstance& p) { return p.kind == kind; });
  return (it != arr.begin() + count) ? &*it : nullptr;
}

// Add stacks to an existing power, or insert a new entry. Returns ref.
// Wave-23/J.beta: stacks widened int16_t → int32_t (Q2-ADR-014).
inline PowerInstance& add_power(
    std::array<PowerInstance, kMaxPowersPerCreature>& arr, uint8_t& count,
    sts2::game::PowerKind kind, int32_t stacks) noexcept {
  PowerInstance* existing = find_power(arr, count, kind);
  if (existing != nullptr) {
    existing->stacks = existing->stacks + stacks;
    return *existing;
  }
  assert(count < kMaxPowersPerCreature && "powers array full");
  arr[count] = PowerInstance{stacks, kind, 0, 0, 0};
  return arr[count++];
}

// Set a power to an exact stack value (replaces existing or inserts).
// Wave-23/J.beta: stacks widened int16_t → int32_t (Q2-ADR-014).
inline PowerInstance& set_power(
    std::array<PowerInstance, kMaxPowersPerCreature>& arr, uint8_t& count,
    sts2::game::PowerKind kind, int32_t stacks) noexcept {
  PowerInstance* existing = find_power(arr, count, kind);
  if (existing != nullptr) {
    existing->stacks = stacks;
    return *existing;
  }
  assert(count < kMaxPowersPerCreature && "powers array full");
  arr[count] = PowerInstance{stacks, kind, 0, 0, 0};
  return arr[count++];
}

// Return stack count for kind, or 0 if absent.
// Wave-23/J.beta: return widened int16_t → int32_t (Q2-ADR-014).
[[nodiscard]] inline int32_t stacks_of(
    const std::array<PowerInstance, kMaxPowersPerCreature>& arr, uint8_t count,
    sts2::game::PowerKind kind) noexcept {
  const PowerInstance* p = find_power(arr, count, kind);
  return (p != nullptr) ? p->stacks : 0;
}

// Wave-26/M.α: remove a PowerInstance entirely (slot-shift), regardless of
// stack count. Sibling to add_power; the existing add/set/stacks_of helpers
// in this namespace operate on the same arr+count refs. Intended for
// one-shot powers like kSurprise that must be consumed on trigger
// (do_surprise_spawn removes kSurprise from the dead enemy so a hypothetical
// re-trigger on the same slot is a no-op).
inline void remove_power(std::array<PowerInstance, kMaxPowersPerCreature>& arr,
                         uint8_t& count, sts2::game::PowerKind kind) noexcept {
  auto it =
      std::find_if(arr.begin(), arr.begin() + count,
                   [kind](const PowerInstance& p) { return p.kind == kind; });
  if (it == arr.begin() + count) {
    return;
  }
  const uint8_t i = static_cast<uint8_t>(it - arr.begin());
  for (uint8_t j = i; j + 1 < count; ++j) {
    arr[j] = arr[j + 1];
  }
  --count;
  arr[count] = {};
}

}  // namespace powers

// Forward-declared here; defined after PowerArray below.
class PowerArray;

namespace powers {

// ---------------------------------------------------------------------------
// Free helpers for Ritual just_applied flag and CurlUp card-stamp.
// These live in the powers:: namespace to keep PowerArray's public surface
// generic. They use ONLY PowerArray's public API (find, find_mut, add,
// remove) — no raw data_/count_ access.
//
// CRITICAL semantic distinction (set_just_applied_ritual(false) vs clear):
//   set(false)  — if entry exists: clears bit, KEEPS entry even if empty.
//                 if absent: early return (no-op, no materialize).
//   clear()     — if entry exists: clears bit, REMOVES entry if stacks==0
//                 && flags==0.
//                 if absent: early return.
// DO NOT collapse these two helpers — the distinction is load-bearing for
// Builder{}.just_applied_ritual(true).just_applied_ritual(false).build()
// byte-parity (and thus the cultist Zobrist BYTE pin).
// ---------------------------------------------------------------------------

[[nodiscard]] inline sts2::game::CardId curl_up_card(
    const PowerArray& p) noexcept;
inline void set_curl_up_card(PowerArray& p, sts2::game::CardId id) noexcept;
[[nodiscard]] inline bool just_applied_ritual(const PowerArray& p) noexcept;
inline void set_just_applied_ritual(PowerArray& p, bool value) noexcept;
inline void clear_just_applied_ritual(PowerArray& p) noexcept;

}  // namespace powers

// ---------------------------------------------------------------------------
// PowerArray — append-only value class wrapping the per-creature power array.
// Insertion order = wire order = Zobrist slot order (load-bearing invariant).
// All methods are inline so the search hot-loop pays no cross-TU call overhead.
// ---------------------------------------------------------------------------
class PowerArray {
 public:
  [[nodiscard]] uint8_t count() const noexcept { return count_; }
  [[nodiscard]] const std::array<PowerInstance, kMaxPowersPerCreature>& data()
      const noexcept {
    return data_;
  }

  [[nodiscard]] int32_t stacks_of(sts2::game::PowerKind kind) const noexcept {
    return powers::stacks_of(data_, count_, kind);
  }

  [[nodiscard]] const PowerInstance* find(
      sts2::game::PowerKind kind) const noexcept {
    return powers::find_power(data_, count_, kind);
  }

  [[nodiscard]] PowerInstance* find_mut(sts2::game::PowerKind kind) noexcept {
    return powers::find_power(data_, count_, kind);
  }

  PowerInstance& add(sts2::game::PowerKind kind, int32_t stacks) noexcept {
    return powers::add_power(data_, count_, kind, stacks);
  }

  PowerInstance& set(sts2::game::PowerKind kind, int32_t stacks) noexcept {
    return powers::set_power(data_, count_, kind, stacks);
  }

  void remove(sts2::game::PowerKind kind) noexcept {
    powers::remove_power(data_, count_, kind);
  }

  void decrement(sts2::game::PowerKind kind) noexcept {
    PowerInstance* p = powers::find_power(data_, count_, kind);
    if (p == nullptr) {
      return;
    }
    --p->stacks;
    if (p->stacks <= 0) {
      powers::remove_power(data_, count_, kind);
    }
  }

  bool operator==(const PowerArray&) const = default;

 private:
  std::array<PowerInstance, kMaxPowersPerCreature> data_{};
  uint8_t count_ = 0;
};

// ---------------------------------------------------------------------------
// powers:: free helper bodies (inline; defined after PowerArray is complete).
// ---------------------------------------------------------------------------
namespace powers {

[[nodiscard]] inline sts2::game::CardId curl_up_card(
    const PowerArray& p) noexcept {
  const PowerInstance* inst = p.find(sts2::game::PowerKind::kCurlUp);
  return (inst != nullptr)
             ? static_cast<sts2::game::CardId>(inst->curl_up_card_stamp)
             : sts2::game::CardId::kNone;
}

inline void set_curl_up_card(PowerArray& p, sts2::game::CardId id) noexcept {
  PowerInstance* inst = p.find_mut(sts2::game::PowerKind::kCurlUp);
  if (inst != nullptr) {
    inst->curl_up_card_stamp = static_cast<uint8_t>(id);
  }
}

[[nodiscard]] inline bool just_applied_ritual(const PowerArray& p) noexcept {
  const PowerInstance* inst = p.find(sts2::game::PowerKind::kRitual);
  return (inst != nullptr) && ((inst->flags & 0x01U) != 0);
}

inline void set_just_applied_ritual(PowerArray& p, bool value) noexcept {
  if (value) {
    PowerInstance* inst = p.find_mut(sts2::game::PowerKind::kRitual);
    if (inst == nullptr) {
      // Materialise kRitual entry with stacks=0 (Invariant #5: load-bearing
      // for from_combat byte-parity). Mirror existing PowerArray method
      // exactly.
      inst = &p.add(sts2::game::PowerKind::kRitual, 0);
    }
    inst->flags |= 0x01U;
  } else {
    PowerInstance* inst = p.find_mut(sts2::game::PowerKind::kRitual);
    if (inst != nullptr) {
      // Clear bit; KEEP entry even if stacks==0 + flags==0 after clear.
      // This differs from clear_just_applied_ritual which REMOVES the entry.
      inst->flags &= static_cast<uint8_t>(~0x01U);
    }
    // If absent: early return (no-op, no materialize).
  }
}

inline void clear_just_applied_ritual(PowerArray& p) noexcept {
  PowerInstance* inst = p.find_mut(sts2::game::PowerKind::kRitual);
  if (inst == nullptr) {
    return;
  }
  inst->flags &= static_cast<uint8_t>(~0x01U);
  if (inst->stacks == 0 && inst->flags == 0) {
    p.remove(sts2::game::PowerKind::kRitual);
  }
}

}  // namespace powers

}  // namespace sts2::ai
