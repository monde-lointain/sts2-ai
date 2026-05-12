using Sts2Headless.Adapters.ControlPlane;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Adapters.ControlPlane;

/// <summary>
/// Shared test helper: bootstrap a <see cref="ControlPlaneSession"/> for the
/// canonical Phase-1 smoke combat (Silent + RingOfTheSnake vs CultistsNormal,
/// seed=42). Used by every T3-T7 test so we exercise the same bootstrap path
/// as the Host's CompositionRoot.
/// </summary>
internal static class SessionFactory
{
    public static ManifestStamp DefaultStamp()
    {
        // Stable stamp — identical across runs so save/load byte equality is
        // testable. ContentHash uses the smoke catalog's id set.
        byte[] contentHash = ManifestStamp.ContentHashFromIds(new[]
        {
            "Acrobatics", "Anchor", "Backflip", "BagOfPreparation",
            "BloodVial", "CalcifiedCultist", "CultistsNormal",
            "DampCultist", "DeadlyPoison", "DefendSilent", "DodgeAndRoll",
            "Neutralize", "PoisonPower", "RingOfTheSnake", "RitualPower",
            "Slice", "StrengthPower", "StrikeSilent", "Survivor", "Vajra",
            "VulnerablePower", "WeakPower",
        });
        return new ManifestStamp(
            GitSha: "test-controlplane",
            BuildId: "Q1-Phase1-S11",
            ContentHash: contentHash);
    }

    public static ControlPlaneSession BootSmokeSession(uint seed = 42u)
    {
        CardCatalog cards = SmokeContent.BuildCardCatalog();
        RelicCatalog relics = SmokeContent.BuildRelicCatalog();
        PowerCatalog powers = SmokeContent.BuildPowerCatalog();
        MonsterCatalog monsters = SmokeContent.BuildMonsterCatalog();
        EncounterCatalog encounters = SmokeContent.BuildEncounterCatalog();

        var clock = new LogicalClock();
        // B.1-alpha-T2 (RC-3): a single RunRngSet drives the engine.
        // The session-level `Rng` field tracks the .Shuffle bucket for
        // backward-compat callers that ask for `session.Rng` directly.
        var runRng = new RunRngSet($"seed-{seed}");
        Rng rng = runRng.Shuffle;

        var deck = new List<CardInstance>();
        uint id = 100u;
        for (int i = 0; i < 5; i++) deck.Add(new CardInstance(id++, StrikeSilent.CanonicalId, 0, null));
        for (int i = 0; i < 5; i++) deck.Add(new CardInstance(id++, DefendSilent.CanonicalId, 0, null));
        deck.Add(new CardInstance(id++, Neutralize.CanonicalId, 0, null));
        deck.Add(new CardInstance(id++, Survivor.CanonicalId, 0, null));
        deck.Add(new CardInstance(id++, DeadlyPoison.CanonicalId, 0, null));
        deck.Add(new CardInstance(id++, Backflip.CanonicalId, 0, null));

        var bootstrap = new CombatBootstrap(cards, relics, powers, monsters, encounters);
        var playerSpec = new PlayerSpec(
            RelicIds: new[] { RingOfTheSnake.CanonicalId },
            Deck: deck);
        CombatContext ctx = CombatEngine.StartCombat(
            (IEncounterModel)encounters.Get(CultistsNormal.CanonicalId),
            bootstrap,
            playerSpec,
            runRng,
            clock);

        TokenMap tokens = new();
        // Pre-populate token map with the smoke ids so round-trips have content.
        tokens.GetOrAddId(StrikeSilent.CanonicalId);
        tokens.GetOrAddId(DefendSilent.CanonicalId);
        tokens.GetOrAddId(Neutralize.CanonicalId);
        tokens.GetOrAddId(Survivor.CanonicalId);

        PlayerRngSet playerRng = new(seed);

        return new ControlPlaneSession(
            context: ctx,
            rng: rng,
            clock: clock,
            cards: cards,
            relics: relics,
            powers: powers,
            monsters: monsters,
            encounters: encounters,
            runRng: runRng,
            playerRng: playerRng,
            tokens: tokens,
            stamp: DefaultStamp());
    }
}
