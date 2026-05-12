// Rng.cs is the M5 Determinism Kernel's underlying PRNG implementation; System.Random is
// the algorithm. The BannedApiAnalyzers ban (RS0030) on System.Random is suppressed for
// THIS FILE ONLY — System.Random remains banned in every other file in
// Sts2Headless.Domain. Any new "I just need a quick random number" usage belongs behind
// M5's IRngSource port, not here.
#pragma warning disable RS0030

namespace Sts2Headless.Domain.Determinism;

/// <summary>
/// Deterministic random number generator — direct port of upstream
/// <c>MegaCrit.Sts2.Core.Random.Rng</c>. Wraps <see cref="System.Random"/> seeded with an
/// explicit 32-bit seed. On .NET 9 the seeded <see cref="System.Random"/> is the Xoshiro256**
/// algorithm and produces byte-identical outputs across processes / OSes for the same seed.
/// This class is the origin of all determinism in the simulator pipeline (M5 — Determinism
/// Kernel); altering its public surface or the underlying PRNG will break replay parity.
///
/// Notes vs upstream:
///   * <c>Rng.Chaotic</c> is intentionally NOT ported — it depends on
///     <c>DateTimeOffset.Now</c>, which is banned by the determinism analyzer and defeats
///     replay. Callers needing a "chaotic" RNG must supply an explicit seed.
///   * <see cref="NextGaussianInt"/> does NOT increment <see cref="Counter"/>; this is a
///     pinned upstream quirk, not a bug to fix here.
/// </summary>
public class Rng : IRngSource
{
    private readonly System.Random _random;

    public int Counter { get; private set; }

    public uint Seed { get; }

    public Rng(uint seed = 0u, int counter = 0)
    {
        Counter = 0;
        Seed = seed;
        _random = new System.Random((int)seed);
        FastForwardCounter(counter);
    }

    public Rng(uint seed, string name)
        : this(seed + (uint)StringHelpers.GetDeterministicHashCode(name))
    {
    }

    public void FastForwardCounter(int targetCount)
    {
        if (Counter > targetCount)
        {
            throw new InvalidOperationException(
                $"Cannot fast-forward an Rng counter to a lower number (current = {Counter}, target = {targetCount})");
        }
        while (Counter < targetCount)
        {
            Counter++;
            _random.Next();
        }
    }

    public bool NextBool()
    {
        Counter++;
        return _random.Next(2) == 0;
    }

    public int NextInt(int maxExclusive = int.MaxValue)
    {
        Counter++;
        return _random.Next(maxExclusive);
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException("minInclusive", "Minimum must be lower than maximum.");
        }
        Counter++;
        return _random.Next(minInclusive, maxExclusive);
    }

    public uint NextUnsignedInt(uint maxExclusive = uint.MaxValue)
        => NextUnsignedInt(0u, maxExclusive);

    public uint NextUnsignedInt(uint minInclusive, uint maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException("minInclusive", "Minimum must be lower than maximum.");
        }
        Counter++;
        double r = _random.NextDouble();
        double range = maxExclusive - minInclusive;
        uint scaled = (uint)(r * range);
        return minInclusive + scaled;
    }

    public float NextFloat(float max = 1f) => NextFloat(0f, max);

    public float NextFloat(float min, float max)
    {
        if (min > max)
        {
            throw new ArgumentOutOfRangeException("min", "Minimum must not be higher than maximum.");
        }
        Counter++;
        return (float)(_random.NextDouble() * (double)(max - min) + (double)min);
    }

    public double NextDouble()
    {
        Counter++;
        return _random.NextDouble();
    }

    public double NextDouble(double min, double max)
    {
        if (min > max)
        {
            throw new ArgumentOutOfRangeException("min", "Minimum must not be higher than maximum.");
        }
        Counter++;
        return _random.NextDouble() * (max - min) + min;
    }

    public float NextGaussianFloat(float mean = 0f, float stdDev = 1f, float min = 0f, float max = 1f)
        => (float)NextGaussianDouble(mean, stdDev, min, max);

    public double NextGaussianDouble(double mean = 0.0, double stdDev = 1.0, double min = 0.0, double max = 1.0)
    {
        if (min > max)
        {
            throw new ArgumentOutOfRangeException("min", "Minimum must not be higher than maximum.");
        }
        Counter++;
        double result;
        do
        {
            double d = _random.NextDouble();
            double n = _random.NextDouble();
            double mag = Math.Sqrt(-2.0 * Math.Log(d));
            double angle = Math.PI * 2.0 * n;
            double z = mag * Math.Cos(angle);
            result = mean + z * stdDev;
        }
        while (result < 0.0 || result > 1.0);
        return result * (max - min) + min;
    }

    /// <summary>
    /// Pinned upstream quirk: this method does NOT increment <see cref="Counter"/>. Do not
    /// "fix" without coordinating a kernel-version bump.
    /// </summary>
    public int NextGaussianInt(int mean, int stdDev, int min, int max)
    {
        int r;
        do
        {
            double d = 1.0 - _random.NextDouble();
            double n = 1.0 - _random.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(d)) * Math.Sin(Math.PI * 2.0 * n);
            double a = (double)mean + (double)stdDev * z;
            r = (int)Math.Round(a);
        }
        while (r < min || r > max);
        return r;
    }

    public T? NextItem<T>(IEnumerable<T> items)
    {
        IEnumerable<T> source = (items as T[]) ?? items.ToArray();
        int count = source.Count();
        if (count == 0)
        {
            return default;
        }
        int index = NextInt(0, count);
        return source.ElementAt(index);
    }

    public T? WeightedNextItem<T>(IEnumerable<T> items, Func<T?, float> weightFetcher)
        => WeightedNextItem(NextFloat(), items, weightFetcher, default(T)!);

    public static T WeightedNextItem<T>(float randInput, IEnumerable<T> items, Func<T, float> weightFetcher, T fallback)
    {
        float total = items.Sum(weightFetcher);
        float remaining = randInput * total;
        foreach (T item in items)
        {
            remaining -= weightFetcher(item);
            if (remaining <= 0f)
            {
                return item;
            }
        }
        return fallback;
    }

    public void Shuffle<T>(IList<T> list)
    {
        for (int num = list.Count - 1; num > 0; num--)
        {
            int swap = NextInt(num + 1);
            (list[num], list[swap]) = (list[swap], list[num]);
        }
    }
}
