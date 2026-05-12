namespace Sts2Headless.Domain.Content;

/// <summary>
/// Relic registry. See <see cref="CardCatalog"/> for shape rationale.
/// </summary>
public class RelicCatalog : ContentTable<string, IRelicModel>
{
}

/// <summary>Marker for relic-shaped content models. Filled in at S5.</summary>
public interface IRelicModel : IContentModel
{
}
