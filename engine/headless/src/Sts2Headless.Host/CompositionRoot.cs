using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Host;

/// <summary>
/// Wires the Q1 module graph for a single combat. Constructs S3/S5 catalogs
/// via <see cref="SmokeContent"/>, S1 RNG + clock, then S6
/// <see cref="CombatEngine.StartCombat"/> to bootstrap a live
/// <see cref="CombatContext"/>. Returns the bundle of services M9's main loop
/// needs (see <see cref="CompositionRootBundle"/>).
///
/// <para>
/// Topological order matches the M9 spec: M5 (Determinism Kernel) + M7
/// (Content Catalog) → M6c (Content models) → M6a (Combat domain). The Host's
/// composition root is the only seam that knows the whole module graph.
/// </para>
///
/// <para>
/// <b>Character / deck / encounter mapping (Phase 1):</b> only the
/// <c>silent + starter</c> deck against <c>cultists_normal</c> is supported.
/// CLI ids are case-insensitive snake_case; the resolver normalises and maps
/// to the catalogs' PascalCase canonical ids (e.g.
/// <c>ring_of_the_snake</c> → <see cref="RingOfTheSnake.CanonicalId"/>).
/// </para>
/// </summary>
public static class CompositionRoot
{
    /// <summary>Bundle returned by <see cref="Build(CliArgs)"/>.
    ///
    /// <para>
    /// <b>B.1-alpha-T1 (RC-2):</b> the kernel RNG is no longer constructed from
    /// the raw <c>--seed N</c> uint. Instead a <see cref="RunRngSet"/> is
    /// instantiated with the upstream-shaped string seed
    /// <c>$"seed-{N}"</c>; the <c>RunRngSet.Seed</c> is the M5-byte-exact
    /// <see cref="StringHelpers.GetDeterministicHashCode(string)"/> of that
    /// string. The <see cref="RunRng"/> property exposes the full set;
    /// <see cref="Rng"/> exposes the single bucket currently fed to the
    /// combat engine (the Shuffle bucket — chosen so existing tests keyed on
    /// deck-order determinism remain stable through T1; B.1-alpha-T2 / RC-3
    /// will split HP rolls onto <c>.Niche</c> and deck shuffles onto
    /// <c>.Shuffle</c> independently).
    /// </para>
    /// </summary>
    public sealed record CompositionRootBundle(
        CombatContext Context,
        LogicalClock Clock,
        Rng Rng,
        RunRngSet RunRng,
        CardCatalog Cards,
        RelicCatalog Relics,
        PowerCatalog Powers,
        MonsterCatalog Monsters,
        EncounterCatalog Encounters
    );

    /// <summary>
    /// Build the module graph and bootstrap combat. Throws
    /// <see cref="CompositionException"/> with a clear message on unsupported
    /// character/deck/encounter ids or other wiring failures.
    ///
    /// <para>
    /// <b>Catalog selection (S13):</b> by default the smoke set is used (so the
    /// S8-T7 golden test stays pinned). When <see cref="CliArgs.Encounter"/>
    /// resolves to anything other than <c>cultists_normal</c>, the full
    /// <see cref="Phase1Content"/> catalogs are loaded automatically — they
    /// preserve smoke-id ordering and append the S12 expansion, so the smoke
    /// encounter byte-blob stays identical.
    /// </para>
    /// </summary>
    public static CompositionRootBundle Build(CliArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // --- Catalog selection -----------------------------------------
        // Smoke catalogs preserve the S8-T7 golden SHA. The Phase-1 catalogs
        // include all 22 encounters; they're activated when the requested
        // encounter id isn't the smoke one.
        bool useFullContent = !IsSmokeEncounter(args.Encounter);

        // --- Catalogs (S3 + S5 / S12) ----------------------------------
        CardCatalog cards;
        RelicCatalog relics;
        PowerCatalog powers;
        MonsterCatalog monsters;
        EncounterCatalog encounters;
        if (useFullContent)
        {
            cards = Phase1Content.BuildCardCatalog();
            relics = Phase1Content.BuildRelicCatalog();
            powers = Phase1Content.BuildPowerCatalog();
            monsters = Phase1Content.BuildMonsterCatalog();
            encounters = Phase1Content.BuildEncounterCatalog();
        }
        else
        {
            cards = SmokeContent.BuildCardCatalog();
            relics = SmokeContent.BuildRelicCatalog();
            powers = SmokeContent.BuildPowerCatalog();
            monsters = SmokeContent.BuildMonsterCatalog();
            encounters = SmokeContent.BuildEncounterCatalog();
        }

        // --- Determinism kernel (S1) -----------------------------------
        // B.1-alpha-T1 (RC-2): derive the master seed via the upstream-byte-
        // exact string-hash protocol. Upstream's CompositionRoot equivalent
        // (RunBootstrap → new RunRngSet($"seed-{N}")) keys every subsystem
        // RNG off StringHelpers.GetDeterministicHashCode($"seed-{N}").
        // B.1-alpha-T2 (RC-3): pass the full RunRngSet through the engine so
        // HP rolls route to .Niche and deck shuffles to .Shuffle (per
        // upstream CombatState.CreateCreature:133 + CombatManager:188).
        var clock = new LogicalClock();
        string stringSeed = $"seed-{args.Seed}";
        var runRng = new RunRngSet(stringSeed);
        // Kept for the bundle's legacy `Rng` field — points at the .Shuffle
        // bucket (the most common in-combat consumer). Bucket-aware callers
        // route through `bundle.RunRng` directly.
        Rng rng = runRng.Shuffle;

        // --- Resolve content ids from CLI tokens -----------------------
        IEncounterModel encounter = ResolveEncounter(args.Encounter, encounters);
        IReadOnlyList<string> relicIds = ResolveRelics(args.Relics, relics);
        IReadOnlyList<CardInstance> deck = ResolveDeck(args.Character, args.Deck);

        // --- Bootstrap combat (S6) -------------------------------------
        if (args.Ascension != 0)
        {
            // Phase 1 supports A0 only; the smoke content has no ascension
            // modifiers wired (S12 introduces them).
            throw new CompositionException(
                $"--ascension {args.Ascension} unsupported (Phase 1 supports A0 only)."
            );
        }

        var bootstrap = new CombatBootstrap(cards, relics, powers, monsters, encounters);
        var playerSpec = new PlayerSpec(RelicIds: relicIds, Deck: deck);
        CombatContext ctx = CombatEngine.StartCombat(
            encounter,
            bootstrap,
            playerSpec,
            runRng,
            clock
        );

        return new CompositionRootBundle(
            ctx,
            clock,
            rng,
            runRng,
            cards,
            relics,
            powers,
            monsters,
            encounters
        );
    }

    // === Resolvers ========================================================

    /// <summary>
    /// Resolve a CLI encounter token (snake_case OR PascalCase canonical id,
    /// case-insensitive) to the canonical encounter id registered in
    /// <paramref name="encounters"/>. The smoke encounter is handled by a fast
    /// path; all other ids are looked up by case-insensitive match against the
    /// registered catalog. This keeps the smoke CLI surface intact while
    /// extending to all 22 Phase-1 encounters for the S13 probe.
    /// </summary>
    private static IEncounterModel ResolveEncounter(string cliToken, EncounterCatalog encounters)
    {
        string normalised = NormaliseToken(cliToken);
        if (normalised == "cultists_normal")
        {
            return (IEncounterModel)encounters.Get(CultistsNormal.CanonicalId);
        }
        // Case-insensitive scan over the registered ids. This is O(N) but N=22
        // for Phase 1 — well within the host-startup budget.
        foreach (string id in encounters.EnumerateIds())
        {
            if (string.Equals(id, cliToken, System.StringComparison.OrdinalIgnoreCase))
            {
                return (IEncounterModel)encounters.Get(id);
            }
        }
        throw new CompositionException(
            $"--encounter '{cliToken}': unknown encounter id (must match one of "
                + $"{string.Join(", ", encounters.EnumerateIds())})."
        );
    }

    /// <summary>
    /// True iff <paramref name="cliToken"/> resolves to the smoke encounter.
    /// Used to select the smoke vs. full Phase-1 catalogs.
    /// </summary>
    private static bool IsSmokeEncounter(string cliToken) =>
        NormaliseToken(cliToken) == "cultists_normal";

    /// <summary>
    /// Resolve a list of CLI relic tokens to canonical ids registered in
    /// <paramref name="relics"/>. Preserves the user's priority order.
    /// </summary>
    private static IReadOnlyList<string> ResolveRelics(
        IReadOnlyList<string> cliTokens,
        RelicCatalog relics
    )
    {
        var resolved = new List<string>(cliTokens.Count);
        foreach (string token in cliTokens)
        {
            string canonical = NormaliseToken(token) switch
            {
                "ring_of_the_snake" => RingOfTheSnake.CanonicalId,
                "anchor" => Anchor.CanonicalId,
                "vajra" => Vajra.CanonicalId,
                "bag_of_preparation" => BagOfPreparation.CanonicalId,
                "blood_vial" => BloodVial.CanonicalId,
                // Stream-B-T2 additions: A0-pool extensions.
                "bag_of_marbles" => BagOfMarbles.CanonicalId,
                "bronze_scales" => BronzeScales.CanonicalId,
                _ => throw new CompositionException($"--relics: unknown relic id '{token}'."),
            };
            // Guard against the unlikely case that the catalog hasn't registered the id.
            _ = relics.Get(canonical);
            resolved.Add(canonical);
        }
        return resolved;
    }

    /// <summary>
    /// Resolve a (character, deck-preset) pair to a concrete starting deck.
    /// Phase 1: <c>silent</c> + <c>starter</c> = 5x Strike, 5x Defend, 1x
    /// Neutralize, 1x Survivor. 12 cards total. RC-1 (B.1-beta-T2) removed
    /// the prior DeadlyPoison + Backflip invented additions to align with
    /// upstream <c>~/development/projects/godot/sts2/src/Core/Models/Characters/Silent.cs</c>'s
    /// <c>StartingDeck</c> (5 StrikeSilent + 5 DefendSilent + Neutralize + Survivor).
    /// Stable instance ids start at 100 so they don't collide with creature
    /// ids (which start at 0 / 1+).
    /// </summary>
    private static IReadOnlyList<CardInstance> ResolveDeck(string character, string deck)
    {
        string normalisedChar = NormaliseToken(character);
        string normalisedDeck = NormaliseToken(deck);
        if (normalisedChar != "silent")
        {
            throw new CompositionException(
                $"--character '{character}': unsupported (Phase 1 supports 'silent' only)."
            );
        }
        if (normalisedDeck != "starter")
        {
            throw new CompositionException(
                $"--deck '{deck}': unsupported (Phase 1 supports 'starter' only)."
            );
        }

        var list = new List<CardInstance>(12);
        uint id = 100u;
        for (int i = 0; i < 5; i++)
        {
            list.Add(new CardInstance(id++, StrikeSilent.CanonicalId, 0, null));
        }
        for (int i = 0; i < 5; i++)
        {
            list.Add(new CardInstance(id++, DefendSilent.CanonicalId, 0, null));
        }
        list.Add(new CardInstance(id++, Neutralize.CanonicalId, 0, null));
        list.Add(new CardInstance(id++, Survivor.CanonicalId, 0, null));
        return list;
    }

    /// <summary>
    /// Lowercase + trim. The host accepts user-friendly snake_case ids that
    /// don't need to mirror the catalogs' PascalCase canonical ids. This keeps
    /// the CLI surface stable even if the upstream renames a canonical id.
    /// </summary>
    private static string NormaliseToken(string raw) =>
        raw?.Trim().ToLowerInvariant() ?? string.Empty;
}

/// <summary>
/// Raised by <see cref="CompositionRoot.Build(CliArgs)"/> when CLI ids don't
/// resolve or unsupported combinations are requested. Treated by
/// <see cref="Program.Main(string[])"/> as a fatal CLI error
/// (exit code <see cref="Program.ExitError"/>).
/// </summary>
public sealed class CompositionException : Exception
{
    public CompositionException(string message)
        : base(message) { }

    public CompositionException(string message, Exception inner)
        : base(message, inner) { }
}
