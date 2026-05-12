using System.Globalization;
using System.Text;

namespace Sts2Headless.Host;

/// <summary>
/// Thread-safe in-memory <see cref="IMetricsRegistry"/> with a Prometheus
/// text-format renderer. Phase 1 surface: counters + gauges only; the
/// observed-max gauge variant is used for the
/// <c>sts2_action_queue_depth_max</c> placeholder. No histogram support yet —
/// the M9 spec lists <c>sts2_gc_pauses_ms_sum</c> as a sum that's served via
/// <see cref="ObserveMax"/> or <see cref="SetGauge"/> depending on the
/// caller's read model.
///
/// <para>
/// <b>Format snapshot:</b>
/// </para>
/// <code>
///   # HELP sts2_combats_total Combats started by the host.
///   # TYPE sts2_combats_total counter
///   sts2_combats_total 1
///
///   # HELP sts2_action_queue_depth_max High-water action queue depth.
///   # TYPE sts2_action_queue_depth_max gauge
///   sts2_action_queue_depth_max 7
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
    private readonly Dictionary<string, double> _gauges = new();
    private readonly Dictionary<string, MetricDescriptor> _descriptors = new();
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
            else
            {
                _gauges.TryAdd(name, 0d);
            }
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
        var sb = new StringBuilder(512);
        lock (_lock)
        {
            // Render in name-sorted order so the output is stable across calls.
            foreach (KeyValuePair<string, MetricDescriptor> kv in _descriptors.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                MetricDescriptor d = kv.Value;
                sb.Append("# HELP ").Append(d.Name).Append(' ').AppendLine(d.Help);
                sb.Append("# TYPE ").Append(d.Name).Append(' ').AppendLine(d.Kind == MetricKind.Counter ? "counter" : "gauge");
                if (d.Kind == MetricKind.Counter)
                {
                    long v = _counters.GetValueOrDefault(d.Name, 0L);
                    sb.Append(d.Name).Append(' ').AppendLine(v.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    double v = _gauges.GetValueOrDefault(d.Name, 0d);
                    sb.Append(d.Name).Append(' ').AppendLine(v.ToString("R", CultureInfo.InvariantCulture));
                }
            }
        }
        return sb.ToString();
    }

    private void EnsureDescriptor(string name, MetricKind kind)
    {
        if (!_descriptors.TryGetValue(name, out var existing))
        {
            _descriptors[name] = new MetricDescriptor(name, kind, name);
            return;
        }
        if (existing.Kind != kind)
        {
            throw new InvalidOperationException(
                $"PrometheusMetricsRegistry: metric '{name}' is {existing.Kind}, cannot use as {kind}.");
        }
    }

    /// <summary>Counter / gauge discriminator.</summary>
    public enum MetricKind { Counter, Gauge }

    private sealed record MetricDescriptor(string Name, MetricKind Kind, string Help);
}
