namespace Sts2Headless.Domain.Content;

/// <summary>
/// Monster registry. See <see cref="CardCatalog"/> for shape rationale.
/// </summary>
public class MonsterCatalog : ContentTable<string, IMonsterModel> { }

/// <summary>Marker for monster-shaped content models. Filled in at S5.</summary>
public interface IMonsterModel : IContentModel { }
