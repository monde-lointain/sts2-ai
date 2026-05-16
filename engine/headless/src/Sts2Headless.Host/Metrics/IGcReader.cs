namespace Sts2Headless.Host.Metrics;

/// <summary>
/// Indirection over <see cref="System.GC"/> so the sampler can be tested
/// without depending on real runtime GC behaviour. Production wires
/// <see cref="SystemGcReader"/>; tests substitute a fake.
/// </summary>
public interface IGcReader
{
    /// <summary>Total collections at or above <paramref name="generation"/>.</summary>
    int GetCollectionCount(int generation);

    /// <summary>Bytes allocated cumulatively on the managed heap (precise=false reading).</summary>
    long GetTotalAllocatedBytes();

    /// <summary>Cumulative wall-clock spent in GC pauses across this process's lifetime (.NET 7+).</summary>
    TimeSpan GetTotalPauseDuration();
}

/// <summary>Production <see cref="IGcReader"/> wired to <see cref="System.GC"/>.</summary>
public sealed class SystemGcReader : IGcReader
{
    public int GetCollectionCount(int generation) => GC.CollectionCount(generation);

    public long GetTotalAllocatedBytes() => GC.GetTotalAllocatedBytes(precise: false);

    public TimeSpan GetTotalPauseDuration() => GC.GetTotalPauseDuration();
}
