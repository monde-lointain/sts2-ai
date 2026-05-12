using System.Globalization;
using System.Text;

namespace Sts2Headless.Host;

/// <summary>
/// Thread-safe in-memory <see cref="IMetricsRegistry"/> with a Prometheus
/// text-format renderer. Phase 1 surface: counters + gauges + labeled-counter
/// series + histograms. The observed-max gauge variant is used for the
/// <c>sts2_action_queue_depth_max</c> placeholder. The histogram support
/// added in R7 backs <c>q1_gc_pause_microseconds</c> per the D5 schema.
///
/// <para>
/// <b>Format snapshot:</b>
/// </para>
/// <code>
///   # HELP sts2_combats_total Combats started by the host.
///   # TYPE sts2_combats_total counter
///   sts2_combats_total 1
///
///   # HELP q1_gc_gen_collections_total ...
///   # TYPE q1_gc_gen_collections_total counter
///   q1_gc_gen_collections_total 0
///   q1_gc_gen_collections_total{gen="0"} 3
///   q1_gc_gen_collections_total{gen="1"} 1
///
///   # HELP q1_gc_pause_microseconds ...
///   # TYPE q1_gc_pause_microseconds histogram
///   q1_gc_pause_microseconds_bucket{le="10"} 0
///   q1_gc_pause_microseconds_bucket{le="+Inf"} 4
///   q1_gc_pause_microseconds_sum 1234.5
///   q1_gc_pause_microseconds_count 4
/// </code>
///
/// <para>
/// <b>Thread safety:</b> all mutations + reads acquire the internal lock so a
/// scrape happening concurrently with a metric update returns a consistent
/// snapshot. The lock granularity is coarse but adequate for Q1's
/// single-threaded combat loop (the metrics thread is the only contender).
/// </para>
/// </summary>
public sealed class PrometheusMetricsRegistry : IMetricsRegistry
{
    private readonly Dictionary<string, long> _counters = new();
    private readonly Dictionary<string, double> _floatCounters = new();
    private readonly Dictionary<string, double> _gauges = new();
    private readonly Dictionary<string, MetricDescriptor> _descriptors = new();
    // Labeled counter series, keyed by (familyName, labelName, labelValue).
    private readonly Dictionary<LabelKey, long> _labeledCounters = new();
    private readonly Dictionary<string, HistogramState> _histograms = new();
    private readonly object _lock = new();

    /// <summary>
    /// Construct with the M9-spec placeholder counters pre-registered so the
    /// rendered output is stable from boot (even with all-zero values, the
    /// Prometheus scrape sees the families).
    /// </summary>
    public PrometheusMetricsRegistry()
    {
        Register(MetricNames.CombatsTotal, MetricKind.Counter, "Combats started by the host.");
        Register(MetricNames.TurnsTotal, MetricKind.Counter, "Total turns played across all combats.");
        Register(MetricNames.ActionsTotal, MetricKind.Counter, "Total player actions applied.");
        Register(MetricNames.GcPausesMsSum, MetricKind.Gauge, "Cumulative GC pause time in milliseconds.");
        Register(MetricNames.ActionQueueDepthMax, MetricKind.Gauge, "High-water mark of action-queue depth.");
    }

    /// <summary>Register a metric family. Idempotent on the same (name, kind) pair.</summary>
    public void Register(string name, MetricKind kind, string help)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        lock (_lock)
        {
            if (_descriptors.TryGetValue(name, out var existing))
            {
                if (existing.Kind != kind)
                {
                    throw new InvalidOperationException(
                        $"PrometheusMetricsRegistry: metric '{name}' already registered as {existing.Kind}.");
                }
                return;
            }
            _descriptors[name] = new MetricDescriptor(name, kind, help);
            if (kind == MetricKind.Counter)
            {
                _counters.TryAdd(name, 0L);
            }
            else if (kind == MetricKind.FloatCounter)
            {
                _floatCounters.TryAdd(name, 0d);
            }
            else if (kind == MetricKind.Gauge)
            {
                _gauges.TryAdd(name, 0d);
            }
            // Histograms register via RegisterHistogram; falling through here
            // would be a programming error.
        }
    }

    /// <summary>
    /// Register a histogram family with strictly-ascending bucket upper bounds.
    /// The implicit <c>+Inf</c> bucket is appended automatically.
    /// </summary>
    public void RegisterHistogram(string name, IReadOnlyList<double> upperBounds, string help)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(upperBounds);
        if (upperBounds.Count == 0)
        {
            throw new ArgumentException("Histogram buckets must be non-empty.", nameof(upperBounds));
        }
        for (int i = 1; i < upperBounds.Count; i++)
        {
            if (!(upperBounds[i] > upperBounds[i - 1]))
            {
                throw new ArgumentException(
                    "Histogram buckets must be strictly ascending.", nameof(upperBounds));
            }
        }
        lock (_lock)
        {
            if (_descriptors.TryGetValue(name, out var existing))
            {
                if (existing.Kind != MetricKind.Histogram)
                {
                    throw new InvalidOperationException(
                        $"PrometheusMetricsRegistry: metric '{name}' already registered as {existing.Kind}.");
                }
                return;
            }
            _descriptors[name] = new MetricDescriptor(name, MetricKind.Histogram, help);
            _histograms[name] = new HistogramState(upperBounds.ToArray());
        }
    }

    public void IncrementCounter(string name, long delta)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (delta == 0L) return;
        lock (_lock)
        {
            EnsureDescriptor(name, MetricKind.Counter);
            _counters[name] = _counters.TryGetValue(name, out long c) ? c + delta : delta;
        }
    }

    /// <summary>
    /// Add <paramref name="delta"/> to a float-valued cumulative counter.
    /// Used for the seconds-shaped GC counter (<c>q1_gc_time_seconds</c>).
    /// </summary>
    public void IncrementCounterFloat(string name, double delta)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (delta == 0d) return;
        lock (_lock)
        {
            EnsureDescriptor(name, MetricKind.FloatCounter);
            _floatCounters[name] = _floatCounters.TryGetValue(name, out double c) ? c + delta : delta;
        }
    }

    /// <summary>
    /// Increment a labeled counter series under <paramref name="name"/>.
    /// Currently supports a single label key per call (sufficient for the
    /// D5-spec GC metrics, which all use <c>gen ∈ {0,1,2}</c>).
    /// </summary>
    public void IncrementLabeledCounter(string name, string labelName, string labelValue, long delta)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(labelName);
        ArgumentException.ThrowIfNullOrEmpty(labelValue);
        if (delta == 0L) return;
        lock (_lock)
        {
            EnsureDescriptor(name, MetricKind.Counter);
            var key = new LabelKey(name, labelName, labelValue);
            _labeledCounters[key] = _labeledCounters.TryGetValue(key, out long c) ? c + delta : delta;
        }
    }

    /// <summary>Record a single observation into <paramref name="name"/>'s histogram.</summary>
    public void ObserveHistogram(string name, double value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        lock (_lock)
        {
            if (!_histograms.TryGetValue(name, out HistogramState? state))
            {
                throw new InvalidOperationException(
                    $"PrometheusMetricsRegistry: histogram '{name}' is not registered.");
            }
            state.Observe(value);
        }
    }

    public void SetGauge(string name, double value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        lock (_lock)
        {
            EnsureDescriptor(name, MetricKind.Gauge);
            _gauges[name] = value;
        }
    }

    public void ObserveMax(string name, double value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        lock (_lock)
        {
            EnsureDescriptor(name, MetricKind.Gauge);
            if (_gauges.TryGetValue(name, out double current))
            {
                if (value > current) _gauges[name] = value;
            }
            else
            {
                _gauges[name] = value;
            }
        }
    }

    public string RenderPrometheus()
    {
        var sb = new StringBuilder(1024);
        lock (_lock)
        {
            // Render in name-sorted order so the output is stable across calls.
            foreach (KeyValuePair<string, MetricDescriptor> kv in _descriptors.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                MetricDescriptor d = kv.Value;
                sb.Append("# HELP ").Append(d.Name).Append(' ').AppendLine(d.Help);
                sb.Append("# TYPE ").Append(d.Name).Append(' ').AppendLine(TypeText(d.Kind));
                switch (d.Kind)
                {
                    case MetricKind.Counter:
                        RenderCounter(sb, d.Name);
                        break;
                    case MetricKind.FloatCounter:
                        RenderFloatCounter(sb, d.Name);
                        break;
                    case MetricKind.Gauge:
                        RenderGauge(sb, d.Name);
                        break;
                    case MetricKind.Histogram:
                        RenderHistogram(sb, d.Name);
                        break;
                }
            }
        }
        return sb.ToString();
    }

    private void RenderCounter(StringBuilder sb, string name)
    {
        long v = _counters.GetValueOrDefault(name, 0L);
        sb.Append(name).Append(' ').AppendLine(v.ToString(CultureInfo.InvariantCulture));
        // Labeled series, sorted by (label-name, label-value) for stable output.
        foreach (KeyValuePair<LabelKey, long> kv in _labeledCounters
                     .Where(kv => kv.Key.Family == name)
                     .OrderBy(kv => kv.Key.LabelName, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.LabelValue, StringComparer.Ordinal))
        {
            sb.Append(name).Append('{')
              .Append(kv.Key.LabelName).Append("=\"").Append(kv.Key.LabelValue).Append("\"} ")
              .AppendLine(kv.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void RenderGauge(StringBuilder sb, string name)
    {
        double v = _gauges.GetValueOrDefault(name, 0d);
        sb.Append(name).Append(' ').AppendLine(v.ToString("R", CultureInfo.InvariantCulture));
    }

    private void RenderFloatCounter(StringBuilder sb, string name)
    {
        double v = _floatCounters.GetValueOrDefault(name, 0d);
        // Render zero as the integer literal "0" to keep the renderer's output
        // stable with the integer-counter convention from boot.
        string text = v == 0d ? "0" : v.ToString("R", CultureInfo.InvariantCulture);
        sb.Append(name).Append(' ').AppendLine(text);
    }

    private void RenderHistogram(StringBuilder sb, string name)
    {
        HistogramState state = _histograms[name];
        // Cumulative bucket counts.
        long cumulative = 0;
        for (int i = 0; i < state.UpperBounds.Length; i++)
        {
            cumulative += state.BucketCounts[i];
            sb.Append(name).Append("_bucket{le=\"")
              .Append(FormatBucketBound(state.UpperBounds[i]))
              .Append("\"} ").AppendLine(cumulative.ToString(CultureInfo.InvariantCulture));
        }
        cumulative += state.BucketCounts[^1]; // +Inf bucket count (last slot reserved for overflow)
        sb.Append(name).Append("_bucket{le=\"+Inf\"} ")
          .AppendLine(cumulative.ToString(CultureInfo.InvariantCulture));
        sb.Append(name).Append("_sum ")
          .AppendLine(state.Sum.ToString("R", CultureInfo.InvariantCulture));
        sb.Append(name).Append("_count ")
          .AppendLine(cumulative.ToString(CultureInfo.InvariantCulture));
    }

    private static string FormatBucketBound(double bound)
    {
        // Integer-valued bounds render without decimals (matches D5 spec text).
        if (bound == Math.Truncate(bound) && !double.IsInfinity(bound))
        {
            return ((long)bound).ToString(CultureInfo.InvariantCulture);
        }
        return bound.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string TypeText(MetricKind kind) => kind switch
    {
        MetricKind.Counter => "counter",
        MetricKind.FloatCounter => "counter",
        MetricKind.Gauge => "gauge",
        MetricKind.Histogram => "histogram",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private void EnsureDescriptor(string name, MetricKind kind)
    {
        if (!_descriptors.TryGetValue(name, out var existing))
        {
            _descriptors[name] = new MetricDescriptor(name, kind, name);
            if (kind == MetricKind.Counter) _counters.TryAdd(name, 0L);
            else if (kind == MetricKind.FloatCounter) _floatCounters.TryAdd(name, 0d);
            else if (kind == MetricKind.Gauge) _gauges.TryAdd(name, 0d);
            return;
        }
        if (existing.Kind != kind)
        {
            throw new InvalidOperationException(
                $"PrometheusMetricsRegistry: metric '{name}' is {existing.Kind}, cannot use as {kind}.");
        }
    }

    /// <summary>Counter / float-counter / gauge / histogram discriminator. Float-counter renders as Prometheus type <c>counter</c>.</summary>
    public enum MetricKind { Counter, FloatCounter, Gauge, Histogram }

    private sealed record MetricDescriptor(string Name, MetricKind Kind, string Help);

    /// <summary>Composite key for labeled-counter series under a family.</summary>
    private readonly record struct LabelKey(string Family, string LabelName, string LabelValue);

    /// <summary>Per-family histogram state. The last bucket slot is the +Inf overflow.</summary>
    private sealed class HistogramState
    {
        public double[] UpperBounds { get; }
        // BucketCounts.Length == UpperBounds.Length + 1; the trailing slot is +Inf-overflow.
        public long[] BucketCounts { get; }
        public double Sum { get; private set; }

        public HistogramState(double[] upperBounds)
        {
            UpperBounds = upperBounds;
            BucketCounts = new long[upperBounds.Length + 1];
        }

        public void Observe(double value)
        {
            Sum += value;
            for (int i = 0; i < UpperBounds.Length; i++)
            {
                if (value <= UpperBounds[i])
                {
                    BucketCounts[i]++;
                    return;
                }
            }
            BucketCounts[^1]++;
        }
    }
}
