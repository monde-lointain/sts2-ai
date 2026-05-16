namespace Sts2Headless.Domain.Content;

/// <summary>
/// Power registry. See <see cref="CardCatalog"/> for shape rationale.
/// </summary>
public class PowerCatalog : ContentTable<string, IPowerModel> { }

/// <summary>Marker for power-shaped content models. Filled in at S5.</summary>
public interface IPowerModel : IContentModel { }
