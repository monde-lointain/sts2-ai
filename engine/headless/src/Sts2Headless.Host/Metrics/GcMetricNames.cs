namespace Sts2Headless.Host.Metrics;

/// <summary>
/// Canonical GC metric names. Source of truth is the D5 schema in
/// <c>engine/headless/docs/specs/modules/process-host.md</c>; this file pins
/// the names so a Q7 contract drift surfaces as a build / test break.
/// </summary>
public static class GcMetricNames
{
    /// <summary>Counter, labels: <c>gen ∈ {0,1,2}</c>. From <c>GC.CollectionCount(gen)</c> deltas.</summary>
    public const string GcGenCollectionsTotal = "q1_gc_gen_collections_total";

    /// <summary>Counter. From <c>GC.GetTotalAllocatedBytes(precise:false)</c>.</summary>
    public const string GcAllocatedBytesTotal = "q1_gc_allocated_bytes_total";

    /// <summary>Histogram (µs buckets). Per-collection pause from <c>GC.GetTotalPauseDuration()</c> deltas.</summary>
    public const string GcPauseMicroseconds = "q1_gc_pause_microseconds";

    /// <summary>Counter (cumulative seconds). Same source as the pause histogram, exposed for Prometheus <c>rate()</c>.</summary>
    public const string GcTimeSeconds = "q1_gc_time_seconds";

    /// <summary>D5 bucket boundaries for the GC pause histogram (microseconds).</summary>
    public static readonly double[] GcPauseBuckets = { 10, 50, 100, 500, 1_000, 5_000, 10_000, 50_000 };
}
