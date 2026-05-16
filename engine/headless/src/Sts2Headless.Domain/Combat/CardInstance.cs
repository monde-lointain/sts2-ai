namespace Sts2Headless.Domain.Combat;

/// <summary>
/// A runtime instance of a <see cref="Sts2Headless.Domain.Content.Models.CardModel"/>.
/// Distinct from the catalog model: there are many CardInstance objects per CardModel
/// (e.g., 5 Strikes in the starting deck → 5 CardInstance records all pointing at
/// <c>ModelId="StrikeSilent"</c>). Per-instance state — upgrade level, this-turn
/// cost overrides — lives here so the catalog model stays canonical/shared.
///
/// <para>
/// <b>Cheap-clone friendly (S17 preempt):</b> this is a <c>record</c> with init-only
/// properties and primitive fields only. <c>with</c>-expressions are O(1) copies.
/// </para>
///
/// <para>
/// <b>State-codec friendly (S7 preempt):</b> field order here is the byte-serialization
/// order S7 will use: <c>InstanceId</c> (u32), <c>ModelId</c> (length-prefixed UTF-8),
/// <c>UpgradeLevel</c> (i32), <c>CostOverride</c> (i32 with sentinel for null).
/// </para>
/// </summary>
/// <param name="InstanceId">
/// Unique per-combat id. Assigned at deck-construction time; used to refer to this
/// specific copy across pile transitions and S11 control-plane RPCs.
/// </param>
/// <param name="ModelId">
/// String id matching <c>CardCatalog</c>. Resolves to the canonical
/// <see cref="Sts2Headless.Domain.Content.Models.CardModel"/> at play time.
/// </param>
/// <param name="UpgradeLevel">
/// 0 = canonical (un-upgraded). Concrete cards apply per-level deltas through their
/// model's <c>Upgrade()</c> method, but the per-instance count lives here so two
/// copies of the same card can have different upgrade levels.
/// </param>
/// <param name="CostOverride">
/// One-turn cost override (e.g., Snecko Eye randomized costs); null means "use the
/// model's canonical cost". Smoke set never sets this; it exists so S12 doesn't have
/// to retrofit the type.
/// </param>
public sealed record CardInstance(
    uint InstanceId,
    string ModelId,
    int UpgradeLevel,
    int? CostOverride
);
