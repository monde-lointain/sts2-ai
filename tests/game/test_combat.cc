// Tests for src/game/combat.{h,cc}.
// Spec: docs/test-plan/02-test-specifications.md §10 (T-CMB-005..290).
//
// Pinned-value caveat: tests using kCombatTestSeed (0xC0FFEEULL) for shuffles
// are toolchain-locked via tests/seeds/expected_values.h
// (kSilentDeckShuffled_C0FFEE). Most assertions here check state (counts, HP,
// pile sizes, power vectors) rather than specific card orderings, so the seed
// pin only matters where noted. Cultist HP rolls under that seed are observed
// via the public API (e.g. enemies()[0].vitals.hp) without re-pinning concrete
// numbers.
//
// Public-API constraint: tests exercise Combat through its public surface
// only. A handful of spec items reach private state that no public method
// exposes — those are documented inline (T-CMB-110) or use GTEST_SKIP with a
// §14.3 reference.

#include <gtest/gtest.h>

#include <algorithm>
#include <cstddef>
#include <utility>
#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemies.h"
#include "sts2/game/enemy.h"
#include "sts2/game/index_types.h"
#include "sts2/game/player.h"
#include "sts2/game/rng.h"
#include "sts2/game/types.h"
#include "sts2/game/vitals.h"
#include "render/render_internal.h"
#include "tests/game/test_helpers.h"
#include "tests/seeds/expected_values.h"

namespace {

using sts2::tests::helpers::DrainPlayerEnergy;
using sts2::tests::helpers::ExpectPowersEq;
using sts2::tests::helpers::KillEnemy;
using sts2::tests::helpers::MakeCombatWithEnemy;
using sts2::tests::helpers::MakePower;
using sts2::tests::helpers::MakeStarterCombat;
using sts2::tests::seeds::kCombatTestSeed;

using Card = sts2::game::Card;
using CardId = sts2::game::CardId;
using Combat = sts2::game::Combat;
using Enemy = sts2::game::Enemy;
using MoveId = sts2::game::MoveId;
using PowerKind = sts2::game::PowerKind;
using Rng = sts2::game::Rng;
using Vitals = sts2::game::Vitals;

// Build the small "fixed deck" used by §10.1's T-CMB-010 setup.
std::vector<Card> MakeStrikeDefendDeck7() {
  std::vector<Card> d;
  d.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  d.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  d.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  d.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  d.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  d.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  d.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  return d;
}

// Find the first hand index of a card with the given id; returns -1 if absent.
int FindHandIndex(const Combat& c, CardId id) {
  return c.find_card_in_hand(id).raw();
}

// -------------------------------------------------------------------------
// 10.1  Construction, start, add_enemy, set_pick_discard_callback,
//       pure delegators, deal_damage_to_enemy, enemy_attack_player
// -------------------------------------------------------------------------

// T-CMB-005 — BP — Constructor leaves combat_over=false, round=1, no enemies,
// empty piles, energy=0 (set later in start_player_turn), hp=70/70.
TEST(CombatConstruction, T_CMB_005_DefaultState) {
  Combat c{kCombatTestSeed};

  EXPECT_FALSE(c.combat_over());
  EXPECT_EQ(c.round(), 1);
  EXPECT_EQ(c.player().energy, 0);
  EXPECT_EQ(c.player().vitals.hp, 70);
  EXPECT_EQ(c.player().vitals.max_hp, 70);
  EXPECT_TRUE(c.enemies().empty());
  EXPECT_EQ(c.player().deck.draw_size(), 0U);
  EXPECT_TRUE(c.player().hand.empty());
  EXPECT_EQ(c.player().deck.discard_size(), 0U);
}

// T-CMB-010 — BP, DF — start(deck) shuffles and triggers start_player_turn,
// drawing 7 (5 base + 2 Ring of the Snake). Deck size 7 → all 7 land in hand.
TEST(CombatConstruction, T_CMB_010_StartTriggersStartPlayerTurn) {
  Combat c{kCombatTestSeed};
  Rng enemy_rng{kCombatTestSeed};
  c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));
  c.add_enemy(sts2::enemies::make_damp_cultist(enemy_rng));

  c.start(MakeStrikeDefendDeck7());

  EXPECT_EQ(c.round(), 1);
  EXPECT_FALSE(c.combat_over());
  EXPECT_EQ(c.player().hand.size(), 7U);
  EXPECT_EQ(c.player().deck.draw_size(), 0U);
  EXPECT_EQ(c.player().deck.discard_size(), 0U);
  EXPECT_EQ(c.player().energy, 3);
}

// T-CMB-015 — DF — Starter deck of 12 → R1 hand size 7, draw pile 5.
TEST(CombatConstruction, T_CMB_015_StarterDeckRingDrawsSeven) {
  Combat c = MakeStarterCombat(kCombatTestSeed);

  EXPECT_EQ(c.player().hand.size(), 7U);
  EXPECT_EQ(c.player().deck.draw_size(), 5U);
}

// T-CMB-020 — BP — add_enemy appends; multiple appends preserve order.
TEST(CombatAddEnemy, T_CMB_020_AppendPreservesOrder) {
  Combat c{kCombatTestSeed};
  Rng enemy_rng{kCombatTestSeed};
  c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));
  c.add_enemy(sts2::enemies::make_damp_cultist(enemy_rng));

  ASSERT_EQ(c.enemies().size(), 2U);
  EXPECT_EQ(c.enemies()[0].name, "Calcified Cultist");
  EXPECT_EQ(c.enemies()[1].name, "Damp Cultist");
}

// T-CMB-025 — BP, DF — set_pick_discard_callback is consulted by
// discard_chosen_from_hand. Callback returns 1 → hand[1] moves to discard.
// Uses an all-Strikes deck of size 3 so the resulting hand is shuffle-order
// agnostic in identity (all Strikes), but indices 0/2 remain after the move.
TEST(CombatAddEnemy, T_CMB_025_PickDiscardCallbackInstalled) {
  Combat c{kCombatTestSeed};
  c.set_pick_discard_callback(
      [](const Combat&) { return sts2::game::HandIndex{1}; });

  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kNeutralize));
  c.start(std::move(deck));

  ASSERT_EQ(c.player().hand.size(), 3U);
  const Card pre_idx0 = c.player().hand.cards()[0];  // copy for later id comparison
  const Card pre_idx1 = c.player().hand.cards()[1];
  const Card pre_idx2 = c.player().hand.cards()[2];

  c.discard_chosen_from_hand();

  ASSERT_EQ(c.player().hand.size(), 2U);
  ASSERT_EQ(c.player().deck.discard_size(), 1U);
  EXPECT_EQ(c.player().deck.discard_pile()[0].id, pre_idx1.id);
  // Pre-call indices 0 and 2 remain in the hand, in order.
  EXPECT_EQ(c.player().hand.cards()[0].id, pre_idx0.id);
  EXPECT_EQ(c.player().hand.cards()[1].id, pre_idx2.id);
}

// T-CMB-030 — BP — gain_player_block(5) adds 5; successive calls accumulate.
TEST(CombatDelegators, T_CMB_030_GainPlayerBlockAccumulates) {
  Combat c{kCombatTestSeed};
  c.gain_player_block(5);
  EXPECT_EQ(c.player().vitals.block, 5);
  c.gain_player_block(5);
  EXPECT_EQ(c.player().vitals.block, 10);
}

// T-CMB-035 — BP — apply_power_to_enemy delegates to powers::apply.
// Re-application accumulates amount.
TEST(CombatDelegators, T_CMB_035_ApplyPowerToEnemyAccumulates) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed);

  c.apply_power_to_enemy(sts2::game::EnemySlot{0}, PowerKind::kWeak, 1);
  ASSERT_EQ(c.enemies()[0].vitals.powers.size(), 1U);
  EXPECT_EQ(c.enemies()[0].vitals.powers[0].kind, PowerKind::kWeak);
  EXPECT_EQ(c.enemies()[0].vitals.powers[0].amount, 1);

  c.apply_power_to_enemy(sts2::game::EnemySlot{0}, PowerKind::kWeak, 2);
  ASSERT_EQ(c.enemies()[0].vitals.powers.size(), 1U);
  EXPECT_EQ(c.enemies()[0].vitals.powers[0].amount, 3);
}

// T-CMB-040 — BP — apply_power_to_enemy_self delegates correctly.
// Verified by reading the passed-in enemy's powers afterwards.
TEST(CombatDelegators, T_CMB_040_ApplyPowerToEnemySelf) {
  Combat c{kCombatTestSeed};
  Enemy e{};
  e.vitals = Vitals{40, 40, 0, {}};

  c.apply_power_to_enemy_self(e, PowerKind::kRitual, 2);

  ASSERT_EQ(e.vitals.powers.size(), 1U);
  EXPECT_EQ(e.vitals.powers[0].kind, PowerKind::kRitual);
  EXPECT_EQ(e.vitals.powers[0].amount, 2);
  // Newly-applied Ritual sets just_applied=true (per powers::apply).
  EXPECT_TRUE(e.vitals.powers[0].just_applied);
}

// T-CMB-045 — BP — is_player_dead() returns TRUE when hp<=0, FALSE when hp>0.
// Set hp=0 by attacking with a temp enemy dealing exactly 70.
TEST(CombatDelegators, T_CMB_045_IsPlayerDeadHpThreshold) {
  Combat c{kCombatTestSeed};
  EXPECT_FALSE(c.is_player_dead());  // hp = 70 by default

  Enemy attacker{};
  attacker.vitals = Vitals{1, 1, 0, {}};
  c.enemy_attack_player(attacker, 70);

  EXPECT_EQ(c.player().vitals.hp, 0);
  EXPECT_TRUE(c.is_player_dead());
}

// T-CMB-050 — BP, DF — deal_damage_to_enemy(0, 6) with no powers reduces hp
// by 6.
TEST(CombatDealDamage, T_CMB_050_DealsRawDamageNoPowers) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed, 40);

  c.deal_damage_to_enemy(sts2::game::EnemySlot{0}, 6);

  EXPECT_EQ(c.enemies()[0].vitals.hp, 34);
  EXPECT_FALSE(c.combat_over());
}

// T-CMB-055 — EG — Lethal damage on one of two enemies does NOT trip
// combat_over.
TEST(CombatDealDamage, T_CMB_055_LethalWhileOtherAlive) {
  Combat c{kCombatTestSeed};
  Enemy e0{};
  e0.vitals = Vitals{1, 1, 0, {}};
  Enemy e1{};
  e1.vitals = Vitals{40, 40, 0, {}};
  c.add_enemy(std::move(e0));
  c.add_enemy(std::move(e1));

  c.deal_damage_to_enemy(sts2::game::EnemySlot{0}, 99);

  EXPECT_EQ(c.enemies()[0].vitals.hp, 0);
  EXPECT_EQ(c.enemies()[1].vitals.hp, 40);
  EXPECT_FALSE(c.combat_over());
}

// T-CMB-060 — EG — Lethal to the LAST enemy trips combat_over via
// all_enemies_dead.
TEST(CombatDealDamage, T_CMB_060_LethalLastEnemyTripsCombatOver) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed, /*hp=*/1);

  c.deal_damage_to_enemy(sts2::game::EnemySlot{0}, 99);

  EXPECT_EQ(c.enemies()[0].vitals.hp, 0);
  EXPECT_TRUE(c.combat_over());
}

// T-CMB-065 — BP, DF — enemy_attack_player reads source's powers (Strength 2),
// not the player's. Damage = 9 + 2 = 11. Player hp = 70 - 11 = 59.
TEST(CombatDealDamage, T_CMB_065_EnemyAttackUsesSourcePowers) {
  Combat c{kCombatTestSeed};
  Enemy source{};
  source.vitals = Vitals{40, 40, 0, {MakePower(PowerKind::kStrength, 2)}};

  c.enemy_attack_player(source, 9);

  EXPECT_EQ(c.player().vitals.hp, 59);
}

// T-CMB-070 — EG — Lethal enemy_attack_player trips combat_over.
TEST(CombatDealDamage, T_CMB_070_LethalEnemyAttackTripsCombatOver) {
  Combat c{kCombatTestSeed};
  // Reduce player to 1 hp first.
  Enemy attacker{};
  attacker.vitals = Vitals{1, 1, 0, {}};
  c.enemy_attack_player(attacker, 69);
  ASSERT_EQ(c.player().vitals.hp, 1);
  ASSERT_FALSE(c.combat_over());

  c.enemy_attack_player(attacker, 5);

  EXPECT_TRUE(c.combat_over());
  EXPECT_TRUE(c.is_player_dead());
}

// -------------------------------------------------------------------------
// 10.2  can_play
// -------------------------------------------------------------------------

// T-CMB-075 — BP, BV — idx == -1 → false (left of `||` short-circuits).
TEST(CombatCanPlay, T_CMB_075_NegativeIndex) {
  Combat c{kCombatTestSeed};
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 1U);

  EXPECT_FALSE(c.can_play(sts2::game::HandIndex{-1}));
}

// T-CMB-080 — BP, BV — idx == hand.size() (just past) → false.
TEST(CombatCanPlay, T_CMB_080_IndexEqualsSize) {
  Combat c{kCombatTestSeed};
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 3U);

  EXPECT_FALSE(c.can_play(sts2::game::HandIndex{3}));
}

// T-CMB-085 — BP, BV — Last valid idx, cost > energy → false.
// Use a 6-card all-Strike deck so the post-start hand is 6, then drain energy
// to 0 (each Strike spent removes one card). End state: hand=3, energy=0.
// can_play(2) → last valid idx, cost 1 > energy 0 → false.
TEST(CombatCanPlay, T_CMB_085_LastValidIdxUnaffordable) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed, /*hp=*/9999);
  std::vector<Card> deck;
  deck.reserve(6);
  for (int i = 0; i < 6; ++i) {
    deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  }
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 6U);
  ASSERT_EQ(c.player().energy, 3);

  DrainPlayerEnergy(c);
  ASSERT_EQ(c.player().energy, 0);
  ASSERT_EQ(c.player().hand.size(), 3U);

  // Last valid idx (2); cost 1 > energy 0.
  EXPECT_FALSE(c.can_play(sts2::game::HandIndex{2}));
}

// T-CMB-090 — BP — Valid idx, cost == energy → true (boundary).
TEST(CombatCanPlay, T_CMB_090_BoundaryCostEqualsEnergy) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed, /*hp=*/9999);
  std::vector<Card> deck;
  deck.reserve(6);
  for (int i = 0; i < 6; ++i) {
    deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  }
  c.start(std::move(deck));
  // Spend two energy, leaving energy=1 == Strike cost.
  ASSERT_TRUE(c.play_card(sts2::game::HandIndex{0}, sts2::game::EnemySlot{0}));
  ASSERT_TRUE(c.play_card(sts2::game::HandIndex{0}, sts2::game::EnemySlot{0}));
  ASSERT_EQ(c.player().energy, 1);

  EXPECT_TRUE(c.can_play(sts2::game::HandIndex{0}));
}

// T-CMB-095 — EG — Cost 0 with 0 energy → true (boundary 0 <= 0).
// Neutralize cost 0. Spend all 3 energy first, then can_play on a Neutralize.
TEST(CombatCanPlay, T_CMB_095_ZeroCostZeroEnergy) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed, /*hp=*/9999);
  // Deck: 3 Strikes + 1 Neutralize. After start, hand has all 4 (deck<7).
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kNeutralize));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 4U);

  // Find Neutralize and play strikes (not Neutralize) to drain energy to 0.
  int strikes_played = 0;
  while (strikes_played < 3) {
    int idx = FindHandIndex(c, CardId::kStrike);
    ASSERT_NE(idx, -1);
    ASSERT_TRUE(c.play_card(sts2::game::HandIndex{idx}, sts2::game::EnemySlot{0}));
    ++strikes_played;
  }
  ASSERT_EQ(c.player().energy, 0);

  int neut_idx = FindHandIndex(c, CardId::kNeutralize);
  ASSERT_NE(neut_idx, -1);
  EXPECT_TRUE(c.can_play(sts2::game::HandIndex{neut_idx}));
}

// T-CMB-100 — EG — Empty hand any idx → false (D1 right operand fires).
TEST(CombatCanPlay, T_CMB_100_EmptyHand) {
  Combat c{kCombatTestSeed};
  ASSERT_TRUE(c.player().hand.empty());

  EXPECT_FALSE(c.can_play(sts2::game::HandIndex{0}));
}

// -------------------------------------------------------------------------
// 10.3  play_card
// -------------------------------------------------------------------------

// T-CMB-105 — BP — Unplayable returns false; no state change.
// Energy=3 by default after start; to be "unplayable", we drain it then
// attempt.
TEST(CombatPlayCard, T_CMB_105_UnplayableReturnsFalse) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed, /*hp=*/40);
  std::vector<Card> deck;
  deck.reserve(4);
  for (int i = 0; i < 4; ++i) {
    deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  }
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 4U);
  DrainPlayerEnergy(c);
  ASSERT_EQ(c.player().energy, 0);
  ASSERT_EQ(c.player().hand.size(), 1U);
  const int hp_before = c.enemies()[0].vitals.hp;
  const std::size_t discard_size_before = c.player().deck.discard_size();

  bool ok = c.play_card(sts2::game::HandIndex{0}, sts2::game::EnemySlot{0});

  EXPECT_FALSE(ok);
  EXPECT_EQ(c.player().hand.size(), 1U);
  EXPECT_EQ(c.player().hand.cards()[0].id, CardId::kStrike);
  EXPECT_EQ(c.player().energy, 0);
  EXPECT_EQ(c.enemies()[0].vitals.hp, hp_before);
  EXPECT_EQ(c.player().deck.discard_size(), discard_size_before);
}

// T-CMB-110 — Documented unreachable branch (every cards::make_* sets on_play).
// See test plan §14.3 U-1. Skipped for traceability.
TEST(CombatPlayCard, T_CMB_110_OnPlayFalsyUnreachable) {
  GTEST_SKIP() << "Unreachable via public API; see test plan §14.3 U-1";
}

// T-CMB-115 — BP, DF — Strike: hand→discard, energy spent, damage dealt,
// check_win_or_lose called (FALSE outcome).
TEST(CombatPlayCard, T_CMB_115_StrikePlaysAndDealsDamage) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed, /*hp=*/40);
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 1U);
  ASSERT_EQ(c.player().energy, 3);

  bool ok = c.play_card(sts2::game::HandIndex{0}, sts2::game::EnemySlot{0});

  EXPECT_TRUE(ok);
  EXPECT_TRUE(c.player().hand.empty());
  ASSERT_EQ(c.player().deck.discard_size(), 1U);
  EXPECT_EQ(c.player().deck.discard_pile()[0].id, CardId::kStrike);
  EXPECT_EQ(c.player().energy, 2);
  EXPECT_EQ(c.enemies()[0].vitals.hp, 34);
  EXPECT_FALSE(c.combat_over());
}

// T-CMB-120 — DF — Survivor: gain block AND discard via callback.
// The callback returns the post-removal index of Strike, so the discarded
// card is deterministically Strike regardless of shuffle. After play_card,
// the discard order is [Strike (pushed by discard_chosen_from_hand), Survivor
// (pushed by play_card)] and hand contains just Defend.
TEST(CombatPlayCard, T_CMB_120_SurvivorBlocksAndDiscards) {
  Combat c{kCombatTestSeed};
  c.set_pick_discard_callback([](const Combat& cc) {
    return cc.find_card_in_hand(CardId::kStrike).valid()
               ? cc.find_card_in_hand(CardId::kStrike)
               : sts2::game::HandIndex{0};
  });
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kSurvivor));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 3U);

  int survivor_idx = FindHandIndex(c, CardId::kSurvivor);
  ASSERT_NE(survivor_idx, -1);

  bool ok = c.play_card(sts2::game::HandIndex{survivor_idx}, sts2::game::EnemySlot::none());

  EXPECT_TRUE(ok);
  ASSERT_EQ(c.player().hand.size(), 1U);
  EXPECT_EQ(c.player().hand.cards()[0].id, CardId::kDefend);
  ASSERT_EQ(c.player().deck.discard_size(), 2U);
  EXPECT_EQ(c.player().deck.discard_pile()[0].id, CardId::kStrike);
  EXPECT_EQ(c.player().deck.discard_pile()[1].id, CardId::kSurvivor);
  EXPECT_EQ(c.player().vitals.block, 8);
}

// T-CMB-125 — EG — Lethal-on-play: dealing damage that kills the last enemy
// trips combat_over.
TEST(CombatPlayCard, T_CMB_125_LethalOnPlayTripsCombatOver) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed, /*hp=*/4);
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 1U);

  bool ok = c.play_card(sts2::game::HandIndex{0}, sts2::game::EnemySlot{0});

  EXPECT_TRUE(ok);
  EXPECT_EQ(c.enemies()[0].vitals.hp, 0);
  EXPECT_TRUE(c.combat_over());
}

// -------------------------------------------------------------------------
// 10.4  draw
// -------------------------------------------------------------------------

// T-CMB-130 — BP, BV — draw(0) is a no-op (D1 FALSE on entry).
TEST(CombatDraw, T_CMB_130_DrawZeroNoOp) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  const std::size_t hand_before = c.player().hand.size();
  const std::size_t draw_before = c.player().deck.draw_size();
  const std::size_t discard_before = c.player().deck.discard_size();

  c.draw(0);

  EXPECT_EQ(c.player().hand.size(), hand_before);
  EXPECT_EQ(c.player().deck.draw_size(), draw_before);
  EXPECT_EQ(c.player().deck.discard_size(), discard_before);
}

// T-CMB-135 — BP, EP — draw(3) from a populated draw pile: hand grows by 3,
// draw shrinks by 3, discard unchanged. Uses the 12-card starter deck where
// after R1 start hand=7, draw=5, discard=0.
TEST(CombatDraw, T_CMB_135_DrawThreeFromPopulatedDeck) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  ASSERT_EQ(c.player().hand.size(), 7U);
  ASSERT_EQ(c.player().deck.draw_size(), 5U);
  ASSERT_EQ(c.player().deck.discard_size(), 0U);

  c.draw(3);

  EXPECT_EQ(c.player().hand.size(), 10U);
  EXPECT_EQ(c.player().deck.draw_size(), 2U);
  EXPECT_EQ(c.player().deck.discard_size(), 0U);
}

// T-CMB-140 — BV — Hand cap clamp. start draws 7 (5+ring 2). Then draw(3)
// brings hand to 10 (kMaxHandSize). Subsequent draw(2) should be a no-op.
TEST(CombatDraw, T_CMB_140_HandCapClamp) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  c.draw(3);
  ASSERT_EQ(c.player().hand.size(), 10U);
  const std::size_t draw_before = c.player().deck.draw_size();

  c.draw(2);

  EXPECT_EQ(c.player().hand.size(), 10U);
  EXPECT_EQ(c.player().deck.draw_size(), draw_before);
}

// T-CMB-145 — BP, EG — Reshuffle when draw empties mid-loop.
// Setup: 8 Defend deck (no enemies needed; Defend targets Self). After start,
// hand=7, draw=1, discard=0. Play one Defend → hand=6, discard=1, draw=1.
// Then draw(2): iter 1 takes the last draw card (hand=7, draw=0); iter 2
// triggers reshuffle (D3 TRUE), draw=1 again, takes it (hand=8, draw=0).
TEST(CombatDraw, T_CMB_145_ReshuffleMidLoop) {
  Combat c{kCombatTestSeed};
  std::vector<Card> deck;
  deck.reserve(8);
  for (int i = 0; i < 8; ++i) {
    deck.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  }
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 7U);
  ASSERT_EQ(c.player().deck.draw_size(), 1U);
  ASSERT_EQ(c.player().deck.discard_size(), 0U);

  ASSERT_TRUE(c.play_card(sts2::game::HandIndex{0}, sts2::game::EnemySlot::none()));
  ASSERT_EQ(c.player().hand.size(), 6U);
  ASSERT_EQ(c.player().deck.discard_size(), 1U);
  ASSERT_EQ(c.player().deck.draw_size(), 1U);

  c.draw(2);

  EXPECT_EQ(c.player().hand.size(), 8U);
  EXPECT_EQ(c.player().deck.draw_size(), 0U);
  EXPECT_EQ(c.player().deck.discard_size(), 0U);
}

// T-CMB-150 — EG — Empty draw + empty discard short-circuits (D3 TRUE,
// D4 TRUE → return on iter 1). 7-card deck → start draws all 7, leaving
// draw=0 and discard=0; draw(3) is a no-op.
TEST(CombatDraw, T_CMB_150_EmptyDrawAndDiscardShortCircuits) {
  Combat c{kCombatTestSeed};
  c.start(MakeStrikeDefendDeck7());
  ASSERT_EQ(c.player().hand.size(), 7U);
  ASSERT_EQ(c.player().deck.draw_size(), 0U);
  ASSERT_EQ(c.player().deck.discard_size(), 0U);

  c.draw(3);

  EXPECT_EQ(c.player().hand.size(), 7U);
  EXPECT_EQ(c.player().deck.draw_size(), 0U);
  EXPECT_EQ(c.player().deck.discard_size(), 0U);
}

// -------------------------------------------------------------------------
// 10.5  reshuffle
// -------------------------------------------------------------------------

// T-CMB-155 — BP, BV — Empty discard → no-op (D1 FALSE on entry).
// 7-card deck → start draws all 7 → discard empty, draw empty, hand=7.
TEST(CombatReshuffle, T_CMB_155_EmptyDiscardNoOp) {
  Combat c{kCombatTestSeed};
  c.start(MakeStrikeDefendDeck7());
  ASSERT_EQ(c.player().deck.discard_size(), 0U);
  ASSERT_EQ(c.player().deck.draw_size(), 0U);
  const std::size_t hand_before = c.player().hand.size();

  c.reshuffle();

  EXPECT_EQ(c.player().hand.size(), hand_before);
  EXPECT_EQ(c.player().deck.draw_size(), 0U);
  EXPECT_EQ(c.player().deck.discard_size(), 0U);
}

// T-CMB-160 — BP, DF — Non-empty discard → drained into draw and shuffled.
// Setup: starter combat (12-card deck, R1 hand=7, draw=5). Play several
// cards to populate discard. Multiset of (pre-draw ∪ pre-discard) is
// preserved into draw_pile after reshuffle.
TEST(CombatReshuffle, T_CMB_160_NonEmptyDiscardDrainedAndShuffled) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  // Play three Defends from hand (each costs 1). Defends always exist at
  // some hand index in the starter deck draw; locate by id.
  for (int i = 0; i < 3; ++i) {
    int idx = FindHandIndex(c, CardId::kDefend);
    ASSERT_NE(idx, -1);
    ASSERT_TRUE(c.play_card(sts2::game::HandIndex{idx}, sts2::game::EnemySlot::none()));
  }
  ASSERT_EQ(c.player().deck.discard_size(), 3U);
  const std::size_t pre_draw = c.player().deck.draw_size();
  const std::size_t pre_discard = c.player().deck.discard_size();

  // Capture id multiset of pre-draw ∪ pre-discard.
  std::vector<CardId> expected_ids;
  for (const Card& cc : c.player().deck.draw_pile()) {
    expected_ids.push_back(cc.id);
  }
  for (const Card& cc : c.player().deck.discard_pile()) {
    expected_ids.push_back(cc.id);
  }
  std::sort(expected_ids.begin(), expected_ids.end());

  c.reshuffle();

  EXPECT_EQ(c.player().deck.discard_size(), 0U);
  EXPECT_EQ(c.player().deck.draw_size(), pre_draw + pre_discard);

  std::vector<CardId> got_ids;
  for (const Card& cc : c.player().deck.draw_pile()) {
    got_ids.push_back(cc.id);
  }
  std::sort(got_ids.begin(), got_ids.end());
  EXPECT_EQ(got_ids, expected_ids);
}

// -------------------------------------------------------------------------
// 10.6  end_player_turn
// -------------------------------------------------------------------------

// T-CMB-165 — BP, BV — Empty hand → D1 FALSE on entry; only ticks powers.
// Player powers can't be planted via the public API (no apply_power_to_player
// exists), so this test verifies the empty-hand path doesn't crash and leaves
// state otherwise unchanged. The Weak-vanishes assertion in the spec is
// observed indirectly: with no powers, tick_at_turn_end is a no-op, so any
// regression introducing a crash would still be caught here. See test plan
// §14.3 / combat.h:56 friend hook for the white-box alternative.
TEST(CombatEndPlayerTurn, T_CMB_165_EmptyHandTicksPowers) {
  Combat c{kCombatTestSeed};
  ASSERT_TRUE(c.player().hand.empty());
  ASSERT_EQ(c.player().deck.discard_size(), 0U);
  ASSERT_TRUE(c.player().vitals.powers.empty());

  c.end_player_turn();

  EXPECT_TRUE(c.player().hand.empty());
  EXPECT_EQ(c.player().deck.discard_size(), 0U);
  EXPECT_TRUE(c.player().vitals.powers.empty());
}

// T-CMB-170 — BP — Non-empty hand → discarded LIFO (push_back of back()).
// Use a 3-card deck of 3 distinct cards (Survivor cost 1 + Strike cost 1 +
// Defend cost 1) — note these collide on cost. Use Strike+Defend+Neutralize:
// distinct ids and we don't need to play them. After start, hand has all 3;
// we record their order, then end_player_turn. Discard ends as reverse hand.
TEST(CombatEndPlayerTurn, T_CMB_170_NonEmptyHandDiscardedLifo) {
  Combat c{kCombatTestSeed};
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kNeutralize));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 3U);

  // Snapshot pre-end hand ids in their current order.
  const CardId h0 = c.player().hand.cards()[0].id;
  const CardId h1 = c.player().hand.cards()[1].id;
  const CardId h2 = c.player().hand.cards()[2].id;

  c.end_player_turn();

  EXPECT_TRUE(c.player().hand.empty());
  ASSERT_EQ(c.player().deck.discard_size(), 3U);
  // Discard is LIFO: last hand element pushed first.
  EXPECT_EQ(c.player().deck.discard_pile()[0].id, h2);
  EXPECT_EQ(c.player().deck.discard_pile()[1].id, h1);
  EXPECT_EQ(c.player().deck.discard_pile()[2].id, h0);
}

// -------------------------------------------------------------------------
// 10.7  start_player_turn
// -------------------------------------------------------------------------

// T-CMB-175 — BP, BV — Round 1: block NOT reset (D3 FALSE), draws 7, energy 3,
// both enemies latch performed_first_move (D2 TRUE for both).
TEST(CombatStartPlayerTurn, T_CMB_175_Round1KeepsBlockDrawsSeven) {
  Combat c{kCombatTestSeed};
  Rng enemy_rng{kCombatTestSeed};
  c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));
  c.add_enemy(sts2::enemies::make_damp_cultist(enemy_rng));
  c.set_pick_discard_callback(
      [](const Combat&) { return sts2::game::HandIndex{0}; });
  // Plant 4 block before start; round 1 must not reset it.
  c.gain_player_block(4);

  c.start(sts2::cards::make_silent_starter_deck());

  EXPECT_EQ(c.round(), 1);
  EXPECT_EQ(c.player().vitals.block, 4);
  EXPECT_EQ(c.player().hand.size(), 7U);
  EXPECT_EQ(c.player().energy, 3);
  ASSERT_EQ(c.enemies().size(), 2U);
  EXPECT_TRUE(c.enemies()[0].performed_first_move);
  EXPECT_TRUE(c.enemies()[1].performed_first_move);
}

// T-CMB-180 — BP — Round 2: block reset, draws 5, both enemies rolled to
// DarkStrike. After end_turn(), R1 enemy_phase has run (Incantation→Ritual)
// and start_player_turn for R2 has rolled DarkStrike.
TEST(CombatStartPlayerTurn, T_CMB_180_Round2ResetsBlockDrawsFive) {
  Combat c = MakeStarterCombat(kCombatTestSeed);

  c.end_turn();

  EXPECT_EQ(c.round(), 2);
  EXPECT_EQ(c.player().vitals.block, 0);
  EXPECT_EQ(c.player().hand.size(), 5U);
  ASSERT_EQ(c.enemies().size(), 2U);
  EXPECT_EQ(c.enemies()[0].current_move, MoveId::kDarkStrike);
  EXPECT_EQ(c.enemies()[1].current_move, MoveId::kDarkStrike);
}

// T-CMB-185 — EG — Dead enemy: roll_next_move skipped (D2 FALSE), so
// current_move and performed_first_move retain their values from R1 start.
TEST(CombatStartPlayerTurn, T_CMB_185_DeadEnemyMoveNotRolled) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  // After start: both enemies have performed_first_move=true,
  // current_move=Incantation.
  ASSERT_TRUE(c.enemies()[0].performed_first_move);
  ASSERT_EQ(c.enemies()[0].current_move, MoveId::kIncantation);

  KillEnemy(c, 0);
  ASSERT_EQ(c.enemies()[0].vitals.hp, 0);

  c.end_turn();  // advances to R2 start_player_turn

  EXPECT_EQ(c.round(), 2);
  // Enemy 0 was dead at R2 start_player_turn — its move stayed Incantation
  // and performed_first_move stayed true.
  EXPECT_EQ(c.enemies()[0].current_move, MoveId::kIncantation);
  EXPECT_TRUE(c.enemies()[0].performed_first_move);
  // Enemy 1 was alive — rolled to DarkStrike.
  EXPECT_EQ(c.enemies()[1].current_move, MoveId::kDarkStrike);
}

// -------------------------------------------------------------------------
// 10.8  enemy_phase
// -------------------------------------------------------------------------

// T-CMB-190 — BP, BV — No enemies: three loops fall through, no-op, no crash.
TEST(CombatEnemyPhase, T_CMB_190_NoEnemiesNoOp) {
  Combat c{kCombatTestSeed};
  ASSERT_TRUE(c.enemies().empty());
  const int hp_before = c.player().vitals.hp;

  c.enemy_phase();

  EXPECT_TRUE(c.enemies().empty());
  EXPECT_EQ(c.player().vitals.hp, hp_before);
  EXPECT_FALSE(c.combat_over());
}

// T-CMB-195 — BP — R1 enemy_phase happy path: both alive, both Incantation.
// After phase, each enemy has Ritual{ritual_amount, just_applied=false}
// because the per-enemy tick at the bottom of the act loop clears just_applied.
// Player hp unchanged (Incantation deals no damage).
TEST(CombatEnemyPhase, T_CMB_195_TwoAliveIncantationFullSweep) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  const int hp_before = c.player().vitals.hp;

  c.enemy_phase();

  EXPECT_EQ(c.player().vitals.hp, hp_before);
  ASSERT_EQ(c.enemies().size(), 2U);
  ExpectPowersEq(c.enemies()[0].vitals.powers,
                 {MakePower(PowerKind::kRitual, 2, /*just_applied=*/false)});
  ExpectPowersEq(c.enemies()[1].vitals.powers,
                 {MakePower(PowerKind::kRitual, 5, /*just_applied=*/false)});
  EXPECT_EQ(c.enemies()[0].vitals.block, 0);
  EXPECT_EQ(c.enemies()[1].vitals.block, 0);
}

// T-CMB-200 — DF — R2 enemy_phase: both DarkStrike for damage, then per-enemy
// tick turns the (just_applied=false) Ritual into a Strength gain equal to
// ritual_amount. Player damage = 9 + 1 (no Strength yet at attack time).
TEST(CombatEnemyPhase, T_CMB_200_Round2DarkStrikeRitualToStrength) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  c.end_turn();  // advance through R1 → R2 start_player_turn (R2 ready)
  ASSERT_EQ(c.round(), 2);

  c.end_turn();  // run R2 end_player_turn → enemy_phase → (round inc + R3
                 // start)

  // After the R2 enemy_phase ran: damage applied = 9 + 1 = 10 → hp 70-10=60.
  EXPECT_EQ(c.player().vitals.hp, 60);
  ASSERT_EQ(c.enemies().size(), 2U);
  ExpectPowersEq(c.enemies()[0].vitals.powers,
                 {MakePower(PowerKind::kRitual, 2, /*just_applied=*/false),
                  MakePower(PowerKind::kStrength, 2)});
  ExpectPowersEq(c.enemies()[1].vitals.powers,
                 {MakePower(PowerKind::kRitual, 5, /*just_applied=*/false),
                  MakePower(PowerKind::kStrength, 5)});
}

// T-CMB-205 — EG — One dead enemy in middle (here: enemy 0): D2 FALSE skips
// block reset, D4 TRUE skips act, D7 FALSE skips tick. Enemy 1 phases normally.
TEST(CombatEnemyPhase, T_CMB_205_DeadEnemyAllLoopsSkip) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  KillEnemy(c, 0);
  ASSERT_EQ(c.enemies()[0].vitals.hp, 0);
  ASSERT_TRUE(c.enemies()[0].vitals.powers.empty());

  c.enemy_phase();

  // Enemy 0: dead, all three loops skipped → still no powers.
  EXPECT_TRUE(c.enemies()[0].vitals.powers.empty());
  // Enemy 1: act applied Ritual (just_applied=true), tick cleared it.
  ExpectPowersEq(c.enemies()[1].vitals.powers,
                 {MakePower(PowerKind::kRitual, 5, /*just_applied=*/false)});
}

// T-CMB-210 — EG — Player dies mid-phase short-circuits the act loop (D5 TRUE).
// Setup: starter + advance to R2 start (enemies on DarkStrike), reduce player
// to hp=1, then end_turn() drives into R2 enemy_phase. Enemy 0 (DarkStrike 9)
// kills the player; enemy 1 does NOT act this turn; tick loop is NOT executed.
TEST(CombatEnemyPhase, T_CMB_210_PlayerDeathShortCircuitsActLoop) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  c.end_turn();  // R1 → R2 start; both enemies now have current_move=DarkStrike
  ASSERT_EQ(c.round(), 2);

  // Reduce player to hp 1 via a temp source enemy with no powers.
  Enemy temp{};
  temp.vitals = Vitals{1, 1, 0, {}};
  c.enemy_attack_player(temp, 69);
  ASSERT_EQ(c.player().vitals.hp, 1);
  ASSERT_FALSE(c.combat_over());

  c.end_turn();

  EXPECT_TRUE(c.combat_over());
  EXPECT_TRUE(c.is_player_dead());
  // Tick loop NOT executed → no Strength gain on either enemy this round.
  // Enemies still hold their R1-tick Ritual{n,false}.
  ASSERT_EQ(c.enemies().size(), 2U);
  ExpectPowersEq(c.enemies()[0].vitals.powers,
                 {MakePower(PowerKind::kRitual, 2, /*just_applied=*/false)});
  ExpectPowersEq(c.enemies()[1].vitals.powers,
                 {MakePower(PowerKind::kRitual, 5, /*just_applied=*/false)});
}

// -------------------------------------------------------------------------
// 10.9  end_turn
// -------------------------------------------------------------------------

// T-CMB-215 — BP — Combat already over → early return (D1 TRUE).
// Set up combat_over=true via lethal damage to player, then verify end_turn
// makes no state changes.
TEST(CombatEndTurn, T_CMB_215_CombatOverEarlyReturn) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  Enemy temp{};
  temp.vitals = Vitals{1, 1, 0, {}};
  c.enemy_attack_player(temp, 999);
  ASSERT_TRUE(c.combat_over());
  const int round_before = c.round();
  const std::size_t hand_before = c.player().hand.size();
  const int hp_before = c.player().vitals.hp;
  const int energy_before = c.player().energy;

  c.end_turn();

  EXPECT_EQ(c.round(), round_before);
  EXPECT_EQ(c.player().hand.size(), hand_before);
  EXPECT_EQ(c.player().vitals.hp, hp_before);
  EXPECT_EQ(c.player().energy, energy_before);
}

// T-CMB-220 — BP — Player dies during enemy_phase → early return after
// enemy_phase (D2 TRUE). round NOT incremented; start_player_turn NOT called
// (next R3 hand not redrawn — hand stays empty post end_player_turn).
TEST(CombatEndTurn, T_CMB_220_PlayerDeathDuringEnemyPhaseEarlyReturn) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  c.end_turn();  // → R2 start; enemies on DarkStrike
  ASSERT_EQ(c.round(), 2);
  Enemy temp{};
  temp.vitals = Vitals{1, 1, 0, {}};
  c.enemy_attack_player(temp, 69);
  ASSERT_EQ(c.player().vitals.hp, 1);

  c.end_turn();

  EXPECT_TRUE(c.combat_over());
  // round was 2 entering end_turn; not incremented because of early return.
  EXPECT_EQ(c.round(), 2);
  // R3 start_player_turn was NOT called → hand stays empty (post
  // end_player_turn).
  EXPECT_TRUE(c.player().hand.empty());
}

// T-CMB-225 — BP — Normal round transition: full path through
// end_player_turn → enemy_phase → round_++ → start_player_turn →
// check_win_or_lose.
TEST(CombatEndTurn, T_CMB_225_NormalRoundTransition) {
  Combat c = MakeStarterCombat(kCombatTestSeed);

  c.end_turn();

  EXPECT_EQ(c.round(), 2);
  EXPECT_EQ(c.player().hand.size(), 5U);
  EXPECT_EQ(c.player().energy, 3);
  EXPECT_EQ(c.player().vitals.block, 0);
  EXPECT_FALSE(c.combat_over());
}

// -------------------------------------------------------------------------
// 10.10  is_player_dead, all_enemies_dead, check_win_or_lose
// -------------------------------------------------------------------------

// T-CMB-230 — BP, BV — TRUE at hp=0 and hp<0.
TEST(CombatIsPlayerDead, T_CMB_230_TrueAtZeroAndBelow) {
  Combat c{kCombatTestSeed};
  Enemy temp{};
  temp.vitals = Vitals{1, 1, 0, {}};
  c.enemy_attack_player(temp, 70);
  ASSERT_EQ(c.player().vitals.hp, 0);
  EXPECT_TRUE(c.is_player_dead());

  // Dealing more damage at hp=0 leaves hp=0 (apply_to_defender clamps); the
  // <=0 invariant still holds either way.
  c.enemy_attack_player(temp, 99);
  EXPECT_LE(c.player().vitals.hp, 0);
  EXPECT_TRUE(c.is_player_dead());
}

// T-CMB-235 — BP — FALSE at hp=1 and hp=70.
TEST(CombatIsPlayerDead, T_CMB_235_FalseAboveZero) {
  Combat c{kCombatTestSeed};
  EXPECT_EQ(c.player().vitals.hp, 70);
  EXPECT_FALSE(c.is_player_dead());

  Enemy temp{};
  temp.vitals = Vitals{1, 1, 0, {}};
  c.enemy_attack_player(temp, 69);
  ASSERT_EQ(c.player().vitals.hp, 1);
  EXPECT_FALSE(c.is_player_dead());
}

// T-CMB-240 — BP, BV — Empty enemies vector → FALSE (`!empty()` returns false).
TEST(CombatAllEnemiesDead, T_CMB_240_EmptyVectorFalse) {
  Combat c{kCombatTestSeed};
  ASSERT_TRUE(c.enemies().empty());

  EXPECT_FALSE(c.all_enemies_dead());
}

// T-CMB-245 — BP — All dead → TRUE.
TEST(CombatAllEnemiesDead, T_CMB_245_AllDeadTrue) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  KillEnemy(c, 0);
  KillEnemy(c, 1);

  EXPECT_TRUE(c.all_enemies_dead());
}

// T-CMB-250 — BP — One alive in middle → FALSE.
TEST(CombatAllEnemiesDead, T_CMB_250_AliveInMiddleFalse) {
  Combat c{kCombatTestSeed};
  Enemy a{};
  a.vitals = Vitals{1, 1, 0, {}};
  Enemy b{};
  b.vitals = Vitals{40, 40, 0, {}};
  Enemy d{};
  d.vitals = Vitals{1, 1, 0, {}};
  c.add_enemy(std::move(a));
  c.add_enemy(std::move(b));
  c.add_enemy(std::move(d));
  KillEnemy(c, 0);
  KillEnemy(c, 2);
  ASSERT_EQ(c.enemies()[0].vitals.hp, 0);
  ASSERT_GT(c.enemies()[1].vitals.hp, 0);
  ASSERT_EQ(c.enemies()[2].vitals.hp, 0);

  EXPECT_FALSE(c.all_enemies_dead());
}

// T-CMB-255 — BP — Player dead → combat_over true (left of `||`
// short-circuits). Use a no-enemy combat so all_enemies_dead would be FALSE
// (empty vector); only is_player_dead can flip combat_over here.
TEST(CombatCheckWinOrLose, T_CMB_255_PlayerDeadShortCircuit) {
  Combat c{kCombatTestSeed};
  Enemy temp{};
  temp.vitals = Vitals{1, 1, 0, {}};
  c.enemy_attack_player(temp, 70);
  ASSERT_TRUE(c.is_player_dead());
  ASSERT_FALSE(c.all_enemies_dead());

  c.check_win_or_lose();

  EXPECT_TRUE(c.combat_over());
}

// T-CMB-260 — BP — Player alive, all enemies dead → combat_over true
// (left FALSE, right TRUE).
TEST(CombatCheckWinOrLose, T_CMB_260_AllEnemiesDeadFlipsOver) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  KillEnemy(c, 0);
  KillEnemy(c, 1);
  ASSERT_FALSE(c.is_player_dead());
  ASSERT_TRUE(c.all_enemies_dead());

  c.check_win_or_lose();

  EXPECT_TRUE(c.combat_over());
}

// T-CMB-265 — BP — Both alive → combat_over FALSE (no change).
TEST(CombatCheckWinOrLose, T_CMB_265_BothAliveNoChange) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  ASSERT_FALSE(c.is_player_dead());
  ASSERT_FALSE(c.all_enemies_dead());

  c.check_win_or_lose();

  EXPECT_FALSE(c.combat_over());
}

// T-CMB-270 — EG — Both sides simultaneously dead → combat_over true on
// the first true predicate. Achievable via public API by killing all enemies
// first, then dealing lethal damage to the player; combat_over latches true
// from the first kill and remains true after the second.
TEST(CombatCheckWinOrLose, T_CMB_270_BothSidesDead) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  KillEnemy(c, 0);
  KillEnemy(c, 1);
  ASSERT_TRUE(c.combat_over());

  Enemy temp{};
  temp.vitals = Vitals{1, 1, 0, {}};
  c.enemy_attack_player(temp, 999);
  ASSERT_TRUE(c.is_player_dead());
  ASSERT_TRUE(c.all_enemies_dead());

  c.check_win_or_lose();

  EXPECT_TRUE(c.combat_over());
}

// -------------------------------------------------------------------------
// 10.11  discard_chosen_from_hand
// -------------------------------------------------------------------------

// T-CMB-275 — BP, BV — Empty hand: D1 TRUE returns before consulting callback.
TEST(CombatDiscardChosen, T_CMB_275_EmptyHandNoOpNoCallback) {
  Combat c{kCombatTestSeed};
  bool called = false;
  c.set_pick_discard_callback([&called](const Combat&) {
    called = true;
    return sts2::game::HandIndex{0};
  });
  ASSERT_TRUE(c.player().hand.empty());

  c.discard_chosen_from_hand();

  EXPECT_FALSE(called);
  EXPECT_TRUE(c.player().hand.empty());
  EXPECT_EQ(c.player().deck.discard_size(), 0U);
}

// T-CMB-280 — BP — Callback returns -1 → no-op (D2 TRUE via left of `||`).
TEST(CombatDiscardChosen, T_CMB_280_CallbackNegativeNoOp) {
  Combat c{kCombatTestSeed};
  c.set_pick_discard_callback(
      [](const Combat&) { return sts2::game::HandIndex{-1}; });
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 1U);

  c.discard_chosen_from_hand();

  EXPECT_EQ(c.player().hand.size(), 1U);
  EXPECT_EQ(c.player().deck.discard_size(), 0U);
}

// T-CMB-285 — BP — Callback returns out-of-range index → no-op
// (D2 TRUE via right of `||`).
TEST(CombatDiscardChosen, T_CMB_285_CallbackOutOfRangeNoOp) {
  Combat c{kCombatTestSeed};
  c.set_pick_discard_callback(
      [](const Combat&) { return sts2::game::HandIndex{5}; });
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 2U);

  c.discard_chosen_from_hand();

  EXPECT_EQ(c.player().hand.size(), 2U);
  EXPECT_EQ(c.player().deck.discard_size(), 0U);
}

// T-CMB-290 — BP, DF — Callback returns valid index → moves hand[idx] to
// discard.
TEST(CombatDiscardChosen, T_CMB_290_CallbackValidIndexMoves) {
  Combat c{kCombatTestSeed};
  c.set_pick_discard_callback(
      [](const Combat&) { return sts2::game::HandIndex{1}; });
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kNeutralize));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 3U);
  const CardId pre0 = c.player().hand.cards()[0].id;
  const CardId pre1 = c.player().hand.cards()[1].id;
  const CardId pre2 = c.player().hand.cards()[2].id;

  c.discard_chosen_from_hand();

  ASSERT_EQ(c.player().hand.size(), 2U);
  EXPECT_EQ(c.player().hand.cards()[0].id, pre0);
  EXPECT_EQ(c.player().hand.cards()[1].id, pre2);
  ASSERT_EQ(c.player().deck.discard_size(), 1U);
  EXPECT_EQ(c.player().deck.discard_pile()[0].id, pre1);
}

// -------------------------------------------------------------------------
// 10.12  Query helpers: alive_enemy_indices, find_card_in_hand
//        is_alive (free helper in enemy.h)
// -------------------------------------------------------------------------

using TargetType = sts2::game::TargetType;

// is_alive: negative-index guard — slot not in range → not alive.
TEST(CombatIsEnemyAlive, NegativeIndexFalse) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed, /*hp=*/40);
  const sts2::game::EnemySlot slot{-1};
  EXPECT_FALSE(slot.in_range(c.enemies()));
}

// is_alive: in-range alive enemy → true.
TEST(CombatIsEnemyAlive, AliveTrue) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed, /*hp=*/40);
  const sts2::game::EnemySlot slot{0};
  ASSERT_TRUE(slot.in_range(c.enemies()));
  EXPECT_TRUE(sts2::game::is_alive(slot.at(c.enemies())));
}

// is_alive: in-range dead enemy → false.
TEST(CombatIsEnemyAlive, DeadFalse) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed, /*hp=*/40);
  KillEnemy(c, 0);
  ASSERT_LE(c.enemies()[0].vitals.hp, 0);
  const sts2::game::EnemySlot slot{0};
  EXPECT_FALSE(sts2::game::is_alive(slot.at(c.enemies())));
}

// is_alive: out-of-range slot → not in range (size guard).
TEST(CombatIsEnemyAlive, OutOfRangeFalse) {
  Combat c = MakeCombatWithEnemy(kCombatTestSeed, /*hp=*/40);
  EXPECT_FALSE(sts2::game::EnemySlot{1}.in_range(c.enemies()));
  EXPECT_FALSE(sts2::game::EnemySlot{99}.in_range(c.enemies()));
}

// alive_enemy_indices: empty enemies vector → empty.
TEST(CombatAliveEnemyIndices, EmptyVectorEmpty) {
  Combat c{kCombatTestSeed};
  ASSERT_TRUE(c.enemies().empty());
  EXPECT_TRUE(c.alive_enemy_indices().empty());
}

// alive_enemy_indices: zero alive → empty.
TEST(CombatAliveEnemyIndices, ZeroAliveEmpty) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  KillEnemy(c, 0);
  KillEnemy(c, 1);
  EXPECT_TRUE(c.alive_enemy_indices().empty());
}

// alive_enemy_indices: one alive in middle → returns its slot index.
TEST(CombatAliveEnemyIndices, OneAliveReturnsSlot) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  KillEnemy(c, 0);
  ASSERT_GT(c.enemies()[1].vitals.hp, 0);
  EXPECT_EQ(c.alive_enemy_indices(), (std::vector<sts2::game::EnemySlot>{sts2::game::EnemySlot{1}}));
}

// alive_enemy_indices: both alive → returns both slot indices in slot order.
TEST(CombatAliveEnemyIndices, BothAliveReturnsBothInOrder) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  EXPECT_EQ(c.alive_enemy_indices(), (std::vector<sts2::game::EnemySlot>{sts2::game::EnemySlot{0}, sts2::game::EnemySlot{1}}));
}

// hand size: empty hand → 0.
TEST(CombatHandSize, EmptyZero) {
  Combat c{kCombatTestSeed};
  ASSERT_TRUE(c.player().hand.empty());
  EXPECT_EQ(c.player().hand.size(), 0U);
}

// hand size: populated starter combat has 7 cards.
TEST(CombatHandSize, PopulatedSeven) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  EXPECT_EQ(c.player().hand.size(), 7U);
}

// find_card_in_hand: present → returns index of first match.
TEST(CombatFindCardInHand, PresentReturnsIndex) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  const sts2::game::HandIndex strike_idx = c.find_card_in_hand(CardId::kStrike);
  ASSERT_TRUE(strike_idx.valid());
  EXPECT_EQ(c.player().hand.at(strike_idx).id, CardId::kStrike);
}

// find_card_in_hand: absent → returns -1.
TEST(CombatFindCardInHand, AbsentReturnsMinusOne) {
  Combat c{kCombatTestSeed};
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 1U);
  EXPECT_EQ(c.find_card_in_hand(CardId::kNeutralize), sts2::game::HandIndex::none());
}

// find_card_in_hand: multiple matches → returns FIRST hand index.
TEST(CombatFindCardInHand, MultipleMatchesReturnsFirst) {
  Combat c{kCombatTestSeed};
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 3U);

  const sts2::game::HandIndex idx = c.find_card_in_hand(CardId::kStrike);
  EXPECT_EQ(idx, sts2::game::HandIndex{0});
}

// -------------------------------------------------------------------------
// 10.13  Player/enemy/deck vitals (via player() / enemies() direct access)
// -------------------------------------------------------------------------

// player vitals: defaults — fresh combat.
TEST(CombatPlayerVitalsAccessors, PlayerHpDefaults) {
  Combat c{kCombatTestSeed};
  EXPECT_EQ(c.player().vitals.hp, 70);
  EXPECT_EQ(c.player().vitals.max_hp, 70);
}

// player vitals: reflects damage applied via enemy_attack_player.
TEST(CombatPlayerVitalsAccessors, PlayerHpAfterDamage) {
  Combat c{kCombatTestSeed};
  Enemy attacker{};
  attacker.vitals = Vitals{1, 1, 0, {}};
  c.enemy_attack_player(attacker, 5);
  EXPECT_EQ(c.player().vitals.hp, 65);
  EXPECT_EQ(c.player().vitals.max_hp, 70);
}

// player block: starts at 0; gain_player_block accumulates.
TEST(CombatPlayerVitalsAccessors, PlayerBlockAccumulates) {
  Combat c{kCombatTestSeed};
  EXPECT_EQ(c.player().vitals.block, 0);
  c.gain_player_block(7);
  EXPECT_EQ(c.player().vitals.block, 7);
}

// player energy: 0 pre-start, kPlayerMaxEnergy post-start.
TEST(CombatPlayerVitalsAccessors, PlayerEnergyAccessors) {
  Combat c{kCombatTestSeed};
  EXPECT_EQ(c.player().energy, 0);

  c.start(MakeStrikeDefendDeck7());
  EXPECT_EQ(c.player().energy, Combat::kPlayerMaxEnergy);
}

// player powers: empty by default.
TEST(CombatPlayerVitalsAccessors, PlayerPowersEmptyByDefault) {
  Combat c{kCombatTestSeed};
  EXPECT_TRUE(c.player().vitals.powers.empty());
}

// hand.at: returns the same Card as cards() at every valid index.
TEST(CombatPlayerHandAccessors, HandAtMatchesCardsAccess) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  ASSERT_EQ(c.player().hand.size(), 7U);
  for (std::size_t i = 0; i < c.player().hand.size(); ++i) {
    const auto idx = sts2::game::HandIndex{static_cast<int>(i)};
    EXPECT_EQ(c.player().hand.at(idx).id, c.player().hand.cards()[i].id);
    EXPECT_EQ(&c.player().hand.at(idx), &c.player().hand.cards()[i]);
  }
}

// draw/discard pile sizes: starter combat — 7 hand + 5 draw + 0 discard at R1.
TEST(CombatPileSizeAccessors, StarterCombatPilesAtR1Start) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  EXPECT_EQ(c.player().deck.draw_size(), 5U);
  EXPECT_EQ(c.player().deck.discard_size(), 0U);
}

// discard pile grows after playing a card.
TEST(CombatPileSizeAccessors, DiscardGrowsAfterPlay) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  const std::size_t pre_discard = c.player().deck.discard_size();
  int idx = FindHandIndex(c, CardId::kDefend);
  ASSERT_NE(idx, -1);
  ASSERT_TRUE(c.play_card(sts2::game::HandIndex{idx}, sts2::game::EnemySlot::none()));
  EXPECT_EQ(c.player().deck.discard_size(), pre_discard + 1);
}

// total deck size: draw + hand + discard.
TEST(CombatTotalDeckSize, SumsAllPiles) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  // 12-card deck; R1: 7 hand + 5 draw + 0 discard.
  EXPECT_EQ(static_cast<int>(c.player().deck.total_size() + c.player().hand.size()), 12);
}

// total deck size: zero when no cards anywhere.
TEST(CombatTotalDeckSize, ZeroWhenEmpty) {
  Combat c{kCombatTestSeed};
  EXPECT_EQ(static_cast<int>(c.player().deck.total_size() + c.player().hand.size()), 0);
}

// display_index_of (render helper): negative slot → -1.
TEST(CombatDisplayIndexOf, NegativeSlotMinusOne) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  EXPECT_EQ(sts2::render::detail::display_index_of(c, sts2::game::EnemySlot{-1}), -1);
}

// display_index_of: out-of-range slot → -1.
TEST(CombatDisplayIndexOf, OutOfRangeMinusOne) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  EXPECT_EQ(sts2::render::detail::display_index_of(c, sts2::game::EnemySlot{2}), -1);
  EXPECT_EQ(sts2::render::detail::display_index_of(c, sts2::game::EnemySlot{99}), -1);
}

// display_index_of: dead enemy slot → -1.
TEST(CombatDisplayIndexOf, DeadSlotMinusOne) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  KillEnemy(c, 0);
  EXPECT_EQ(sts2::render::detail::display_index_of(c, sts2::game::EnemySlot{0}), -1);
}

// display_index_of: both alive — slot indices map identity-wise.
TEST(CombatDisplayIndexOf, BothAliveIdentity) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  EXPECT_EQ(sts2::render::detail::display_index_of(c, sts2::game::EnemySlot{0}), 0);
  EXPECT_EQ(sts2::render::detail::display_index_of(c, sts2::game::EnemySlot{1}), 1);
}

// display_index_of: enemy 0 dead — slot 1 collapses to display 0.
TEST(CombatDisplayIndexOf, FrontDeadCollapsesIndex) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  KillEnemy(c, 0);
  EXPECT_EQ(sts2::render::detail::display_index_of(c, sts2::game::EnemySlot{1}), 0);
}

}  // namespace
