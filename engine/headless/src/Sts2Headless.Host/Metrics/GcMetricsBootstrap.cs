namespace Sts2Headless.Host.Metrics;

/// <summary>
/// Registers the four D5-spec GC metric families with a
/// <see cref="PrometheusMetricsRegistry"/>. Pre-registration is so a
/// Prometheus scrape sees stable families from boot (all-zero values are fine
/// per Prometheus convention).
/// </summary>
public static class GcMetricsBootstrap
{
    /// <summary>
    /// Register all four GC families on the given registry. Idempotent
    /// (re-registration of the same (name, kind) is a no-op per the registry
    /// contract).
    /// </summary>
    public static void RegisterFamilies(PrometheusMetricsRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(
            GcMetricNames.GcGenCollectionsTotal,
            PrometheusMetricsRegistry.MetricKind.Counter,
            "GC collections per generation (cumulative). Computed from GC.CollectionCount deltas.");
        registry.Register(
            GcMetricNames.GcAllocatedBytesTotal,
            PrometheusMetricsRegistry.MetricKind.Counter,
            "Bytes allocated on managed heap (cumulative, from GC.GetTotalAllocatedBytes(precise=false)).");
        registry.RegisterHistogram(
            GcMetricNames.GcPauseMicroseconds,
            GcMetricNames.GcPauseBuckets,
            "Per-collection GC pause wall-clock (from GC.GetTotalPauseDuration() deltas).");
        registry.Register(
            GcMetricNames.GcTimeSeconds,
            PrometheusMetricsRegistry.MetricKind.Counter,
            "Cumulative GC pause time in seconds (project-lead R7 metric).");
    }
}
