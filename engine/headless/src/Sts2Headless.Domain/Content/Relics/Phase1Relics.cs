using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;

namespace Sts2Headless.Domain.Content.Relics;

/// <summary>
/// Bulk-port of Phase-1 relics from upstream <c>~/development/projects/godot/sts2/src/Core/Models/Relics/*.cs</c>.
/// Each class captures the canonical id + name + rarity per upstream's
/// <c>public override RelicRarity Rarity => RelicRarity.Foo</c> declarations.
/// Hook subscriptions (per-turn-start triggers, after-attack riders, etc.) are
/// declared as no-op overrides here; S13 fills them in alongside the determinism probe
/// once S6 has the matching HookType payloads.
/// </summary>
public sealed class Akabeko : RelicModel
{
    public const string CanonicalId = "Akabeko";
    public const int VigorAmount = 8;
    public Akabeko() : base(CanonicalId, "Akabeko", RelicRarity.Uncommon) { }

    /// <summary>
    /// Verbatim port of upstream <c>Akabeko.AfterSideTurnStart</c>: on the
    /// player's first side-turn-start, apply 8 Vigor to self. B.1-gamma-T4
    /// approximation: hook fires every AfterSideTurnStart; the turn-1 guard
    /// is captured by the hook subscriber checking
    /// <c>ctx.CombatState.TurnCounter == 1</c>. Without combat-state access
    /// from the relic hook, we record the action and let the dispatcher
    /// resolve gating; for Phase-1 BeforeCombatStart fires once so we attach
    /// there instead — matches the empirical effect (Akabeko applies Vigor on
    /// combat start, observable indistinguishably from "on first turn start").
    /// </summary>
    protected override void SubscribeHooks(HookRegistry hooks)
    {
        Subscribe(hooks, HookType.BeforeCombatStart, ctx =>
        {
            ctx.Execution.Queue.Enqueue(new ApplyPowerAction(
                PowerIds.Vigor, VigorAmount, Target: null /* self */));
        });
    }
}

public sealed class BagOfMarbles : RelicModel
{
    public const string CanonicalId = "BagOfMarbles";
    public const int VulnerableStacks = 1;
    public BagOfMarbles() : base(CanonicalId, "Bag of Marbles", RelicRarity.Common) { }

    /// <summary>
    /// Verbatim port of upstream <c>BagOfMarbles.BeforeSideTurnStart</c>: at the
    /// start of the player's first round, apply 1 Vulnerable to every living
    /// enemy. Phase-1 surface: BeforeCombatStart hook (only fires once per
    /// combat, so the round-1 guard is implicit). Uses the
    /// <see cref="ApplyPowerToAllEnemiesAction"/> fan-out added in Stream-B-T2.
    /// </summary>
    protected override void SubscribeHooks(HookRegistry hooks)
    {
        Subscribe(hooks, HookType.BeforeCombatStart, ctx =>
        {
            ctx.Execution.Queue.Enqueue(new ApplyPowerToAllEnemiesAction(
                PowerIds.Vulnerable, VulnerableStacks));
        });
    }
}

public sealed class BronzeScales : RelicModel
{
    public const string CanonicalId = "BronzeScales";
    public const int ThornsAmount = 3;
    public BronzeScales() : base(CanonicalId, "Bronze Scales", RelicRarity.Common) { }

    /// <summary>
    /// Verbatim port of upstream <c>BronzeScales.AfterRoomEntered(CombatRoom)</c>:
    /// on combat start, apply 3 Thorns to the player. Mapped to
    /// <see cref="HookType.BeforeCombatStart"/> per the Vajra rationale
    /// (CombatRoom is the only room shape S5/S6 currently exposes hooks for).
    /// </summary>
    protected override void SubscribeHooks(HookRegistry hooks)
    {
        Subscribe(hooks, HookType.BeforeCombatStart, ctx =>
        {
            ctx.Execution.Queue.Enqueue(new ApplyPowerAction(
                PowerIds.Thorns, ThornsAmount, Target: null /* self */));
        });
    }
}

public sealed class CentennialPuzzle : RelicModel
{
    public const string CanonicalId = "CentennialPuzzle";
    public const int CardsDrawn = 3;
    public CentennialPuzzle() : base(CanonicalId, "Centennial Puzzle", RelicRarity.Common) { }
}

public sealed class HappyFlower : RelicModel
{
    public const string CanonicalId = "HappyFlower";
    public HappyFlower() : base(CanonicalId, "Happy Flower", RelicRarity.Common) { }
}

public sealed class OddlySmoothStone : RelicModel
{
    public const string CanonicalId = "OddlySmoothStone";
    public const int DexterityAmount = 1;
    public OddlySmoothStone() : base(CanonicalId, "Oddly Smooth Stone", RelicRarity.Common) { }

    /// <summary>
    /// Verbatim port of upstream <c>OddlySmoothStone.AfterRoomEntered</c>:
    /// on combat-room entry, apply 1 Dexterity to self. B.1-gamma-T4.
    /// </summary>
    protected override void SubscribeHooks(HookRegistry hooks)
    {
        Subscribe(hooks, HookType.BeforeCombatStart, ctx =>
        {
            ctx.Execution.Queue.Enqueue(new ApplyPowerAction(
                PowerIds.Dexterity, DexterityAmount, Target: null));
        });
    }
}

public sealed class RedSkull : RelicModel
{
    public const string CanonicalId = "RedSkull";
    public RedSkull() : base(CanonicalId, "Red Skull", RelicRarity.Common) { }
}

public sealed class Whetstone : RelicModel
{
    public const string CanonicalId = "Whetstone";
    public const int CardsUpgraded = 2;
    public Whetstone() : base(CanonicalId, "Whetstone", RelicRarity.Common) { }
}

public sealed class DataDisk : RelicModel
{
    public const string CanonicalId = "DataDisk";
    public const int FocusAmount = 1;
    public DataDisk() : base(CanonicalId, "Data Disk", RelicRarity.Common) { }

    /// <summary>
    /// Verbatim port of upstream <c>DataDisk.AfterRoomEntered</c>: apply
    /// 1 Focus on combat start.
    /// </summary>
    protected override void SubscribeHooks(HookRegistry hooks)
    {
        Subscribe(hooks, HookType.BeforeCombatStart, ctx =>
        {
            ctx.Execution.Queue.Enqueue(new ApplyPowerAction(
                PowerIds.Focus, FocusAmount, Target: null));
        });
    }
}

public sealed class MealTicket : RelicModel
{
    public const string CanonicalId = "MealTicket";
    public const int HealAmount = 15;
    public MealTicket() : base(CanonicalId, "Meal Ticket", RelicRarity.Common) { }
}

public sealed class PenNib : RelicModel
{
    public const string CanonicalId = "PenNib";
    public PenNib() : base(CanonicalId, "Pen Nib", RelicRarity.Uncommon) { }
}

public sealed class Pantograph : RelicModel
{
    public const string CanonicalId = "Pantograph";
    public const int HealAmount = 25;
    public Pantograph() : base(CanonicalId, "Pantograph", RelicRarity.Uncommon) { }

    /// <summary>
    /// Verbatim port of upstream <c>Pantograph.AfterRoomEntered</c>: on boss-
    /// combat entry, heal 25. Phase-1 doesn't distinguish boss vs normal
    /// combats; we always heal on combat start. Deviation documented.
    /// </summary>
    protected override void SubscribeHooks(HookRegistry hooks)
    {
        Subscribe(hooks, HookType.BeforeCombatStart, ctx =>
        {
            ctx.Execution.Queue.Enqueue(new HealAction(HealAmount));
        });
    }
}

public sealed class MercuryHourglass : RelicModel
{
    public const string CanonicalId = "MercuryHourglass";
    public const int DamageToAll = 3;
    public MercuryHourglass() : base(CanonicalId, "Mercury Hourglass", RelicRarity.Uncommon) { }

    /// <summary>
    /// Verbatim port of upstream <c>MercuryHourglass.AfterPlayerTurnStart</c>:
    /// at every player turn start, deal 3 damage to every living enemy.
    /// </summary>
    protected override void SubscribeHooks(HookRegistry hooks)
    {
        Subscribe(hooks, HookType.AfterPlayerTurnStart, ctx =>
        {
            ctx.Execution.Queue.Enqueue(new DealDamageToAllEnemiesAction(DamageToAll));
        });
    }
}

public sealed class PaperPhrog : RelicModel
{
    public const string CanonicalId = "PaperPhrog";
    public PaperPhrog() : base(CanonicalId, "Paper Phrog", RelicRarity.Uncommon) { }
}

public sealed class GamblingChip : RelicModel
{
    public const string CanonicalId = "GamblingChip";
    public GamblingChip() : base(CanonicalId, "Gambling Chip", RelicRarity.Rare) { }
}

public sealed class Mango : RelicModel
{
    public const string CanonicalId = "Mango";
    public const int MaxHpAmount = 14;
    public Mango() : base(CanonicalId, "Mango", RelicRarity.Rare) { }
}

public sealed class Pocketwatch : RelicModel
{
    public const string CanonicalId = "Pocketwatch";
    public const int CardsDrawn = 3;
    public Pocketwatch() : base(CanonicalId, "Pocketwatch", RelicRarity.Rare) { }
}

public sealed class Shovel : RelicModel
{
    public const string CanonicalId = "Shovel";
    public Shovel() : base(CanonicalId, "Shovel", RelicRarity.Rare) { }
}

public sealed class ArtOfWar : RelicModel
{
    public const string CanonicalId = "ArtOfWar";
    public const int EnergyAmount = 1;
    public ArtOfWar() : base(CanonicalId, "Art of War", RelicRarity.Rare) { }
}

public sealed class Bellows : RelicModel
{
    public const string CanonicalId = "Bellows";
    public Bellows() : base(CanonicalId, "Bellows", RelicRarity.Rare) { }
}

public sealed class Bookmark : RelicModel
{
    public const string CanonicalId = "Bookmark";
    public Bookmark() : base(CanonicalId, "Bookmark", RelicRarity.Rare) { }
}

public sealed class MeatOnTheBone : RelicModel
{
    public const string CanonicalId = "MeatOnTheBone";
    public const int HealAmount = 12;
    public MeatOnTheBone() : base(CanonicalId, "Meat on the Bone", RelicRarity.Rare) { }
}

public sealed class BeautifulBracelet : RelicModel
{
    public const string CanonicalId = "BeautifulBracelet";
    public const int CardsDrawn = 2;
    public BeautifulBracelet() : base(CanonicalId, "Beautiful Bracelet", RelicRarity.Ancient) { }
}

public sealed class BiiigHug : RelicModel
{
    public const string CanonicalId = "BiiigHug";
    public BiiigHug() : base(CanonicalId, "Biiig Hug", RelicRarity.Ancient) { }
}

public sealed class CallingBell : RelicModel
{
    public const string CanonicalId = "CallingBell";
    public CallingBell() : base(CanonicalId, "Calling Bell", RelicRarity.Ancient) { }
}

public sealed class Ectoplasm : RelicModel
{
    public const string CanonicalId = "Ectoplasm";
    public const int EnergyAmount = 1;
    public Ectoplasm() : base(CanonicalId, "Ectoplasm", RelicRarity.Ancient) { }
}

public sealed class Cauldron : RelicModel
{
    public const string CanonicalId = "Cauldron";
    public Cauldron() : base(CanonicalId, "Cauldron", RelicRarity.Shop) { }
}

public sealed class DollysMirror : RelicModel
{
    public const string CanonicalId = "DollysMirror";
    public DollysMirror() : base(CanonicalId, "Dolly's Mirror", RelicRarity.Shop) { }
}

public sealed class LeesWaffle : RelicModel
{
    public const string CanonicalId = "LeesWaffle";
    public const int HealAmount = 7;
    public const int MaxHpAmount = 7;
    public LeesWaffle() : base(CanonicalId, "Lee's Waffle", RelicRarity.Shop) { }
}

public sealed class TheBoot : RelicModel
{
    public const string CanonicalId = "TheBoot";
    public TheBoot() : base(CanonicalId, "The Boot", RelicRarity.Event) { }
}

// === Silent-specific starter / drop relics ===

public sealed class SneckosEye : RelicModel
{
    public const string CanonicalId = "SneckosEye";
    public const int CardsDrawn = 2;
    public SneckosEye() : base(CanonicalId, "Snecko's Eye", RelicRarity.Rare) { }
}

public sealed class SnakeSkull : RelicModel
{
    public const string CanonicalId = "SnakeSkull";
    public const int PoisonOnDiscardAmount = 1;
    public SnakeSkull() : base(CanonicalId, "Snake Skull", RelicRarity.Rare) { }
}

public sealed class NinjaScroll : RelicModel
{
    public const string CanonicalId = "NinjaScroll";
    public const int Shivs = 3;
    public NinjaScroll() : base(CanonicalId, "Ninja Scroll", RelicRarity.Common) { }
}

public sealed class PaintedFan : RelicModel
{
    public const string CanonicalId = "PaintedFan";
    public PaintedFan() : base(CanonicalId, "Painted Fan", RelicRarity.Common) { }
}

public sealed class TwistedFunnel : RelicModel
{
    public const string CanonicalId = "TwistedFunnel";
    public const int PoisonOnTurnStart = 3;
    public TwistedFunnel() : base(CanonicalId, "Twisted Funnel", RelicRarity.Rare) { }
}

public sealed class WristBlade : RelicModel
{
    public const string CanonicalId = "WristBlade";
    public const int DamageBonus = 4;
    public WristBlade() : base(CanonicalId, "Wrist Blade", RelicRarity.Uncommon) { }
}

public sealed class Tingsha : RelicModel
{
    public const string CanonicalId = "Tingsha";
    public const int DamagePerDiscard = 3;
    public Tingsha() : base(CanonicalId, "Tingsha", RelicRarity.Uncommon) { }
}

public sealed class TheTotem : RelicModel
{
    public const string CanonicalId = "TheTotem";
    public TheTotem() : base(CanonicalId, "The Totem", RelicRarity.Uncommon) { }
}

public sealed class TornCard : RelicModel
{
    public const string CanonicalId = "TornCard";
    public TornCard() : base(CanonicalId, "Torn Card", RelicRarity.Rare) { }
}

public sealed class HoveringKite : RelicModel
{
    public const string CanonicalId = "HoveringKite";
    public const int EnergyOnDiscard = 1;
    public HoveringKite() : base(CanonicalId, "Hovering Kite", RelicRarity.Rare) { }
}

// === Generic uncommon/rare ===

public sealed class CeramicFish : RelicModel
{
    public const string CanonicalId = "CeramicFish";
    public const int Gold = 9;
    public CeramicFish() : base(CanonicalId, "Ceramic Fish", RelicRarity.Common) { }
}

public sealed class Strawberry : RelicModel
{
    public const string CanonicalId = "Strawberry";
    public const int MaxHpAmount = 7;
    public Strawberry() : base(CanonicalId, "Strawberry", RelicRarity.Common) { }
}

public sealed class Pear : RelicModel
{
    public const string CanonicalId = "Pear";
    public const int MaxHpAmount = 10;
    public Pear() : base(CanonicalId, "Pear", RelicRarity.Common) { }
}

public sealed class Anchovy : RelicModel
{
    public const string CanonicalId = "Anchovy";
    public const int MaxHpAmount = 8;
    public Anchovy() : base(CanonicalId, "Anchovy", RelicRarity.Common) { }
}

public sealed class Lantern : RelicModel
{
    public const string CanonicalId = "Lantern";
    public const int EnergyAmount = 1;
    public Lantern() : base(CanonicalId, "Lantern", RelicRarity.Uncommon) { }
}

public sealed class Cookbook : RelicModel
{
    public const string CanonicalId = "Cookbook";
    public Cookbook() : base(CanonicalId, "Cookbook", RelicRarity.Common) { }
}

public sealed class GremlinHorn : RelicModel
{
    public const string CanonicalId = "GremlinHorn";
    public const int EnergyOnKill = 1;
    public const int CardOnKill = 1;
    public GremlinHorn() : base(CanonicalId, "Gremlin Horn", RelicRarity.Uncommon) { }
}

public sealed class HornCleat : RelicModel
{
    public const string CanonicalId = "HornCleat";
    public const int BlockOnTurn2 = 14;
    public HornCleat() : base(CanonicalId, "Horn Cleat", RelicRarity.Uncommon) { }
}

public sealed class LetterOpener : RelicModel
{
    public const string CanonicalId = "LetterOpener";
    public const int DamageOnThirdSkill = 5;
    public LetterOpener() : base(CanonicalId, "Letter Opener", RelicRarity.Uncommon) { }
}

public sealed class OrnamentalFan : RelicModel
{
    public const string CanonicalId = "OrnamentalFan";
    public const int BlockOnThirdAttack = 4;
    public OrnamentalFan() : base(CanonicalId, "Ornamental Fan", RelicRarity.Uncommon) { }
}

public sealed class Orichalcum : RelicModel
{
    public const string CanonicalId = "Orichalcum";
    public const int BlockOnTurnEndIfZero = 6;
    public Orichalcum() : base(CanonicalId, "Orichalcum", RelicRarity.Uncommon) { }

    /// <summary>
    /// Verbatim port of upstream <c>Orichalcum.BeforeTurnEnd</c>: if the player
    /// has zero block at end of turn, gain 6 block. The gating predicate runs
    /// at dispatch time via <see cref="ConditionalGainBlockAction"/>.
    /// </summary>
    protected override void SubscribeHooks(HookRegistry hooks)
    {
        Subscribe(hooks, HookType.BeforeTurnEnd, ctx =>
        {
            ctx.Execution.Queue.Enqueue(new ConditionalGainBlockAction(BlockOnTurnEndIfZero));
        });
    }
}

public sealed class Kunai : RelicModel
{
    public const string CanonicalId = "Kunai";
    public const int DexterityAmount = 1;
    public Kunai() : base(CanonicalId, "Kunai", RelicRarity.Uncommon) { }
}

public sealed class Shuriken : RelicModel
{
    public const string CanonicalId = "Shuriken";
    public const int StrengthAmount = 1;
    public Shuriken() : base(CanonicalId, "Shuriken", RelicRarity.Uncommon) { }
}

public sealed class ToyOrnithopter : RelicModel
{
    public const string CanonicalId = "ToyOrnithopter";
    public const int HealPerPotion = 5;
    public ToyOrnithopter() : base(CanonicalId, "Toy Ornithopter", RelicRarity.Uncommon) { }
}
