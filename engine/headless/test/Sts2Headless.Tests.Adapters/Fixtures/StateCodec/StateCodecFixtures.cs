using System.Collections.Immutable;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Adapters.Fixtures.StateCodec;

/// <summary>
/// One fixture tuple. The bit-identical-roundtrip CI gate asserts
/// <c>Serialize(Deserialize(Serialize(t))) == Serialize(t)</c> byte-for-byte
/// for each fixture in <see cref="StateCodecFixtures.GenerateAll"/>.
/// </summary>
public sealed record StateCodecFixture(
    string Name,
    CombatState State,
    RunRngSet RunRng,
    PlayerRngSet PlayerRng,
    TokenMap Tokens,
    ManifestStamp Stamp
);

/// <summary>
/// Procedural fixture corpus for the M1 State Codec's hard gate. ~20
/// distinct (state, rng-bundle, tokens, stamp) tuples covering the
/// failure modes the bit-identical roundtrip must survive:
///
/// <list type="bullet">
///   <item>Empty/fresh combat start.</item>
///   <item>Mid-combat turn 1 (energy partial, hand drawn).</item>
///   <item>Turn 2 / Turn 3 (enemies taken turns; dead enemies left in list).</item>
///   <item>Edge cases: Poison 99 max stack, zero-HP creature, full hand,
///   fully exhausted deck, empty hand at turn-end.</item>
///   <item>ImmutableList sizes 0, 1, 50 for the piles.</item>
///   <item>Post-combat-end (CombatPhase.CombatEnd).</item>
///   <item>Manifest-stamp variety (empty strings, long strings, unicode).</item>
/// </list>
///
/// <para>
/// <b>Reproducibility:</b> fixtures are generated from code so a CI rerun
/// always sees the same corpus; no checked-in binary blobs to drift.
/// </para>
/// </summary>
public static class StateCodecFixtures
{
    private const string DefaultGitSha = "deadbeefcafebabe1234567890abcdef12345678";

    /// <summary>
    /// Build a canonical content hash from a fixed id list — used for the
    /// stamp so different runs of the fixture generator produce identical
    /// content hashes.
    /// </summary>
    private static byte[] CanonicalContentHash =>
        ManifestStamp.ContentHashFromIds(
            new[]
            {
                "Acrobatics",
                "Anchor",
                "Backflip",
                "BagOfPreparation",
                "BloodVial",
                "CalcifiedCultist",
                "CultistsNormal",
                "DampCultist",
                "DeadlyPoison",
                "DefendSilent",
                "DodgeAndRoll",
                "Neutralize",
                "PoisonPower",
                "RingOfTheSnake",
                "RitualPower",
                "Slice",
                "StrengthPower",
                "StrikeSilent",
                "Survivor",
                "Vajra",
                "VulnerablePower",
                "WeakPower",
            }
        );

    private static ManifestStamp Stamp(string buildId = "Q1-Phase1-fixture") =>
        new(DefaultGitSha, buildId, CanonicalContentHash);

    private static ManifestStamp StampUnicode() =>
        new("sha-éπ-1234", "build-世界-007", CanonicalContentHash);

    private static ManifestStamp StampEmptyStrings() => new("", "", CanonicalContentHash);

    private static ManifestStamp StampLongBuildId()
    {
        // ~1KB build id — exercises u16 length encoding away from trivial.
        string longId = string.Concat(Enumerable.Repeat("build-XYZ-001/", 80));
        return new(DefaultGitSha, longId, CanonicalContentHash);
    }

    private static TokenMap Tokens(params string[] entries)
    {
        TokenMap m = new();
        foreach (string e in entries)
        {
            m.GetOrAddId(e);
        }
        return m;
    }

    private static RunRngSet RunRng(string seed = "fixture-seed", int shuffleCount = 0)
    {
        RunRngSet set = new(seed);
        for (int i = 0; i < shuffleCount; i++)
        {
            set.Shuffle.NextInt();
        }
        return set;
    }

    private static PlayerRngSet PlayerRng(uint seed = 42u, int rewardsCount = 0)
    {
        PlayerRngSet set = new(seed);
        for (int i = 0; i < rewardsCount; i++)
        {
            set.Rewards.NextInt();
        }
        return set;
    }

    private static Creature BuildPlayer(
        int hp,
        int maxHp,
        int block,
        params PowerInstance[] powers
    ) =>
        new(
            CreatureId.Player,
            "Silent",
            hp,
            maxHp,
            block,
            powers.Length == 0
                ? ImmutableList<PowerInstance>.Empty
                : ImmutableList.CreateRange(powers),
            null,
            IsPlayer: true
        );

    private static Creature BuildEnemy(
        uint id,
        string name,
        int hp,
        int maxHp,
        int block,
        MonsterIntent? intent,
        params PowerInstance[] powers
    ) =>
        new(
            new CreatureId(id),
            name,
            hp,
            maxHp,
            block,
            powers.Length == 0
                ? ImmutableList<PowerInstance>.Empty
                : ImmutableList.CreateRange(powers),
            intent,
            IsPlayer: false
        );

    private static CardInstance Card(
        uint id,
        string modelId,
        int upgrade = 0,
        int? costOverride = null
    ) => new(id, modelId, upgrade, costOverride);

    private static CardPile Pile(params CardInstance[] cards) =>
        cards.Length == 0 ? CardPile.Empty : CardPile.OfRange(cards);

    private static MonsterIntent Attack(int damage, int hits = 1) =>
        new(MonsterIntentKind.Attack, damage, hits, ImmutableList<MonsterIntentPower>.Empty);

    private static MonsterIntent Buff(params MonsterIntentPower[] applies) =>
        new(MonsterIntentKind.Buff, 0, 0, ImmutableList.CreateRange(applies));

    /// <summary>
    /// Generate every fixture. Result is a fresh list — callers can mutate
    /// the returned list without side-effecting future calls (each call
    /// rebuilds the fixtures from scratch).
    /// </summary>
    public static List<StateCodecFixture> GenerateAll()
    {
        List<StateCodecFixture> result = new();

        // ---- F01: fresh combat start, Silent vs CultistsNormal seed=42 state-zero ----
        result.Add(
            new StateCodecFixture(
                "F01-fresh-combat-start",
                new CombatState(
                    TurnCounter: 0,
                    Phase: CombatPhase.CombatStart,
                    Player: BuildPlayer(70, 70, 0),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(1, "CalcifiedCultist", 50, 50, 0, null),
                        BuildEnemy(2, "DampCultist", 48, 48, 0, null)
                    ),
                    Energy: 3,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: CardPile.Empty,
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 0,
                    MonsterRngCounter: 0
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        // ---- F02: turn 1, hand drawn, no actions taken ----
        result.Add(
            new StateCodecFixture(
                "F02-turn1-hand-drawn",
                new CombatState(
                    TurnCounter: 1,
                    Phase: CombatPhase.PlayerActing,
                    Player: BuildPlayer(70, 70, 0),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(1, "CalcifiedCultist", 50, 50, 0, Attack(6)),
                        BuildEnemy(2, "DampCultist", 48, 48, 0, Attack(8))
                    ),
                    Energy: 3,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: Pile(
                        Card(10, "StrikeSilent"),
                        Card(11, "StrikeSilent"),
                        Card(12, "DefendSilent"),
                        Card(13, "Neutralize"),
                        Card(14, "Survivor")
                    ),
                    HandPile: Pile(
                        Card(20, "StrikeSilent"),
                        Card(21, "StrikeSilent"),
                        Card(22, "DefendSilent"),
                        Card(23, "DefendSilent"),
                        Card(24, "Survivor")
                    ),
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 5,
                    MonsterRngCounter: 2
                ),
                RunRng("seed-42", shuffleCount: 1),
                PlayerRng(42u, rewardsCount: 0),
                Tokens("StrikeSilent", "DefendSilent", "Neutralize", "Survivor"),
                Stamp()
            )
        );

        // ---- F03: turn 1 mid-play, energy partial, one card played ----
        result.Add(
            new StateCodecFixture(
                "F03-turn1-mid-play-energy-partial",
                new CombatState(
                    TurnCounter: 1,
                    Phase: CombatPhase.PlayerActing,
                    Player: BuildPlayer(70, 70, 5),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(1, "CalcifiedCultist", 44, 50, 0, Attack(6))
                    ),
                    Energy: 2,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: Pile(Card(11, "StrikeSilent"), Card(12, "DefendSilent")),
                    HandPile: Pile(Card(20, "Neutralize"), Card(21, "DefendSilent")),
                    DiscardPile: Pile(Card(10, "StrikeSilent")),
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 8,
                    MonsterRngCounter: 3
                ),
                RunRng("seed-42", shuffleCount: 1),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        // ---- F04: turn 2, enemy turn taken (enemies took an action) ----
        result.Add(
            new StateCodecFixture(
                "F04-turn2-after-enemy-attack",
                new CombatState(
                    TurnCounter: 2,
                    Phase: CombatPhase.PlayerActing,
                    Player: BuildPlayer(58, 70, 0),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(
                            1,
                            "CalcifiedCultist",
                            30,
                            50,
                            0,
                            Attack(6),
                            new PowerInstance("RitualPower", 3, new CreatureId(1u), false)
                        ),
                        BuildEnemy(2, "DampCultist", 35, 48, 0, Attack(8))
                    ),
                    Energy: 3,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: Pile(Card(11, "StrikeSilent")),
                    HandPile: Pile(
                        Card(20, "StrikeSilent"),
                        Card(21, "DefendSilent"),
                        Card(22, "Neutralize"),
                        Card(23, "Survivor"),
                        Card(24, "Survivor")
                    ),
                    DiscardPile: Pile(
                        Card(10, "StrikeSilent"),
                        Card(12, "DefendSilent"),
                        Card(13, "Neutralize"),
                        Card(14, "Survivor")
                    ),
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 12,
                    MonsterRngCounter: 5
                ),
                RunRng("seed-42", shuffleCount: 2),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        // ---- F05: turn 3, one enemy dead ----
        result.Add(
            new StateCodecFixture(
                "F05-turn3-one-enemy-dead",
                new CombatState(
                    TurnCounter: 3,
                    Phase: CombatPhase.PlayerActing,
                    Player: BuildPlayer(45, 70, 0),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(1, "CalcifiedCultist", 0, 50, 0, null), // DEAD
                        BuildEnemy(2, "DampCultist", 28, 48, 0, Attack(8))
                    ),
                    Energy: 3,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: Pile(Card(20, "StrikeSilent"), Card(21, "DefendSilent")),
                    DiscardPile: Pile(
                        Card(10, "StrikeSilent"),
                        Card(11, "DefendSilent"),
                        Card(12, "Neutralize"),
                        Card(13, "Survivor"),
                        Card(14, "StrikeSilent"),
                        Card(15, "DefendSilent"),
                        Card(16, "Neutralize"),
                        Card(17, "Survivor")
                    ),
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 20,
                    MonsterRngCounter: 7
                ),
                RunRng("seed-42", shuffleCount: 3),
                PlayerRng(42u, rewardsCount: 1),
                Tokens(),
                Stamp()
            )
        );

        // ---- F06: max-stack Poison 99 ----
        result.Add(
            new StateCodecFixture(
                "F06-max-stack-poison-99",
                new CombatState(
                    TurnCounter: 2,
                    Phase: CombatPhase.PlayerActing,
                    Player: BuildPlayer(70, 70, 0),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(
                            1,
                            "CalcifiedCultist",
                            20,
                            50,
                            0,
                            Attack(6),
                            new PowerInstance("PoisonPower", 99, new CreatureId(0u), false)
                        )
                    ),
                    Energy: 3,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: CardPile.Empty,
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 30,
                    MonsterRngCounter: 10
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        // ---- F07: zero-HP creature about to die (lethal queued) ----
        result.Add(
            new StateCodecFixture(
                "F07-zero-hp-creature",
                new CombatState(
                    TurnCounter: 4,
                    Phase: CombatPhase.EnemyActing,
                    Player: BuildPlayer(0, 70, 0), // about to die
                    Enemies: ImmutableList.Create(
                        BuildEnemy(1, "CalcifiedCultist", 10, 50, 0, Attack(6))
                    ),
                    Energy: 0,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: CardPile.Empty,
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 0,
                    MonsterRngCounter: 0
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        // ---- F08: full hand (10 cards) ----
        result.Add(
            new StateCodecFixture(
                "F08-full-hand-10",
                new CombatState(
                    TurnCounter: 1,
                    Phase: CombatPhase.PlayerActing,
                    Player: BuildPlayer(70, 70, 0),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(1, "CalcifiedCultist", 50, 50, 0, Attack(6))
                    ),
                    Energy: 3,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 10,
                    DrawPile: CardPile.Empty,
                    HandPile: Pile(
                        Card(20, "StrikeSilent"),
                        Card(21, "StrikeSilent"),
                        Card(22, "StrikeSilent"),
                        Card(23, "DefendSilent"),
                        Card(24, "DefendSilent"),
                        Card(25, "Neutralize"),
                        Card(26, "Survivor"),
                        Card(27, "Survivor"),
                        Card(28, "Acrobatics"),
                        Card(29, "Backflip")
                    ),
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 0,
                    MonsterRngCounter: 0
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens(
                    "StrikeSilent",
                    "DefendSilent",
                    "Neutralize",
                    "Survivor",
                    "Acrobatics",
                    "Backflip"
                ),
                Stamp()
            )
        );

        // ---- F09: fully exhausted deck (all cards in exhaust pile) ----
        result.Add(
            new StateCodecFixture(
                "F09-fully-exhausted-deck",
                new CombatState(
                    TurnCounter: 5,
                    Phase: CombatPhase.PlayerActing,
                    Player: BuildPlayer(40, 70, 5),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(1, "CalcifiedCultist", 30, 50, 0, Attack(6))
                    ),
                    Energy: 3,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: CardPile.Empty,
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: Pile(
                        Card(10, "StrikeSilent"),
                        Card(11, "StrikeSilent"),
                        Card(12, "DefendSilent"),
                        Card(13, "Neutralize"),
                        Card(14, "Survivor"),
                        Card(15, "Acrobatics"),
                        Card(16, "Backflip")
                    ),
                    PlayerRngCounter: 50,
                    MonsterRngCounter: 15
                ),
                RunRng("seed-42", shuffleCount: 5),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        // ---- F10: empty hand at turn-end (PlayerTurnEnd) ----
        result.Add(
            new StateCodecFixture(
                "F10-empty-hand-at-turn-end",
                new CombatState(
                    TurnCounter: 2,
                    Phase: CombatPhase.PlayerTurnEnd,
                    Player: BuildPlayer(60, 70, 10),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(1, "CalcifiedCultist", 35, 50, 0, Attack(6))
                    ),
                    Energy: 0,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: Pile(Card(11, "StrikeSilent")),
                    HandPile: CardPile.Empty,
                    DiscardPile: Pile(Card(10, "StrikeSilent"), Card(12, "DefendSilent")),
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 10,
                    MonsterRngCounter: 4
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        // ---- F11: ImmutableList count 50 in draw pile ----
        {
            CardInstance[] fifty = new CardInstance[50];
            for (int i = 0; i < 50; i++)
            {
                fifty[i] = Card((uint)(100 + i), i % 2 == 0 ? "StrikeSilent" : "DefendSilent");
            }
            result.Add(
                new StateCodecFixture(
                    "F11-draw-pile-50-cards",
                    new CombatState(
                        TurnCounter: 0,
                        Phase: CombatPhase.CombatStart,
                        Player: BuildPlayer(70, 70, 0),
                        Enemies: ImmutableList<Creature>.Empty,
                        Energy: 0,
                        BaseEnergyPerTurn: 3,
                        HandDrawSize: 5,
                        DrawPile: Pile(fifty),
                        HandPile: CardPile.Empty,
                        DiscardPile: CardPile.Empty,
                        ExhaustPile: CardPile.Empty,
                        PlayerRngCounter: 0,
                        MonsterRngCounter: 0
                    ),
                    RunRng("seed-42"),
                    PlayerRng(42u),
                    Tokens(),
                    Stamp()
                )
            );
        }

        // ---- F12: post-combat-end, victory ----
        result.Add(
            new StateCodecFixture(
                "F12-combat-end-victory",
                new CombatState(
                    TurnCounter: 6,
                    Phase: CombatPhase.CombatEnd,
                    Player: BuildPlayer(35, 70, 0),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(1, "CalcifiedCultist", 0, 50, 0, null),
                        BuildEnemy(2, "DampCultist", 0, 48, 0, null)
                    ),
                    Energy: 0,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: Pile(Card(20, "StrikeSilent"), Card(21, "DefendSilent")),
                    DiscardPile: Pile(
                        Card(10, "StrikeSilent"),
                        Card(11, "StrikeSilent"),
                        Card(12, "DefendSilent")
                    ),
                    ExhaustPile: Pile(Card(13, "Survivor")),
                    PlayerRngCounter: 80,
                    MonsterRngCounter: 30
                ),
                RunRng("seed-42", shuffleCount: 6),
                PlayerRng(42u, rewardsCount: 2),
                Tokens(),
                Stamp()
            )
        );

        // ---- F13: post-combat-end, defeat ----
        result.Add(
            new StateCodecFixture(
                "F13-combat-end-defeat",
                new CombatState(
                    TurnCounter: 8,
                    Phase: CombatPhase.CombatEnd,
                    Player: BuildPlayer(0, 70, 0),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(1, "CalcifiedCultist", 25, 50, 0, Attack(6))
                    ),
                    Energy: 0,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: CardPile.Empty,
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 0,
                    MonsterRngCounter: 0
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        // ---- F14: intent with applies-powers list (Buff Ritual) ----
        result.Add(
            new StateCodecFixture(
                "F14-monster-intent-with-applies-powers",
                new CombatState(
                    TurnCounter: 1,
                    Phase: CombatPhase.PlayerActing,
                    Player: BuildPlayer(70, 70, 0),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(
                            1,
                            "CalcifiedCultist",
                            50,
                            50,
                            0,
                            Buff(new MonsterIntentPower("RitualPower", 3))
                        )
                    ),
                    Energy: 3,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: CardPile.Empty,
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 0,
                    MonsterRngCounter: 0
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        // ---- F15: card with cost override (Snecko Eye / situational) ----
        result.Add(
            new StateCodecFixture(
                "F15-card-cost-override",
                new CombatState(
                    TurnCounter: 1,
                    Phase: CombatPhase.PlayerActing,
                    Player: BuildPlayer(70, 70, 0),
                    Enemies: ImmutableList<Creature>.Empty,
                    Energy: 3,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: Pile(Card(10, "StrikeSilent", upgrade: 0, costOverride: 0)),
                    HandPile: Pile(Card(20, "DefendSilent", upgrade: 1, costOverride: 2)),
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 0,
                    MonsterRngCounter: 0
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        // ---- F16: empty CombatState extremes — all piles empty, no enemies ----
        result.Add(
            new StateCodecFixture(
                "F16-extreme-empty",
                new CombatState(
                    TurnCounter: 0,
                    Phase: CombatPhase.CombatStart,
                    Player: new Creature(
                        CreatureId.Player,
                        "",
                        0,
                        0,
                        0,
                        ImmutableList<PowerInstance>.Empty,
                        null,
                        true
                    ),
                    Enemies: ImmutableList<Creature>.Empty,
                    Energy: 0,
                    BaseEnergyPerTurn: 0,
                    HandDrawSize: 0,
                    DrawPile: CardPile.Empty,
                    HandPile: CardPile.Empty,
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 0,
                    MonsterRngCounter: 0
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        // ---- F17: many powers on player (5+ different powers) ----
        result.Add(
            new StateCodecFixture(
                "F17-many-powers-on-player",
                new CombatState(
                    TurnCounter: 4,
                    Phase: CombatPhase.PlayerActing,
                    Player: BuildPlayer(
                        50,
                        70,
                        10,
                        new PowerInstance("StrengthPower", 3, new CreatureId(0u), false),
                        new PowerInstance("VulnerablePower", 2, new CreatureId(1u), false),
                        new PowerInstance("WeakPower", 1, new CreatureId(1u), true),
                        new PowerInstance("PoisonPower", 5, new CreatureId(1u), false),
                        new PowerInstance("RitualPower", 1, new CreatureId(0u), false)
                    ),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(1, "CalcifiedCultist", 30, 50, 0, Attack(6))
                    ),
                    Energy: 1,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: CardPile.Empty,
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 25,
                    MonsterRngCounter: 10
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        // ---- F18: high-counter RNG state (counters > 100) ----
        result.Add(
            new StateCodecFixture(
                "F18-high-counter-rng-state",
                new CombatState(
                    TurnCounter: 0,
                    Phase: CombatPhase.CombatStart,
                    Player: BuildPlayer(70, 70, 0),
                    Enemies: ImmutableList<Creature>.Empty,
                    Energy: 0,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: CardPile.Empty,
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 1000,
                    MonsterRngCounter: 999
                ),
                RunRng("seed-42", shuffleCount: 150),
                PlayerRng(42u, rewardsCount: 80),
                Tokens(),
                Stamp()
            )
        );

        // ---- F19: unicode manifest stamp (Greek / CJK / emoji) ----
        result.Add(
            new StateCodecFixture(
                "F19-unicode-stamp",
                new CombatState(
                    TurnCounter: 1,
                    Phase: CombatPhase.PlayerActing,
                    Player: BuildPlayer(70, 70, 0),
                    Enemies: ImmutableList<Creature>.Empty,
                    Energy: 3,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: CardPile.Empty,
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 0,
                    MonsterRngCounter: 0
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens(),
                StampUnicode()
            )
        );

        // ---- F20: empty-strings manifest stamp + long build-id ----
        result.Add(
            new StateCodecFixture(
                "F20-empty-then-long-stamp",
                new CombatState(
                    TurnCounter: 0,
                    Phase: CombatPhase.CombatStart,
                    Player: BuildPlayer(70, 70, 0),
                    Enemies: ImmutableList<Creature>.Empty,
                    Energy: 0,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: CardPile.Empty,
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 0,
                    MonsterRngCounter: 0
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens("A", "B"),
                StampLongBuildId()
            )
        );

        // ---- F21: ID/Name with unicode in Creature.Name ----
        result.Add(
            new StateCodecFixture(
                "F21-creature-name-unicode",
                new CombatState(
                    TurnCounter: 0,
                    Phase: CombatPhase.CombatStart,
                    Player: new Creature(
                        CreatureId.Player,
                        "Silent — δ Class",
                        70,
                        70,
                        0,
                        ImmutableList<PowerInstance>.Empty,
                        null,
                        true
                    ),
                    Enemies: ImmutableList.Create(
                        BuildEnemy(1, "Calcified-Cultist-世界", 50, 50, 0, null)
                    ),
                    Energy: 3,
                    BaseEnergyPerTurn: 3,
                    HandDrawSize: 5,
                    DrawPile: CardPile.Empty,
                    HandPile: CardPile.Empty,
                    DiscardPile: CardPile.Empty,
                    ExhaustPile: CardPile.Empty,
                    PlayerRngCounter: 0,
                    MonsterRngCounter: 0
                ),
                RunRng("seed-42"),
                PlayerRng(42u),
                Tokens(),
                Stamp()
            )
        );

        return result;
    }
}
