namespace Sts2Headless.Domain.Content;

/// <summary>
/// Potion registry. See <see cref="CardCatalog"/> for shape rationale.
/// </summary>
public class PotionCatalog : ContentTable<string, IPotionModel>
{
}

/// <summary>Marker for potion-shaped content models. Filled in at S5 / S12.</summary>
public interface IPotionModel : IContentModel
{
}
