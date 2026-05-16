namespace Sts2Headless.Host.Metrics;

/// <summary>
/// Samples GC counters and pushes deltas into a
/// <see cref="PrometheusMetricsRegistry"/>. Intended to run on the M9 utility
/// thread (or a test-harness thread), NEVER on the decision path per
/// Q1-ADR-008.
///
/// <para>
/// <b>Delta protocol:</b> the first call to <see cref="SampleOnce"/> seeds the
/// baseline (no metric writes). Subsequent calls push <c>(current - baseline)
/// </c> as a counter delta to the registry, then advance the baseline. Negative
/// deltas (which would imply non-monotonic GC counters) are clamped at zero.
/// </para>
///
/// <para>
/// <b>Pause histogram protocol:</b> the delta of
/// <see cref="IGcReader.GetTotalPauseDuration"/> across one sample interval
/// represents aggregate pause across all collections that happened in that
/// interval. We record it as a single observation per sample (not per
/// collection — that fine grain isn't available from the public GC API). For
/// the 10K-RT harness the sampling interval is "the whole workload", so
/// effectively a single observation captures the entire workload's GC pause.
/// </para>
/// </summary>
public sealed class GcMetricsSampler
{
    private readonly PrometheusMetricsRegistry _registry;
    private readonly IGcReader _reader;
    private bool _hasBaseline;
    private readonly int[] _baselineCounts = new int[3];
    private long _baselineAllocated;
    private TimeSpan _baselinePause;

    public GcMetricsSampler(PrometheusMetricsRegistry registry, IGcReader reader)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(reader);
        _registry = registry;
        _reader = reader;
    }

    /// <summary>Take a sample. First call seeds the baseline; subsequent calls push deltas.</summary>
    public void SampleOnce()
    {
        int[] counts = new int[3];
        for (int gen = 0; gen < 3; gen++)
        {
            counts[gen] = _reader.GetCollectionCount(gen);
        }
        long allocated = _reader.GetTotalAllocatedBytes();
        TimeSpan pause = _reader.GetTotalPauseDuration();

        if (!_hasBaseline)
        {
            Array.Copy(counts, _baselineCounts, 3);
            _baselineAllocated = allocated;
            _baselinePause = pause;
            _hasBaseline = true;
            return;
        }

        for (int gen = 0; gen < 3; gen++)
        {
            long delta = ClampNonNegative(counts[gen] - _baselineCounts[gen]);
            if (delta > 0)
            {
                _registry.IncrementLabeledCounter(
                    GcMetricNames.GcGenCollectionsTotal,
                    "gen",
                    gen.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    delta
                );
            }
            _baselineCounts[gen] = counts[gen];
        }

        long allocDelta = ClampNonNegative(allocated - _baselineAllocated);
        if (allocDelta > 0)
        {
            _registry.IncrementCounter(GcMetricNames.GcAllocatedBytesTotal, allocDelta);
        }
        _baselineAllocated = allocated;

        TimeSpan pauseDelta = pause - _baselinePause;
        if (pauseDelta > TimeSpan.Zero)
        {
            double pauseMicros = pauseDelta.TotalMicroseconds;
            _registry.ObserveHistogram(GcMetricNames.GcPauseMicroseconds, pauseMicros);
            _registry.IncrementCounterFloat(GcMetricNames.GcTimeSeconds, pauseDelta.TotalSeconds);
        }
        _baselinePause = pause;
    }

    /// <summary>Current sampler-side cumulative pause time, in seconds.</summary>
    public double CumulativeGcSeconds => (_baselinePause - TimeSpan.Zero).TotalSeconds;

    private static long ClampNonNegative(long v) => v < 0 ? 0 : v;
}
