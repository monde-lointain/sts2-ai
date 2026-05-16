using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Adapters.ControlPlane;

/// <summary>
/// Holds everything an orchestrator-driven Q1 session needs to roundtrip state
/// through the M1 codec, advance the M6 engine, and re-seed via M5.
///
/// <para>
/// A session is created at bootstrap (single-orchestrator pattern: one session
/// per Q1 process). RPC handlers <see cref="ControlPlaneRpcHandlers"/> mutate
/// the session in-place: <c>load_state</c> swaps out the <see cref="Context"/>
/// for a fresh one seeded from a deserialized blob; <c>set_seed</c> replaces
/// <see cref="Rng"/>; <c>apply_action</c> mutates <see cref="Context"/>.
/// </para>
///
/// <para>
/// <b>Catalogs are shared:</b> the S3 content catalogs (cards/relics/powers/
/// monsters/encounters) are constant for the lifetime of a process — they
/// never change across load_state. This lets us rebuild a fresh
/// <see cref="CombatContext"/> on load without re-resolving content.
/// </para>
/// </summary>
public sealed class ControlPlaneSession
{
    private CombatContext _context;
    private Rng _rng;

    /// <summary>The live combat context, replaced wholesale on <c>load_state</c>.</summary>
    public CombatContext Context => _context;

    /// <summary>The engine RNG, replaced wholesale on <c>set_seed</c>.</summary>
    public Rng Rng => _rng;

    /// <summary>Engine clock (shared across loads).</summary>
    public IClock Clock { get; }

    public CardCatalog Cards { get; }
    public RelicCatalog Relics { get; }
    public PowerCatalog Powers { get; }
    public MonsterCatalog Monsters { get; }
    public EncounterCatalog Encounters { get; }

    /// <summary>RunRng — carried alongside state for codec roundtrip.</summary>
    public RunRngSet RunRng { get; private set; }

    /// <summary>PlayerRng — carried alongside state for codec roundtrip.</summary>
    public PlayerRngSet PlayerRng { get; private set; }

    /// <summary>TokenMap — carried alongside state for codec roundtrip.</summary>
    public TokenMap Tokens { get; private set; }

    /// <summary>Manifest stamp — constant for the process; informational.</summary>
    public ManifestStamp Stamp { get; }

    public ControlPlaneSession(
        CombatContext context,
        Rng rng,
        IClock clock,
        CardCatalog cards,
        RelicCatalog relics,
        PowerCatalog powers,
        MonsterCatalog monsters,
        EncounterCatalog encounters,
        RunRngSet runRng,
        PlayerRngSet playerRng,
        TokenMap tokens,
        ManifestStamp stamp
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(relics);
        ArgumentNullException.ThrowIfNull(powers);
        ArgumentNullException.ThrowIfNull(monsters);
        ArgumentNullException.ThrowIfNull(encounters);
        ArgumentNullException.ThrowIfNull(runRng);
        ArgumentNullException.ThrowIfNull(playerRng);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(stamp);

        _context = context;
        _rng = rng;
        Clock = clock;
        Cards = cards;
        Relics = relics;
        Powers = powers;
        Monsters = monsters;
        Encounters = encounters;
        RunRng = runRng;
        PlayerRng = playerRng;
        Tokens = tokens;
        Stamp = stamp;
    }

    /// <summary>
    /// Replace the held state with a freshly built <see cref="CombatContext"/>
    /// whose <see cref="CombatContext.State"/> equals <paramref name="state"/>.
    /// The new context uses the existing <see cref="Rng"/> and
    /// <see cref="Clock"/> + the shared catalogs — content references survive
    /// across load.
    /// </summary>
    public void ReplaceState(CombatState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        // B.1-alpha-T2 (RC-3): CombatContext now takes the full RunRngSet
        // and routes `ctx.Rng` to `.Shuffle` internally. The session's RunRng
        // is the source of truth; the legacy `_rng` field is kept as the
        // back-compat handle that mirrors `RunRng.Shuffle`.
        _context = new CombatContext(
            state,
            RunRng,
            Clock,
            Cards,
            Relics,
            Powers,
            Monsters,
            Encounters
        );
    }

    /// <summary>
    /// Replace held RunRng / PlayerRng / Tokens (carried alongside state for
    /// codec roundtrip). Used by <c>load_state</c>.
    /// </summary>
    public void ReplaceCodecCarriers(RunRngSet runRng, PlayerRngSet playerRng, TokenMap tokens)
    {
        ArgumentNullException.ThrowIfNull(runRng);
        ArgumentNullException.ThrowIfNull(playerRng);
        ArgumentNullException.ThrowIfNull(tokens);
        RunRng = runRng;
        PlayerRng = playerRng;
        Tokens = tokens;
    }

    /// <summary>
    /// Replace the engine's run-scoped RNG fan-out. The current
    /// <see cref="Context"/> is rebuilt with the new <see cref="RunRng"/> so
    /// every subsequent engine operation consumes from the new streams.
    /// The session's legacy <see cref="Rng"/> handle is re-derived from the
    /// new set's <c>.Shuffle</c> bucket.
    ///
    /// <para>
    /// <b>B.1-alpha-T2 (RC-3):</b> the upstream-correct entry point for
    /// <c>set_seed</c>; pass a fresh <see cref="RunRngSet"/> built from
    /// <c>$"seed-{N}"</c> so the new master seed feeds all twelve subsystem
    /// buckets uniformly (HP rolls, deck shuffles, monster AI, etc.).
    /// </para>
    /// </summary>
    public void ReplaceRunRng(RunRngSet newRunRng)
    {
        ArgumentNullException.ThrowIfNull(newRunRng);
        RunRng = newRunRng;
        _rng = newRunRng.Shuffle;
        _context = new CombatContext(
            _context.State,
            RunRng,
            Clock,
            Cards,
            Relics,
            Powers,
            Monsters,
            Encounters
        );
    }

    /// <summary>
    /// Replace the engine RNG. Legacy entry point preserved for callers that
    /// hold an <see cref="Rng"/> directly (e.g., probe harnesses constructing
    /// RNG without a run-scope fan-out).
    ///
    /// <para>
    /// <b>B.1-alpha-T2 (RC-3) caveat:</b> after the RC-3 refactor the engine
    /// consumes via <see cref="RunRng"/>; this method DOES NOT rebuild the
    /// context with the new <paramref name="newRng"/> — the run-scope set
    /// remains the source of truth. To reseed the engine's bucket fan-out,
    /// callers must use <see cref="ReplaceRunRng(RunRngSet)"/>.
    /// </para>
    /// </summary>
    public void ReplaceRng(Rng newRng)
    {
        ArgumentNullException.ThrowIfNull(newRng);
        _rng = newRng;
        _context = new CombatContext(
            _context.State,
            RunRng,
            Clock,
            Cards,
            Relics,
            Powers,
            Monsters,
            Encounters
        );
    }
}
