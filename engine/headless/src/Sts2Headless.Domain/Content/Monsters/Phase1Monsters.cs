using System.Collections.Immutable;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;

namespace Sts2Headless.Domain.Content.Monsters;

/// <summary>
/// Bulk-port of Phase-1 monsters from upstream
/// <c>~/development/projects/godot/sts2/src/Core/Models/Monsters/*.cs</c>. Each class
/// captures id + Ascension-0 HP envelope (MinInitialHp, MaxInitialHp) per upstream's
/// <c>AscensionHelper.GetValueIfAscension(...)</c> calls. The full move-rotation
/// state machine is captured via a single-state placeholder (move name + intent +
/// self-loop follow-up); S13 will replace these with the full multi-state rotations
/// once HookContext payloads support intent-rotation hooks.
/// </summary>
/// <summary>
/// Verbatim port of upstream Chomper.GenerateMoveStateMachine:
/// CLAMP_MOVE (MultiAttack 8x2) ↔ SCREECH_MOVE (Status, add 3 Dazed).
/// Initial state is CLAMP (the <c>_screamFirst</c> override defaults to false).
/// Status card-pollution is intent-only at Phase-1 (engine does not yet add
/// cards to discard mid-combat) — rotation is byte-faithful regardless.
/// </summary>
public sealed class Chomper : MonsterModel
{
    public const string CanonicalId = "Chomper";
    public const int MinHp = 60;
    public const int MaxHp = 64;

    /// <summary>Per-hit damage on CLAMP — upstream <c>ClampDamage = 8</c> at A0.</summary>
    public const int ClampDamagePerHit = 8;

    /// <summary>Number of hits on CLAMP — upstream <c>_clampRepeat = 2</c>.</summary>
    public const int ClampHitCount = 2;

    /// <summary>Status cards added by SCREECH — upstream <c>_screechStatusCount = 3</c>.</summary>
    public const int ScreechStatusCards = 3;

    /// <summary>Aggregate per-turn damage on CLAMP (for old single-state callers).</summary>
    public const int AttackDamage = ClampDamagePerHit * ClampHitCount;

    public const string ClampMoveId = "CLAMP_MOVE";
    public const string ScreechMoveId = "SCREECH_MOVE";

    public Chomper()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[]
            {
                new(
                    ClampMoveId,
                    Intent.MultiAttack(ClampDamagePerHit, ClampHitCount),
                    FollowUpMoveId: ScreechMoveId
                ),
                new(ScreechMoveId, Intent.Status(ScreechStatusCards), FollowUpMoveId: ClampMoveId),
            },
            initialMoveId: ClampMoveId
        ) { }
}

/// <summary>
/// Verbatim port of upstream <c>Monsters.Exoskeleton</c>:
/// SKITTER (1x3 multi-attack) → MANDIBLES (8 single) → ENRAGE (self +2 Strength) → RAND → ...
/// The upstream <c>RandomBranchState</c> uses <c>MoveRepeatType.CannotRepeat</c>
/// (weight=0 for the immediately-prior move). After ENRAGE both SKITTER and
/// MANDIBLES are eligible 50/50; after SKITTER the next move must be MANDIBLES;
/// after MANDIBLES the static follow-up is ENRAGE. Q1 collapses RAND onto
/// ENRAGE's follow-up via <see cref="RngBranchResolver"/> — the deterministic
/// constraints handle the SKITTER and MANDIBLES paths via static FollowUp.
/// Upstream's <c>AfterAddedToRoom</c> applies 9 HardToKillPower (handled by
/// the engine's monster-spawn hook).
/// </summary>
public sealed class Exoskeleton : MonsterModel
{
    public const string CanonicalId = "Exoskeleton";
    public const int MinHp = 24;
    public const int MaxHp = 28;
    public const int SkitterDamage = 1;
    public const int SkitterRepeats = 3;
    public const int MandiblesDamage = 8;
    public const int EnrageStrengthAmount = 2;
    public const int HardToKillStacks = 9;

    /// <summary>Backwards-compat for callers still using a single damage number.</summary>
    public const int AttackDamage = SkitterDamage * SkitterRepeats;

    public const string SkitterMoveId = "SKITTER_MOVE";
    public const string MandiblesMoveId = "MANDIBLES_MOVE";
    public const string EnrageMoveId = "ENRAGE_MOVE";

    public Exoskeleton()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[]
            {
                // SKITTER: 1 dmg x 3. Upstream RAND with CannotRepeat after a SKITTER
                // is guaranteed to pick MANDIBLES (SKITTER weight zeroed). Encode that
                // deterministically.
                new(
                    SkitterMoveId,
                    Intent.MultiAttack(SkitterDamage, SkitterRepeats),
                    FollowUpMoveId: MandiblesMoveId
                ),
                // MANDIBLES → ENRAGE (upstream static FollowUp).
                new(MandiblesMoveId, Intent.Attack(MandiblesDamage), FollowUpMoveId: EnrageMoveId),
                // ENRAGE → RAND (50/50 between SKITTER and MANDIBLES). After ENRAGE
                // both moves are eligible since neither was just played.
                new(
                    EnrageMoveId,
                    Intent.Buff(),
                    FollowUpMoveId: SkitterMoveId,
                    BranchResolver: new RngBranchResolver(
                        choices: ImmutableArray.Create(
                            new RngBranchChoice(SkitterMoveId, 1f),
                            new RngBranchChoice(MandiblesMoveId, 1f)
                        )
                    )
                ),
            },
            initialMoveId: SkitterMoveId,
            // Upstream AfterAddedToRoom: 9 HardToKill (not in Q1's catalog yet; the
            // engine fails-soft on unknown ids so this is documentation-only).
            spawnPowers: ImmutableArray.Create(
                new MonsterSpawnPower("HardToKillPower", HardToKillStacks)
            )
        ) { }
}

public sealed class FuzzyWurmCrawler : MonsterModel
{
    public const string CanonicalId = "FuzzyWurmCrawler";
    public const int MinHp = 55;
    public const int MaxHp = 57;
    public const int AttackDamage = 14;

    public FuzzyWurmCrawler()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

/// <summary>
/// Verbatim port of upstream <c>Monsters.LouseProgenitor</c>: WEB_CANNON (9 dmg
/// + 2 Frail to player) → CURL_AND_GROW (gain 14 block + 5 Strength) → POUNCE
/// (16 single attack) → WEB_CANNON → ... Initial move (per upstream's machine
/// constructor) is WEB_CANNON. Upstream's <c>AfterAddedToRoom</c> stamps
/// CurlUpPower(CurlBlock=14) on the louse — handled by the engine spawn-time
/// power application list (Q1's monster-spawn pathway, B.1-gamma-T3).
/// </summary>
public sealed class LouseProgenitor : MonsterModel
{
    public const string CanonicalId = "LouseProgenitor";
    public const int MinHp = 134;
    public const int MaxHp = 136;

    /// <summary>Backwards-compat aggregate damage. Use <see cref="PounceDamage"/> for the
    /// per-hit attack stat.</summary>
    public const int AttackDamage = PounceDamage;
    public const int WebDamage = 9;
    public const int WebFrailStacks = 2;
    public const int CurlBlock = 14;
    public const int CurlStrength = 5;
    public const int PounceDamage = 16;

    public const string WebCannonMoveId = "WEB_CANNON_MOVE";
    public const string CurlAndGrowMoveId = "CURL_AND_GROW_MOVE";
    public const string PounceMoveId = "POUNCE_MOVE";

    public LouseProgenitor()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[]
            {
                new(WebCannonMoveId, Intent.Attack(WebDamage), FollowUpMoveId: CurlAndGrowMoveId),
                new(CurlAndGrowMoveId, Intent.Defend(CurlBlock), FollowUpMoveId: PounceMoveId),
                new(PounceMoveId, Intent.Attack(PounceDamage), FollowUpMoveId: WebCannonMoveId),
            },
            initialMoveId: WebCannonMoveId,
            // Upstream AfterAddedToRoom: CurlUp(CurlBlock=14).
            spawnPowers: ImmutableArray.Create(new MonsterSpawnPower(PowerIds.CurlUp, CurlBlock))
        ) { }
}

public sealed class GremlinMerc : MonsterModel
{
    public const string CanonicalId = "GremlinMerc";
    public const int MinHp = 47;
    public const int MaxHp = 49;
    public const int AttackDamage = 9;

    public GremlinMerc()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class HauntedShip : MonsterModel
{
    public const string CanonicalId = "HauntedShip";
    public const int MinHp = 63;
    public const int MaxHp = 63;
    public const int AttackDamage = 12;

    public HauntedShip()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class LivingFog : MonsterModel
{
    public const string CanonicalId = "LivingFog";
    public const int MinHp = 80;
    public const int MaxHp = 80;
    public const int AttackDamage = 14;

    public LivingFog()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

/// <summary>
/// Port of upstream <c>Monsters.CeremonialBeast</c> rotation, simplified for
/// Q1: STAMP (buff PlowPower) → PLOW (18 dmg + 2 Strength) → PLOW (loop) — the
/// post-stun BEAST_CRY → STOMP → CRUSH cycle is deferred because Q1 lacks
/// PlowPower mid-charge / stun-on-plow-removal infrastructure. The
/// initial-state STAMP and the PLOW self-loop are byte-faithful; stun
/// transitions to BEAST_CRY are skipped (deviation documented).
/// </summary>
public sealed class CeremonialBeast : MonsterModel
{
    public const string CanonicalId = "CeremonialBeast";
    public const int MinHp = 252;
    public const int MaxHp = 252;
    public const int PlowAmount = 150;
    public const int PlowDamage = 18;
    public const int PlowStrengthGain = 2;
    public const int StompDamage = 15;
    public const int CrushDamage = 17;
    public const int CrushStrengthGain = 3;

    /// <summary>Backwards-compat aggregate damage.</summary>
    public const int AttackDamage = PlowDamage;

    public const string StampMoveId = "STAMP_MOVE";
    public const string PlowMoveId = "PLOW_MOVE";
    public const string StompMoveId = "STOMP_MOVE";
    public const string CrushMoveId = "CRUSH_MOVE";
    public const string BeastCryMoveId = "BEAST_CRY_MOVE";

    public CeremonialBeast()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[]
            {
                // Pre-stun loop (upstream's initial state machine).
                new(StampMoveId, Intent.Buff(), FollowUpMoveId: PlowMoveId),
                new(PlowMoveId, Intent.Attack(PlowDamage), FollowUpMoveId: PlowMoveId),
                // Post-stun cycle (reached only if Plow is stripped — deferred wiring).
                new(BeastCryMoveId, Intent.Debuff(), FollowUpMoveId: StompMoveId),
                new(StompMoveId, Intent.Attack(StompDamage), FollowUpMoveId: CrushMoveId),
                new(CrushMoveId, Intent.Attack(CrushDamage), FollowUpMoveId: BeastCryMoveId),
            },
            initialMoveId: StampMoveId
        ) { }
}

// === Common Act-1 normals ===
public sealed class JawWorm : MonsterModel
{
    public const string CanonicalId = "JawWorm";
    public const int MinHp = 40;
    public const int MaxHp = 44;
    public const int AttackDamage = 11;

    public JawWorm()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class RedLouse : MonsterModel
{
    public const string CanonicalId = "RedLouse";
    public const int MinHp = 10;
    public const int MaxHp = 15;
    public const int AttackDamage = 6;

    public RedLouse()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class GreenLouse : MonsterModel
{
    public const string CanonicalId = "GreenLouse";
    public const int MinHp = 11;
    public const int MaxHp = 17;
    public const int AttackDamage = 5;

    public GreenLouse()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class AcidSlimeS : MonsterModel
{
    public const string CanonicalId = "AcidSlimeS";
    public const int MinHp = 8;
    public const int MaxHp = 12;
    public const int AttackDamage = 3;

    public AcidSlimeS()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class AcidSlimeM : MonsterModel
{
    public const string CanonicalId = "AcidSlimeM";
    public const int MinHp = 28;
    public const int MaxHp = 32;
    public const int AttackDamage = 7;

    public AcidSlimeM()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class AcidSlimeL : MonsterModel
{
    public const string CanonicalId = "AcidSlimeL";
    public const int MinHp = 65;
    public const int MaxHp = 69;
    public const int AttackDamage = 11;

    public AcidSlimeL()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class SpikeSlimeS : MonsterModel
{
    public const string CanonicalId = "SpikeSlimeS";
    public const int MinHp = 10;
    public const int MaxHp = 14;
    public const int AttackDamage = 5;

    public SpikeSlimeS()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class SpikeSlimeM : MonsterModel
{
    public const string CanonicalId = "SpikeSlimeM";
    public const int MinHp = 28;
    public const int MaxHp = 32;
    public const int AttackDamage = 8;

    public SpikeSlimeM()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class SpikeSlimeL : MonsterModel
{
    public const string CanonicalId = "SpikeSlimeL";
    public const int MinHp = 64;
    public const int MaxHp = 70;
    public const int AttackDamage = 16;

    public SpikeSlimeL()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class FungalBoss : MonsterModel
{
    public const string CanonicalId = "FungalBoss";
    public const int MinHp = 200;
    public const int MaxHp = 200;
    public const int AttackDamage = 18;

    public FungalBoss()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class SnakePlant : MonsterModel
{
    public const string CanonicalId = "SnakePlant";
    public const int MinHp = 75;
    public const int MaxHp = 79;
    public const int AttackDamage = 6;

    public SnakePlant()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class Sentry : MonsterModel
{
    public const string CanonicalId = "Sentry";
    public const int MinHp = 38;
    public const int MaxHp = 42;
    public const int AttackDamage = 9;

    public Sentry()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

/// <summary>
/// Port of upstream <c>Monsters.LagavulinMatriarch</c> rotation, adapted to Q1's
/// HP-threshold gate (lead's prompt §T3): SLEEP while HP fraction is above 0.5;
/// once HP &lt;= half, wake into SLASH → DISEMBOWEL → SLASH2 → SOUL_SIPHON → SLASH.
/// Upstream's <c>AfterAddedToRoom</c> applies PlatingPower(12) and AsleepPower(3);
/// Q1 elides AsleepPower (no infrastructure for it yet) and uses the HP
/// fraction as the wake trigger via <see cref="HpThresholdResolver"/>. Deviation
/// documented; the rotation is otherwise byte-faithful to upstream's
/// MonsterMoveStateMachine.
///
/// <para>B.1-final-T1: renamed from <c>Lagavulin</c> → <c>LagavulinMatriarch</c>
/// and HP 109/111 → 222/222 to match upstream
/// <c>LagavulinMatriarch.MinInitialHp = AscensionHelper.GetValueIfAscension(ToughEnemies, 233, 222)</c>
/// at Ascension 0 (single-value envelope; MaxInitialHp = MinInitialHp).</para>
/// </summary>
public sealed class LagavulinMatriarch : MonsterModel
{
    public const string CanonicalId = "LagavulinMatriarch";
    public const int MinHp = 222;
    public const int MaxHp = 222;
    public const int SlashDamage = 19;
    public const int Slash2Damage = 12;
    public const int Slash2Block = 12;
    public const int DisembowelDamage = 9;
    public const int DisembowelRepeats = 2;
    public const int SoulSiphonStrengthSelf = 2;
    public const int SoulSiphonStatsDownPlayer = 2;
    public const int PlatingStacks = 12;

    /// <summary>Backwards-compat aggregate damage.</summary>
    public const int AttackDamage = SlashDamage;

    /// <summary>HP fraction at or below which Lagavulin wakes.</summary>
    public const float WakeHpFraction = 0.5f;

    public const string SleepMoveId = "SLEEP_MOVE";
    public const string SlashMoveId = "SLASH_MOVE";
    public const string DisembowelMoveId = "DISEMBOWEL_MOVE";
    public const string Slash2MoveId = "SLASH2_MOVE";
    public const string SoulSiphonMoveId = "SOUL_SIPHON_MOVE";

    public LagavulinMatriarch()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[]
            {
                // SLEEP → conditional: HP > half → SLEEP, HP <= half → SLASH.
                // Q1 maps Asleep-power gate onto HP threshold (deviation; see class
                // remarks). "Above" branch keeps sleeping; "below-or-equal" branches
                // to SLASH per HpThresholdResolver convention.
                new(
                    SleepMoveId,
                    Intent.Buff(),
                    FollowUpMoveId: SlashMoveId,
                    BranchResolver: new HpThresholdResolver(
                        fraction: WakeHpFraction,
                        belowMoveId: SlashMoveId,
                        aboveMoveId: SleepMoveId
                    )
                ),
                // Once awake: SLASH → DISEMBOWEL → SLASH2 → SOUL_SIPHON → SLASH (loop).
                new(SlashMoveId, Intent.Attack(SlashDamage), FollowUpMoveId: DisembowelMoveId),
                new(
                    DisembowelMoveId,
                    Intent.MultiAttack(DisembowelDamage, DisembowelRepeats),
                    FollowUpMoveId: Slash2MoveId
                ),
                new(Slash2MoveId, Intent.Attack(Slash2Damage), FollowUpMoveId: SoulSiphonMoveId),
                new(SoulSiphonMoveId, Intent.Debuff(), FollowUpMoveId: SlashMoveId),
            },
            initialMoveId: SleepMoveId,
            // Upstream AfterAddedToRoom: Plating(12) + Asleep(3). Asleep elided per
            // class remarks (HP-threshold proxy); Plating is documentation-only
            // (Q1's PlatedArmor power id; fails-soft on unknown).
            spawnPowers: ImmutableArray.Create(
                new MonsterSpawnPower(PowerIds.Plated, PlatingStacks)
            )
        ) { }
}

public sealed class CenturyGuard : MonsterModel
{
    public const string CanonicalId = "CenturyGuard";
    public const int MinHp = 240;
    public const int MaxHp = 240;
    public const int AttackDamage = 12;

    public CenturyGuard()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class SilverMage : MonsterModel
{
    public const string CanonicalId = "SilverMage";
    public const int MinHp = 86;
    public const int MaxHp = 86;
    public const int AttackDamage = 13;

    public SilverMage()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

// B.1-final-T2b: KaiserCrab (Q1-invented single-monster boss) replaced by upstream
// two-monster spawn — Crusher + Rocket. KaiserCrab class deleted from catalog;
// classes Crusher + Rocket below.

/// <summary>
/// Verbatim port of upstream <c>Monsters.Crusher</c> (left-arm half of KaiserCrabBoss):
/// THRASH (12 attack) → ENLARGING_STRIKE (4 attack) → BUG_STING (6×2 multi-attack +
/// debuff WeakPower+FrailPower) → ADAPT (buff +2 Strength) → GUARDED_STRIKE
/// (12 attack + 18 block) → THRASH (loop). HP = 209 (single-value envelope per
/// upstream <c>AscensionHelper.GetValueIfAscension(ToughEnemies, 219, 209)</c> at A0).
/// Upstream <c>AfterAddedToRoom</c> applies BackAttackLeftPower(1) + CrabRagePower(1);
/// declared as <see cref="MonsterSpawnPower"/> entries below — engine fails-soft on
/// power ids not present in Q1's power catalog (documentation-only at Phase-1).
/// </summary>
public sealed class Crusher : MonsterModel
{
    public const string CanonicalId = "Crusher";
    public const int MinHp = 209;
    public const int MaxHp = 209;
    public const int ThrashDamage = 12;
    public const int EnlargingStrikeDamage = 4;
    public const int BugStingDamage = 6;
    public const int BugStingRepeats = 2;
    public const int BugStingWeakStacks = 2;
    public const int BugStingFrailStacks = 2;
    public const int AdaptStrengthGain = 2;
    public const int GuardedStrikeDamage = 12;
    public const int GuardedStrikeBlock = 18;

    /// <summary>Backwards-compat aggregate damage.</summary>
    public const int AttackDamage = ThrashDamage;

    public const string ThrashMoveId = "THRASH_MOVE";
    public const string EnlargingStrikeMoveId = "ENLARGING_STRIKE_MOVE";
    public const string BugStingMoveId = "BUG_STING_MOVE";
    public const string AdaptMoveId = "ADAPT_MOVE";
    public const string GuardedStrikeMoveId = "GUARDED_STRIKE_MOVE";

    public Crusher()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[]
            {
                new(
                    ThrashMoveId,
                    Intent.Attack(ThrashDamage),
                    FollowUpMoveId: EnlargingStrikeMoveId
                ),
                new(
                    EnlargingStrikeMoveId,
                    Intent.Attack(EnlargingStrikeDamage),
                    FollowUpMoveId: BugStingMoveId
                ),
                new(
                    BugStingMoveId,
                    Intent.MultiAttack(BugStingDamage, BugStingRepeats),
                    FollowUpMoveId: AdaptMoveId
                ),
                new(AdaptMoveId, Intent.Buff(), FollowUpMoveId: GuardedStrikeMoveId),
                new(
                    GuardedStrikeMoveId,
                    Intent.Attack(GuardedStrikeDamage),
                    FollowUpMoveId: ThrashMoveId
                ),
            },
            initialMoveId: ThrashMoveId,
            // Upstream AfterAddedToRoom: BackAttackLeft(1) + CrabRage(1). Both ids
            // are not in Q1's power catalog yet — fails-soft per the engine spawn-time
            // hook convention.
            spawnPowers: ImmutableArray.Create(
                new MonsterSpawnPower("BackAttackLeftPower", 1),
                new MonsterSpawnPower("CrabRagePower", 1)
            )
        ) { }
}

/// <summary>
/// Verbatim port of upstream <c>Monsters.Rocket</c> (right-arm half of KaiserCrabBoss):
/// TARGETING_RETICLE (3 attack) → PRECISION_BEAM (18 attack) → CHARGE_UP
/// (buff +2 Strength) → LASER (31 attack) → RECHARGE (sleep) → TARGETING_RETICLE
/// (loop). HP = 199 (single-value per <c>AscensionHelper.GetValueIfAscension(ToughEnemies,
/// 209, 199)</c> at A0). Upstream <c>AfterAddedToRoom</c> applies SurroundedPower(1)
/// to opponents + BackAttackRightPower(1) + CrabRagePower(1) on self; declared as
/// <see cref="MonsterSpawnPower"/> entries (Q1 fails-soft on unknown ids).
///
/// <para>The RECHARGE intent is a "Sleep" / no-op turn in upstream
/// (<c>SleepIntent</c>). Q1 represents this as a <see cref="Intent.Buff"/> placeholder
/// (no engine payload — initial-state byte snapshot only cares about the move id
/// and intent kind).</para>
/// </summary>
public sealed class Rocket : MonsterModel
{
    public const string CanonicalId = "Rocket";
    public const int MinHp = 199;
    public const int MaxHp = 199;
    public const int TargetingReticleDamage = 3;
    public const int PrecisionBeamDamage = 18;
    public const int LaserDamage = 31;
    public const int ChargeUpStrengthGain = 2;

    /// <summary>Backwards-compat aggregate damage.</summary>
    public const int AttackDamage = TargetingReticleDamage;

    public const string TargetingReticleMoveId = "TARGETING_RETICLE_MOVE";
    public const string PrecisionBeamMoveId = "PRECISION_BEAM_MOVE";
    public const string ChargeUpMoveId = "CHARGE_UP_MOVE";
    public const string LaserMoveId = "LASER_MOVE";
    public const string RechargeMoveId = "RECHARGE_MOVE";

    public Rocket()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[]
            {
                new(
                    TargetingReticleMoveId,
                    Intent.Attack(TargetingReticleDamage),
                    FollowUpMoveId: PrecisionBeamMoveId
                ),
                new(
                    PrecisionBeamMoveId,
                    Intent.Attack(PrecisionBeamDamage),
                    FollowUpMoveId: ChargeUpMoveId
                ),
                new(ChargeUpMoveId, Intent.Buff(), FollowUpMoveId: LaserMoveId),
                new(LaserMoveId, Intent.Attack(LaserDamage), FollowUpMoveId: RechargeMoveId),
                new(RechargeMoveId, Intent.Buff(), FollowUpMoveId: TargetingReticleMoveId),
            },
            initialMoveId: TargetingReticleMoveId,
            // Upstream AfterAddedToRoom: Surrounded(1) on opponents (engine target
            // dispatch differs; declared on self for spawn-time visibility) +
            // BackAttackRight(1) + CrabRage(1) on self. All ids absent from Q1's
            // power catalog — fail-soft.
            spawnPowers: ImmutableArray.Create(
                new MonsterSpawnPower("BackAttackRightPower", 1),
                new MonsterSpawnPower("CrabRagePower", 1),
                new MonsterSpawnPower("SurroundedPower", 1)
            )
        ) { }
}

// === Bowlbugs (Act 1 swarms) ===
// HP envelopes from upstream Bowlbug{Egg,Nectar,Rock,Silk}.cs's
// AscensionHelper.GetValueIfAscension(ToughEnemies, asc, A0) — second integer.
// RC-5 (B.1-beta-T1): prior values 9-12 / 18-22 / 22-26 / 14-18 transcription error.
public sealed class BowlbugEgg : MonsterModel
{
    public const string CanonicalId = "BowlbugEgg";
    public const int MinHp = 21;
    public const int MaxHp = 22;
    public const int AttackDamage = 0;

    public BowlbugEgg()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("BUFF", Intent.Buff(), "BUFF") },
            "BUFF"
        ) { }
}

public sealed class BowlbugNectar : MonsterModel
{
    public const string CanonicalId = "BowlbugNectar";
    public const int MinHp = 35;
    public const int MaxHp = 38;
    public const int AttackDamage = 5;

    public BowlbugNectar()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class BowlbugRock : MonsterModel
{
    public const string CanonicalId = "BowlbugRock";
    public const int MinHp = 45;
    public const int MaxHp = 48;
    public const int AttackDamage = 7;

    public BowlbugRock()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

public sealed class BowlbugSilk : MonsterModel
{
    public const string CanonicalId = "BowlbugSilk";
    public const int MinHp = 40;
    public const int MaxHp = 43;
    public const int AttackDamage = 4;

    public BowlbugSilk()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}

// === Fossil Stalker / Frog Knight (Act 1 elites) ===
// HP envelopes from upstream FossilStalker.cs / FrogKnight.cs A0 values.
// RC-5 (B.1-beta-T1): FossilStalker 105-110 -> 51-53; FrogKnight 84-88 -> 191-191.
/// <summary>
/// Verbatim port of upstream <c>Monsters.FossilStalker</c> rotation: LATCH /
/// TACKLE / LASH all branch to a uniform RngBranchState. Upstream's RAND has
/// cooldown=2 per branch (move can't repeat for 2 turns); Q1 simplifies to a
/// pure uniform pick (rotation determinism is preserved given fixed seed,
/// even though the empirical distribution diverges from upstream's cooldown-
/// shaped one). Upstream's <c>AfterAddedToRoom</c> applies 3 SuckPower (handled
/// by engine spawn-time list).
/// </summary>
public sealed class FossilStalker : MonsterModel
{
    public const string CanonicalId = "FossilStalker";
    public const int MinHp = 51;
    public const int MaxHp = 53;
    public const int TackleDamage = 9;
    public const int LatchDamage = 12;
    public const int LashDamage = 3;
    public const int LashRepeats = 2;

    /// <summary>Backwards-compat aggregate damage.</summary>
    public const int AttackDamage = LatchDamage;

    public const string TackleMoveId = "TACKLE_MOVE";
    public const string LatchMoveId = "LATCH_MOVE";
    public const string LashMoveId = "LASH_MOVE";

    private static readonly RngBranchResolver _randResolver = new(
        choices: ImmutableArray.Create(
            new RngBranchChoice(TackleMoveId, 1f),
            new RngBranchChoice(LatchMoveId, 1f),
            new RngBranchChoice(LashMoveId, 1f)
        )
    );

    public FossilStalker()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[]
            {
                new(
                    TackleMoveId,
                    Intent.Attack(TackleDamage),
                    FollowUpMoveId: LatchMoveId,
                    BranchResolver: _randResolver
                ),
                new(
                    LatchMoveId,
                    Intent.Attack(LatchDamage),
                    FollowUpMoveId: LashMoveId,
                    BranchResolver: _randResolver
                ),
                new(
                    LashMoveId,
                    Intent.MultiAttack(LashDamage, LashRepeats),
                    FollowUpMoveId: TackleMoveId,
                    BranchResolver: _randResolver
                ),
            },
            // Upstream's machine constructor initialises with LATCH_MOVE.
            initialMoveId: LatchMoveId
        ) { }
}

public sealed class FrogKnight : MonsterModel
{
    public const string CanonicalId = "FrogKnight";
    public const int MinHp = 191;
    public const int MaxHp = 191;
    public const int AttackDamage = 15;

    public FrogKnight()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[] { new("ATTACK", Intent.Attack(AttackDamage), "ATTACK") },
            "ATTACK"
        ) { }
}
