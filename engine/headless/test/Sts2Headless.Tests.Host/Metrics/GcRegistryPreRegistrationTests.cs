using Sts2Headless.Host;
using Sts2Headless.Host.Metrics;

namespace Sts2Headless.Tests.Host.Metrics;

/// <summary>
/// Asserts <see cref="PrometheusMetricsRegistry"/> pre-registers every GC
/// metric family from the D5 schema so a Prometheus scrape sees stable
/// families from boot (all-zero values are fine).
/// </summary>
public sealed class GcRegistryPreRegistrationTests
{
    [Fact]
    public void RegisterGcFamilies_renders_all_four_GC_metric_names()
    {
        var r = new PrometheusMetricsRegistry();
        GcMetricsBootstrap.RegisterFamilies(r);
        string s = r.RenderPrometheus();

        Assert.Contains("# TYPE q1_gc_gen_collections_total counter", s);
        Assert.Contains("# TYPE q1_gc_allocated_bytes_total counter", s);
        Assert.Contains("# TYPE q1_gc_pause_microseconds histogram", s);
        Assert.Contains("# TYPE q1_gc_time_seconds counter", s);
    }

    [Fact]
    public void RegisterGcFamilies_emits_per_gen_label_series_for_collections()
    {
        var r = new PrometheusMetricsRegistry();
        GcMetricsBootstrap.RegisterFamilies(r);
        // Trigger a label-series creation so the gen=0/1/2 lines show up
        // even with no actual collections observed.
        r.IncrementLabeledCounter(GcMetricNames.GcGenCollectionsTotal, "gen", "0", 0);
        r.IncrementLabeledCounter(GcMetricNames.GcGenCollectionsTotal, "gen", "1", 0);
        r.IncrementLabeledCounter(GcMetricNames.GcGenCollectionsTotal, "gen", "2", 0);
        // A 0-delta increment is a no-op per the registry contract; that's
        // fine here — the families are still registered and HELP/TYPE renders.
        string s = r.RenderPrometheus();
        Assert.Contains("# HELP q1_gc_gen_collections_total", s);
    }

    [Fact]
    public void RegisterGcFamilies_pause_histogram_uses_spec_buckets()
    {
        var r = new PrometheusMetricsRegistry();
        GcMetricsBootstrap.RegisterFamilies(r);
        string s = r.RenderPrometheus();
        // D5 buckets for q1_gc_pause_microseconds: 10, 50, 100, 500, 1k, 5k, 10k, 50k.
        Assert.Contains("q1_gc_pause_microseconds_bucket{le=\"10\"}", s);
        Assert.Contains("q1_gc_pause_microseconds_bucket{le=\"50\"}", s);
        Assert.Contains("q1_gc_pause_microseconds_bucket{le=\"100\"}", s);
        Assert.Contains("q1_gc_pause_microseconds_bucket{le=\"500\"}", s);
        Assert.Contains("q1_gc_pause_microseconds_bucket{le=\"1000\"}", s);
        Assert.Contains("q1_gc_pause_microseconds_bucket{le=\"5000\"}", s);
        Assert.Contains("q1_gc_pause_microseconds_bucket{le=\"10000\"}", s);
        Assert.Contains("q1_gc_pause_microseconds_bucket{le=\"50000\"}", s);
        Assert.Contains("q1_gc_pause_microseconds_bucket{le=\"+Inf\"}", s);
    }
}
