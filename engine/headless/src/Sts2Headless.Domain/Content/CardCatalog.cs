namespace Sts2Headless.Domain.Content;

/// <summary>
/// Card registry. Thin alias over <see cref="ContentTable{TId, TModel}"/> so call sites
/// read as <c>CardCatalog</c> rather than <c>ContentTable&lt;string, ICardModel&gt;</c>.
/// Concrete <see cref="ICardModel"/> implementations land in S5 / S12.
/// </summary>
public class CardCatalog : ContentTable<string, ICardModel> { }

/// <summary>
/// Marker for card-shaped content models. Concrete <c>CardModel</c> abstract base
/// arrives in S5 (M6c smoke content).
/// </summary>
public interface ICardModel : IContentModel { }
