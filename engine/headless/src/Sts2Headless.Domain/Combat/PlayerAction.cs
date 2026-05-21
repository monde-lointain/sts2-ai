namespace Sts2Headless.Domain.Combat;

/// <summary>
/// A discrete action the player can take during the player-acting phase. C#
/// records modelling a discriminated union: <see cref="PlayCard"/> (one per
/// playable card+target combo) or <see cref="EndTurn"/> (sentinel).
///
/// <para>
/// Used by:
/// </para>
/// <list type="bullet">
///   <item><see cref="LegalActions.Enumerate"/> — returns the set of legal
///         actions for the current state.</item>
///   <item>M6d's hook-protocol-mask emission — will translate this enumeration
///         to an indexed bitmask of playable cards-per-target.</item>
///   <item>S11 control plane's <c>apply_action</c> RPC — the on-wire shape will
///         be a token id matching this enumeration's index.</item>
/// </list>
///
/// <para>
/// <b>Equality:</b> records give value-equality automatically; tests can
/// compare lists of PlayerActions directly. <c>PlayCard</c>'s nullable target
/// participates in equality so two plays of the same card at different
/// targets compare unequal.
/// </para>
/// </summary>
public abstract record PlayerAction
{
    /// <summary>Force <see cref="PlayerAction"/> to be a closed-type union — only the nested types subclass.</summary>
    private PlayerAction() { }

    /// <summary>"Play this card (optionally at this enemy)." Sealed; only one subclass per discriminator.</summary>
    public sealed record PlayCard(uint CardInstanceId, CreatureId? TargetEnemyId) : PlayerAction;

    /// <summary>"End my turn." Sentinel; no payload.</summary>
    public sealed record EndTurn : PlayerAction
    {
        /// <summary>Singleton instance — there's only ever one EndTurn.</summary>
        public static readonly EndTurn Instance = new();
    }
}
