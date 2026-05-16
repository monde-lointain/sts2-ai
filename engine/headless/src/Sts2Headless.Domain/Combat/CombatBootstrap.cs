using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Encounters;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Bundle of S3/S5 content catalogs needed to bootstrap a combat. Constructed
/// once by the host's composition root and threaded through
/// <see cref="CombatEngine.StartCombat"/>.
/// </summary>
public sealed record CombatBootstrap(
    CardCatalog Cards,
    RelicCatalog Relics,
    PowerCatalog Powers,
    MonsterCatalog Monsters,
    EncounterCatalog Encounters
);
