using Sts2Headless.Host;

namespace Sts2Headless.Tests.Host.Metrics;

/// <summary>
/// Tests the additive labeled-counter extension required by the D5 schema
/// (e.g., <c>q1_gc_gen_collections_total{gen="0|1|2"}</c>). Labels are encoded
/// in the Prometheus text format as <c>name{k="v"} value</c>.
/// </summary>
public sealed class LabeledCounterTests
{
    [Fact]
    public void IncrementLabeledCounter_accumulates_per_label_value()
    {
        var r = new PrometheusMetricsRegistry();
        r.Register("q1_test_total", PrometheusMetricsRegistry.MetricKind.Counter, "Test counter.");
        r.IncrementLabeledCounter("q1_test_total", "k", "a", 1);
        r.IncrementLabeledCounter("q1_test_total", "k", "a", 2);
        r.IncrementLabeledCounter("q1_test_total", "k", "b", 5);

        string s = r.RenderPrometheus();
        Assert.Contains("q1_test_total{k=\"a\"} 3", s);
        Assert.Contains("q1_test_total{k=\"b\"} 5", s);
    }

    [Fact]
    public void Labeled_and_unlabeled_counter_under_same_family_are_disjoint()
    {
        var r = new PrometheusMetricsRegistry();
        r.Register("q1_test_total", PrometheusMetricsRegistry.MetricKind.Counter, "Test counter.");
        r.IncrementCounter("q1_test_total", 10);
        r.IncrementLabeledCounter("q1_test_total", "k", "a", 7);

        string s = r.RenderPrometheus();
        // Unlabeled bare-name form (the registry pre-creates the no-label series at 0).
        Assert.Contains("q1_test_total 10", s);
        Assert.Contains("q1_test_total{k=\"a\"} 7", s);
    }

    [Fact]
    public void Labeled_counter_HELP_and_TYPE_emitted_once_per_family()
    {
        var r = new PrometheusMetricsRegistry();
        r.Register("q1_test_total", PrometheusMetricsRegistry.MetricKind.Counter, "Test counter.");
        r.IncrementLabeledCounter("q1_test_total", "k", "a", 1);
        r.IncrementLabeledCounter("q1_test_total", "k", "b", 1);

        string s = r.RenderPrometheus();
        // HELP/TYPE should appear exactly once for the family, not per-series.
        int helpCount = CountOccurrences(s, "# HELP q1_test_total ");
        int typeCount = CountOccurrences(s, "# TYPE q1_test_total ");
        Assert.Equal(1, helpCount);
        Assert.Equal(1, typeCount);
    }

    [Fact]
    public void Zero_delta_increment_is_noop_for_labeled_counter()
    {
        var r = new PrometheusMetricsRegistry();
        r.Register("q1_test_total", PrometheusMetricsRegistry.MetricKind.Counter, "Test counter.");
        r.IncrementLabeledCounter("q1_test_total", "k", "a", 0);
        // A zero delta must not synthesise a label series.
        string s = r.RenderPrometheus();
        Assert.DoesNotContain("q1_test_total{k=\"a\"}", s);
    }

    [Fact]
    public void Empty_label_value_throws()
    {
        var r = new PrometheusMetricsRegistry();
        r.Register("q1_test_total", PrometheusMetricsRegistry.MetricKind.Counter, "Test counter.");
        Assert.Throws<ArgumentException>(() =>
            r.IncrementLabeledCounter("q1_test_total", "k", string.Empty, 1)
        );
    }

    private static int CountOccurrences(string s, string needle)
    {
        int count = 0;
        int idx = 0;
        while ((idx = s.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
