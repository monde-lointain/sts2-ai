using Sts2Headless.Domain.Content.Monsters;

namespace Sts2Headless.Domain.Content.Encounters;

/// <summary>
/// Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Models.Encounters.CultistsNormal</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Encounters/CultistsNormal.cs):
/// the first-act normal cultist encounter. Spawns <see cref="CalcifiedCultist"/>
/// then <see cref="DampCultist"/>. Room type is upstream <c>RoomType.Monster</c>
/// (encoded by the catalog shape, not surfaced here).
/// </summary>
public sealed class CultistsNormal : EncounterModel
{
    public const string CanonicalId = "CultistsNormal";

    public CultistsNormal() : base(
        id: CanonicalId,
        monsterIds: new[]
        {
            CalcifiedCultist.CanonicalId,
            DampCultist.CanonicalId,
        })
    { }
}
