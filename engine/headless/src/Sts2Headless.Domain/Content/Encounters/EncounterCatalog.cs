using Sts2Headless.Domain.Content;

namespace Sts2Headless.Domain.Content.Encounters;

/// <summary>
/// Encounter registry. Same shape as the five Q4-coverage catalogs in
/// <c>Sts2Headless.Domain.Content</c>: a thin alias over
/// <see cref="ContentTable{TId, TModel}"/> so call sites read as
/// <c>EncounterCatalog</c> rather than the long generic form.
///
/// <para>
/// <b>Q4 manifest coverage:</b> S5 does NOT extend the Q4 manifest with an
/// encounters bucket because the S5 prompt only sanctions the existing five
/// (cards/relics/powers/monsters/potions). Encounter coverage will arrive in
/// S12 / Phase-2 when the Q4 schema bumps to add the bucket. For now the
/// encounter is registered but not coverage-gated.
/// </para>
/// </summary>
public class EncounterCatalog : ContentTable<string, IEncounterModel>
{
}
