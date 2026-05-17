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
}
