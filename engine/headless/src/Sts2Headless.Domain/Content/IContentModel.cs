namespace Sts2Headless.Domain.Content;

/// <summary>
/// Minimum-surface contract for anything that lives in an M7 catalog: a stable
/// string id that survives across patches per pipeline ADR-003.
///
/// <para>
/// S3 ships this interface plus empty catalog instances. S5 (M6c smoke content)
/// introduces concrete model bases that implement it — <c>CardModel</c>,
/// <c>RelicModel</c>, <c>PowerModel</c>, <c>MonsterModel</c>, <c>PotionModel</c>.
/// S12 fills out the full Silent content surface.
/// </para>
/// </summary>
public interface IContentModel
{
    /// <summary>
    /// Stable string id matching upstream <c>ModelId.Value</c>. Must be unique within
    /// its catalog. Used as the Q4 token-map key (see <see cref="TokenMap"/>).
    /// </summary>
    string Id { get; }
}
