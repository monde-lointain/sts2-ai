using Sts2Headless.Host;
using Sts2Headless.Host.Metrics;

namespace Sts2Headless.Tests.Host.Metrics;

/// <summary>
/// Asserts the GC metric name constants match the schema in
/// <c>engine/headless/docs/specs/modules/process-host.md</c> (D5).
/// Schema is the contract Q7 codes against; this test pins the names so a
/// rename surfaces as a compile-or-test break, not silent drift.
/// </summary>
public sealed class GcMetricNamesTests
{
    [Fact]
    public void GcGenCollectionsTotal_matches_spec_name()
    {
        Assert.Equal("q1_gc_gen_collections_total", GcMetricNames.GcGenCollectionsTotal);
    }

    [Fact]
    public void GcAllocatedBytesTotal_matches_spec_name()
    {
        Assert.Equal("q1_gc_allocated_bytes_total", GcMetricNames.GcAllocatedBytesTotal);
    }

    [Fact]
    public void GcPauseMicroseconds_matches_spec_name()
    {
        Assert.Equal("q1_gc_pause_microseconds", GcMetricNames.GcPauseMicroseconds);
    }

    [Fact]
    public void GcTimeSeconds_matches_spec_name()
    {
        Assert.Equal("q1_gc_time_seconds", GcMetricNames.GcTimeSeconds);
    }
}
