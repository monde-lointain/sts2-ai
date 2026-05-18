using System.Collections.Generic;
using System.Linq;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Domain.Content.Encounters;

/// <summary>
/// Marker plus minimum surface for catalog entries. S5 ships only the smoke
/// encounter (<see cref="Encounters.CultistsNormal"/>); S12 / Phase-2 expands.
/// Lives alongside the other <c>IContentModel</c> markers so future
/// <c>EncounterCatalog</c> work follows the same pattern.
/// </summary>
public interface IEncounterModel : IContentModel
{
    /// <summary>Monster ids (in spawn order) the encounter generates.</summary>
    IReadOnlyList<string> MonsterIds { get; }
}

/// <summary>
/// Base for encounter content. Matches the data shape of upstream
/// <c>MegaCrit.Sts2.Core.Models.EncounterModel</c>
/// (~/development/projects/godot/sts2/src/Core/Models/EncounterModel.cs:302):
/// id + ordered monster spawn list. Behavior (room-type filtering, ascension
/// variants, per-encounter setup) lives in S12.
/// </summary>
public abstract class EncounterModel : IEncounterModel
{
    public string Id { get; }
    public IReadOnlyList<string> MonsterIds { get; }

    /// <summary>
    /// Construct with a stable id and the ordered list of monster ids the encounter
    /// spawns. Order matches upstream's <c>GenerateMonsters</c> output order.
    /// </summary>
    protected EncounterModel(string id, IEnumerable<string> monsterIds)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new System.ArgumentException("EncounterModel id must be non-empty.", nameof(id));
        }
        System.ArgumentNullException.ThrowIfNull(monsterIds);
        List<string> ids = monsterIds.ToList();
        if (ids.Count == 0)
        {
            throw new System.ArgumentException(
                $"EncounterModel '{id}': MonsterIds must be non-empty.",
                nameof(monsterIds)
            );
        }
        foreach (string monsterId in ids)
        {
            if (string.IsNullOrWhiteSpace(monsterId))
            {
                throw new System.ArgumentException(
                    $"EncounterModel '{id}': empty monster id in spawn list.",
                    nameof(monsterIds)
                );
            }
        }
        Id = id;
        MonsterIds = ids;
    }

    /// <summary>
    /// The string key used when seeding the per-encounter Rng via
    /// <see cref="RunRngSet.ForEncounter"/>. Matches upstream's
    /// <c>EncounterModel.Id.Entry</c> (the slugified class name, e.g.
    /// <c>"SLIMES_WEAK"</c>) for encounters whose <c>GenerateMonsters(Rng)</c>
    /// override must produce byte-identical output with upstream.
    ///
    /// <para>
    /// Defaults to <see cref="Id"/> (the Q1 canonical encounter id). Encounters
    /// that drive upstream's Rng-based monster selection MUST override this to
    /// return the upstream slugified type name so the seed formula
    /// <c>(int)Seed + totalFloor + hash(key)</c> produces the same uint as upstream.
    /// </para>
    /// </summary>
    public virtual string EncounterRngKey => Id;

    /// <summary>
    /// B.1-ε scaffold: additive virtual overload that receives a per-encounter
    /// <see cref="Rng"/> instance. Default implementation returns the static
    /// <see cref="MonsterIds"/> spawn list WITHOUT consuming the rng — all 22
    /// existing encounters keep identical behaviour.
    ///
    /// <para>
    /// Wave 3.5 overrides this in <c>SmallSlimes</c> / <c>MediumSlimes</c> to
    /// use <paramref name="rng"/> for variant selection. Callers derive the rng
    /// via <see cref="RunRngSet.ForEncounter"/>.
    /// </para>
    /// </summary>
    public virtual IReadOnlyList<string> GenerateMonsters(Rng rng) => MonsterIds;

    /// <summary>
    /// Wave-24/K.q1: additive virtual that returns the spawn list paired with
    /// optional per-slot initial-move overrides. Default implementation wraps
    /// <see cref="GenerateMonsters"/> with null overrides so legacy encounters
    /// that override only <see cref="GenerateMonsters"/> continue to work
    /// unchanged — the same <paramref name="rng"/> instance is threaded through
    /// without any extra tick.
    ///
    /// <para>
    /// Encounters with fixed per-slot initial moves (e.g. <c>NibbitsNormal</c>
    /// where slot-0 starts SLICE and slot-1 starts HISS) override this method
    /// instead of <see cref="GenerateMonsters"/>. The returned
    /// <c>InitialMoveIdOverride</c> is <see langword="null"/> for slots that
    /// use the monster model's own <c>InitialMoveId</c>.
    /// </para>
    /// </summary>
    public virtual IReadOnlyList<(string MonsterId, string? InitialMoveIdOverride)>
        GenerateMonstersWithMoves(Rng rng) =>
        GenerateMonsters(rng).Select(id => (id, (string?)null)).ToList();
}
