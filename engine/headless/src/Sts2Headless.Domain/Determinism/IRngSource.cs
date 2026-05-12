namespace Sts2Headless.Domain.Determinism;

/// <summary>
/// Port for the determinism kernel's RNG surface. Consumers in M6a / M6b /
/// M6c / M6d / M7 depend on this interface rather than the concrete
/// <see cref="Rng"/> class so the boundary stays clean per Q1-ADR-001 and so
/// tests can plug in fakes / replay shims without touching production code.
///
/// The shape mirrors upstream <c>MegaCrit.Sts2.Core.Random.Rng</c> 1:1 — every
/// public primitive on <see cref="Rng"/> is here. <see cref="Counter"/> and
/// <see cref="Seed"/> are exposed so M5's <c>IRngStateSerializer</c> can
/// roundtrip state without reflecting over the concrete class.
///
/// Implementations MUST be counter-monotonic per the upstream contract: every
/// scalar primitive call advances <see cref="Counter"/> by exactly one (with
/// the pinned exception of <see cref="NextGaussianInt"/>, which intentionally
/// does NOT advance — this is a documented upstream quirk preserved by the
/// port).
/// </summary>
public interface IRngSource
{
    uint Seed { get; }
    int Counter { get; }

    void FastForwardCounter(int targetCount);

    bool NextBool();
    int NextInt(int maxExclusive = int.MaxValue);
    int NextInt(int minInclusive, int maxExclusive);

    uint NextUnsignedInt(uint maxExclusive = uint.MaxValue);
    uint NextUnsignedInt(uint minInclusive, uint maxExclusive);

    float NextFloat(float max = 1f);
    float NextFloat(float min, float max);

    double NextDouble();
    double NextDouble(double min, double max);

    float NextGaussianFloat(float mean = 0f, float stdDev = 1f, float min = 0f, float max = 1f);
    double NextGaussianDouble(double mean = 0.0, double stdDev = 1.0, double min = 0.0, double max = 1.0);

    /// <summary>Pinned upstream quirk: does NOT advance <see cref="Counter"/>.</summary>
    int NextGaussianInt(int mean, int stdDev, int min, int max);

    T? NextItem<T>(IEnumerable<T> items);
    void Shuffle<T>(IList<T> list);
}
