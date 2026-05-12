using Sts2Headless.Host;

namespace Sts2Headless.Tests.Host;

public sealed class PrometheusMetricsRegistryTests
{
    [Fact]
    public void Increment_counter_accumulates()
    {
        var r = new PrometheusMetricsRegistry();
        r.IncrementCounter(MetricNames.CombatsTotal, 1);
        r.IncrementCounter(MetricNames.CombatsTotal, 2);
        string s = r.RenderPrometheus();
        Assert.Contains("sts2_combats_total 3", s);
    }

    [Fact]
    public void SetGauge_overwrites()
    {
        var r = new PrometheusMetricsRegistry();
        r.SetGauge(MetricNames.GcPausesMsSum, 12.5);
        r.SetGauge(MetricNames.GcPausesMsSum, 99.5);
        string s = r.RenderPrometheus();
        Assert.Contains("sts2_gc_pauses_ms_sum 99.5", s);
    }

    [Fact]
    public void ObserveMax_keeps_highest()
    {
        var r = new PrometheusMetricsRegistry();
        r.ObserveMax(MetricNames.ActionQueueDepthMax, 4);
        r.ObserveMax(MetricNames.ActionQueueDepthMax, 7);
        r.ObserveMax(MetricNames.ActionQueueDepthMax, 3);
        string s = r.RenderPrometheus();
        Assert.Contains("sts2_action_queue_depth_max 7", s);
    }

    [Fact]
    public void RenderPrometheus_includes_HELP_and_TYPE_lines()
    {
        var r = new PrometheusMetricsRegistry();
        string s = r.RenderPrometheus();
        Assert.Contains("# HELP sts2_combats_total", s);
        Assert.Contains("# TYPE sts2_combats_total counter", s);
        Assert.Contains("# HELP sts2_action_queue_depth_max", s);
        Assert.Contains("# TYPE sts2_action_queue_depth_max gauge", s);
    }

    [Fact]
    public void RenderPrometheus_pre_registers_all_M9_placeholder_metrics()
    {
        var r = new PrometheusMetricsRegistry();
        string s = r.RenderPrometheus();
        Assert.Contains(MetricNames.CombatsTotal, s);
        Assert.Contains(MetricNames.TurnsTotal, s);
        Assert.Contains(MetricNames.ActionsTotal, s);
        Assert.Contains(MetricNames.GcPausesMsSum, s);
        Assert.Contains(MetricNames.ActionQueueDepthMax, s);
    }

    [Fact]
    public void Zero_delta_increment_is_noop()
    {
        var r = new PrometheusMetricsRegistry();
        r.IncrementCounter(MetricNames.CombatsTotal, 0);
        string s = r.RenderPrometheus();
        Assert.Contains("sts2_combats_total 0", s);
    }

    [Fact]
    public void Counter_kind_mismatch_throws()
    {
        var r = new PrometheusMetricsRegistry();
        Assert.Throws<InvalidOperationException>(() => r.SetGauge(MetricNames.CombatsTotal, 1));
    }

    [Fact]
    public void Rendered_output_uses_invariant_culture_for_floats()
    {
        var r = new PrometheusMetricsRegistry();
        r.SetGauge(MetricNames.GcPausesMsSum, 1.5);
        string s = r.RenderPrometheus();
        // Period decimal separator, not comma.
        Assert.Contains("1.5", s);
        Assert.DoesNotContain("1,5", s);
    }

    [Fact]
    public void Concurrent_writes_do_not_lose_counts()
    {
        var r = new PrometheusMetricsRegistry();
        Parallel.For(0, 1000, _ => r.IncrementCounter(MetricNames.ActionsTotal, 1));
        Assert.Contains("sts2_actions_total 1000", r.RenderPrometheus());
    }
}

public sealed class MetricsHttpServerTests
{
    [Fact]
    public async Task Start_then_GET_metrics_returns_200_with_prometheus_body()
    {
        var registry = new PrometheusMetricsRegistry();
        registry.IncrementCounter(MetricNames.CombatsTotal, 7);
        int port = RandomLocalPort();
        using var server = new MetricsHttpServer(registry, port);
        server.Start();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var resp = await http.GetAsync($"http://127.0.0.1:{port}/metrics");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("sts2_combats_total 7", body);
        Assert.Contains("# TYPE", body);
    }

    [Fact]
    public async Task Unknown_path_returns_404()
    {
        var registry = new PrometheusMetricsRegistry();
        int port = RandomLocalPort();
        using var server = new MetricsHttpServer(registry, port);
        server.Start();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var resp = await http.GetAsync($"http://127.0.0.1:{port}/nowhere");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public void Stop_is_idempotent_and_shutdown_completes()
    {
        var registry = new PrometheusMetricsRegistry();
        int port = RandomLocalPort();
        var server = new MetricsHttpServer(registry, port);
        server.Start();
        server.Stop();
        server.Stop(); // idempotent
        // Dispose also calls Stop.
        server.Dispose();
    }

    [Fact]
    public void Out_of_range_port_throws()
    {
        var registry = new PrometheusMetricsRegistry();
        Assert.Throws<ArgumentOutOfRangeException>(() => new MetricsHttpServer(registry, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MetricsHttpServer(registry, 70000));
    }

    private static int RandomLocalPort()
    {
        // Bind a TcpListener on port 0 to let the OS pick a free port, then
        // release. This is racy in theory but adequate for the test scope.
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
