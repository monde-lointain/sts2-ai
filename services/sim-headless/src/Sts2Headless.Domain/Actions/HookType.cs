// Full HookType enum ported from upstream godot/sts2/src/Core/Hooks/Hook.cs.
//
// Mapping rule:
//   Each enum value corresponds 1:1 to an AbstractModel callback method
//   invoked by a static Hook.* coordinator. Names are PascalCase verbatim
//   from upstream — same casing, same spelling. Variants with -Early /
//   -Late / -VeryEarly suffixes appear because upstream fires them as
//   distinct iteration passes; preserve them so Q1 ordering matches the
//   probe (Q1-ADR-006, Q1-ADR-007).
//
// Stability:
//   IDs are stable. New values append; never reorder (Q1-ADR-005 — hook
//   protocol schema is co-versioned with state schema, and replays
//   reference hook firings).
//
// Phase scope:
//   The bulk of these are combat-scope and fire in Phase 1. Run-level
//   entries (Map/Merchant/RestSite/Reward/Room/Potion/Event) remain in the
//   enum so Phase 2 doesn't trigger renames, but they are not fired by
//   Phase-1 tests.

namespace Sts2Headless.Domain.Actions;

/// <summary>
/// Hook identity. One value per upstream AbstractModel hook callback. The set
/// is closed for Phase 1; appending later (when new mechanics ship) requires
/// a state-schema version bump (Q1-ADR-005).
/// </summary>
public enum HookType
{
    None = 0,

    // === Act / Run lifecycle ===
    AfterActEntered,
    AfterMapGenerated,
    ModifyGeneratedMap,
    ModifyGeneratedMapLate,
    BeforeRoomEntered,
    AfterRoomEntered,
    ShouldProceedToNextMapPoint,
    ModifyOddsIncreaseForUnrolledRoomType,
    ModifyUnknownMapPointRoomTypes,
    ShouldAllowFreeTravel,
    ModifyNextEvent,
    ShouldAllowAncient,

    // === Combat lifecycle ===
    BeforeCombatStart,
    BeforeCombatStartLate,
    AfterCombatEnd,
    AfterCombatVictoryEarly,
    AfterCombatVictory,
    ShouldStopCombatFromEnding,
    AfterCreatureAddedToCombat,
    AfterCardEnteredCombat,
    AfterCardGeneratedForCombat,

    // === Turn lifecycle ===
    BeforeSideTurnStart,
    AfterSideTurnStart,
    AfterSideTurnStartLate,
    AfterPlayerTurnStartEarly,
    AfterPlayerTurnStart,
    AfterPlayerTurnStartLate,
    BeforePlayPhaseStart,
    BeforePlayPhaseStartLate,
    BeforeTurnEndVeryEarly,
    BeforeTurnEndEarly,
    BeforeTurnEnd,
    AfterTurnEnd,
    AfterTurnEndLate,
    ShouldTakeExtraTurn,
    AfterTakingExtraTurn,

    // === Card play ===
    BeforeCardPlayed,
    AfterCardPlayed,
    AfterCardPlayedLate,
    BeforeCardAutoPlayed,
    ShouldPlay,
    ModifyCardPlayCount,
    AfterModifyingCardPlayCount,
    ModifyCardPlayResultPileTypeAndPosition,
    ModifyEnergyCostInCombat,
    TryModifyEnergyCostInCombat,
    ModifyStarCost,
    TryModifyStarCost,
    ModifyXValue,

    // === Card piles / draw / discard / exhaust / shuffle ===
    AfterCardChangedPiles,
    AfterCardChangedPilesLate,
    AfterCardDiscarded,
    AfterCardDrawnEarly,
    AfterCardDrawn,
    AfterCardExhausted,
    AfterCardRetained,
    BeforeHandDraw,
    BeforeHandDrawLate,
    AfterHandEmptied,
    ShouldDraw,
    AfterPreventingDraw,
    ModifyHandDraw,
    ModifyHandDrawLate,
    AfterModifyingHandDraw,
    AfterShuffle,
    ModifyShuffleOrder,
    BeforeCardRemoved,
    ShouldAddToDeck,
    TryModifyCardBeingAddedToDeck,
    TryModifyCardBeingAddedToDeckLate,
    ShouldEtherealTrigger,

    // === Damage / attack ===
    BeforeAttack,
    AfterAttack,
    ModifyAttackHitCount,
    ModifyDamageAdditive,
    ModifyDamageMultiplicative,
    ModifyDamageCap,
    AfterModifyingDamageAmount,
    BeforeDamageReceived,
    AfterDamageReceived,
    AfterDamageReceivedLate,
    AfterDamageGiven,
    ModifyHpLostBeforeOsty,
    ModifyHpLostBeforeOstyLate,
    AfterModifyingHpLostBeforeOsty,
    ModifyHpLostAfterOsty,
    ModifyHpLostAfterOstyLate,
    AfterModifyingHpLostAfterOsty,
    ModifyUnblockedDamageTarget,
    ShouldAllowTargeting,
    ShouldAllowHitting,
    AfterCurrentHpChanged,

    // === Block ===
    BeforeBlockGained,
    AfterBlockGained,
    AfterBlockBroken,
    AfterBlockCleared,
    ModifyBlockAdditive,
    ModifyBlockMultiplicative,
    AfterModifyingBlockAmount,
    ShouldClearBlock,
    AfterPreventingBlockClear,

    // === Death ===
    BeforeDeath,
    AfterDeath,
    AfterDiedToDoom,
    ShouldDie,
    ShouldDieLate,
    AfterPreventingDeath,
    ShouldCreatureBeRemovedFromCombatAfterDeath,
    ShouldPowerBeRemovedOnDeath,

    // === Powers ===
    BeforePowerAmountChanged,
    AfterPowerAmountChanged,
    ModifyPowerAmountGiven,
    AfterModifyingPowerAmountGiven,
    TryModifyPowerAmountReceived,
    AfterModifyingPowerAmountReceived,

    // === Orbs ===
    AfterOrbChanneled,
    AfterOrbEvoked,
    ModifyOrbValue,
    ModifyOrbPassiveTriggerCounts,
    AfterModifyingOrbPassiveTriggerCount,

    // === Energy ===
    AfterEnergyReset,
    AfterEnergyResetLate,
    AfterEnergySpent,
    ShouldPlayerResetEnergy,
    ModifyEnergyGain,
    AfterModifyingEnergyGain,
    ModifyMaxEnergy,

    // === Afflictions / Forge / Osty / Summon / Stars ===
    ShouldAfflict,
    AfterForge,
    AfterOstyRevived,
    AfterSummon,
    ModifySummonAmount,
    AfterStarsGained,
    AfterStarsSpent,
    ShouldGainStars,
    ShouldPayExcessEnergyCostWithStars,

    // === Flush ===
    BeforeFlush,
    BeforeFlushLate,
    ShouldFlush,

    // === Potions ===
    BeforePotionUsed,
    AfterPotionUsed,
    AfterPotionDiscarded,
    AfterPotionProcured,
    ShouldProcurePotion,
    ShouldForcePotionReward,

    // === Gold / Treasure ===
    AfterGoldGained,
    ShouldGainGold,
    ShouldGenerateTreasure,

    // === Rewards ===
    BeforeRewardsOffered,
    AfterRewardTaken,
    AfterModifyingRewards,
    TryModifyRewards,
    TryModifyRewardsLate,
    AfterModifyingCardRewardOptions,
    TryModifyCardRewardOptions,
    TryModifyCardRewardOptionsLate,
    TryModifyCardRewardAlternatives,
    ModifyCardRewardCreationOptions,
    ModifyCardRewardCreationOptionsLate,
    ModifyCardRewardUpgradeOdds,
    ShouldAllowSelectingMoreCardRewards,

    // === Merchant ===
    ModifyMerchantCardCreationResults,
    ModifyMerchantCardPool,
    ModifyMerchantCardRarity,
    ModifyMerchantPrice,
    ShouldAllowMerchantCardRemoval,
    ShouldRefillMerchantEntry,
    AfterItemPurchased,

    // === Rest site ===
    AfterRestSiteHeal,
    AfterRestSiteSmith,
    ModifyRestSiteHealAmount,
    ModifyExtraRestSiteHealText,
    TryModifyRestSiteOptions,
    TryModifyRestSiteHealRewards,
    ShouldDisableRemainingRestSiteOptions,
}
