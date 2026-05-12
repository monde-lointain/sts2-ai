namespace Sts2Headless.Host;

/// <summary>
/// In-memory metrics registry exposed by the host's <c>/metrics</c> endpoint.
/// The surface is a tiny subset of Prometheus: counters (incremented per
/// event), gauges (set to a value), and a Prometheus text-format renderer.
///
/// <para>
/// <b>Placeholder counters (per the M9 spec, Phase 1):</b>
/// </para>
/// <list type="bullet">
///   <item><c>sts2_combats_total</c>.</item>
///   <item><c>sts2_turns_total</c>.</item>
///   <item><c>sts2_actions_total</c>.</item>
///   <item><c>sts2_gc_pauses_ms_sum</c> (gauge).</item>
///   <item><c>sts2_action_queue_depth_max</c> (gauge).</item>
/// </list>
/// </summary>
public interface IMetricsRegistry
{
    /// <summary>Add <paramref name="delta"/> to the named counter (zero is a no-op).</summary>
    void IncrementCounter(string name, long delta);

    /// <summary>Set the named gauge to <paramref name="value"/> (overwrites).</summary>
    void SetGauge(string name, double value);

    /// <summary>Update the named gauge to max(current, value). Used for "queue depth max".</summary>
    void ObserveMax(string name, double value);

    /// <summary>Render a Prometheus text-format snapshot.</summary>
    string RenderPrometheus();
}

/// <summary>Canonical metric names used by the host.</summary>
public static class MetricNames
{
    public const string CombatsTotal = "sts2_combats_total";
    public const string TurnsTotal = "sts2_turns_total";
    public const string ActionsTotal = "sts2_actions_total";
    public const string GcPausesMsSum = "sts2_gc_pauses_ms_sum";
    public const string ActionQueueDepthMax = "sts2_action_queue_depth_max";
}
