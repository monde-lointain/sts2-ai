using Sts2Headless.Host;

namespace Sts2Headless.Tests.Host;

/// <summary>
/// Capturing logger used in tests — records each <c>Log</c> call so assertions
/// can inspect the emitted event stream.
/// </summary>
public sealed class CapturingLogger : IStructuredLogger
{
    public List<(string Event, IReadOnlyDictionary<string, object?> Payload)> Entries { get; } = new();

    public void Log(string eventType, IReadOnlyDictionary<string, object?> payload)
    {
        Entries.Add((eventType, payload));
    }
}

/// <summary>
/// In-memory metrics registry used in tests — accumulates counter/gauge
/// values in a dictionary so assertions can read them without depending on
/// the Prometheus text format.
/// </summary>
public sealed class InMemoryMetrics : IMetricsRegistry
{
    public Dictionary<string, long> Counters { get; } = new();
    public Dictionary<string, double> Gauges { get; } = new();

    public void IncrementCounter(string name, long delta)
    {
        if (delta == 0L) return;
        Counters[name] = Counters.TryGetValue(name, out long current) ? current + delta : delta;
    }

    public void SetGauge(string name, double value)
    {
        Gauges[name] = value;
    }

    public void ObserveMax(string name, double value)
    {
        if (Gauges.TryGetValue(name, out double current))
        {
            if (value > current) Gauges[name] = value;
        }
        else
        {
            Gauges[name] = value;
        }
    }

    public string RenderPrometheus() => string.Empty;
}
