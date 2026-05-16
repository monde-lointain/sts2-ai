using System.Net;
using System.Text;

namespace Sts2Headless.Host;

/// <summary>
/// Tiny <see cref="HttpListener"/>-backed HTTP server that serves
/// <c>GET /metrics</c> using <see cref="IMetricsRegistry.RenderPrometheus"/>.
/// Phase 1: Prometheus pull endpoint only — no health-check, no
/// authentication, no TLS.
///
/// <para>
/// <b>Lifecycle:</b> <see cref="Start"/> binds to the configured port on
/// 127.0.0.1 and spins up a background thread that accepts requests until
/// <see cref="Stop"/> is called. <see cref="Stop"/> is idempotent and safe to
/// call from a SIGTERM handler.
/// </para>
///
/// <para>
/// <b>R8 — opt-in:</b> per the stage prompt, the metrics server is OFF by
/// default. The host only constructs this class when <c>--metrics-port</c> is
/// specified. <see cref="HttpListener"/> is fully managed but its kernel-side
/// glue can fail on locked-down systems; keeping it optional preserves the
/// reference combat as an HTTP-free path.
/// </para>
///
/// <para>
/// <b>Thread model:</b> the listener thread is a single managed worker that
/// loops on <c>GetContext()</c>. Each request is handled inline. We don't
/// expect contention from Prometheus's typical 15s scrape interval.
/// </para>
/// </summary>
public sealed class MetricsHttpServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly IMetricsRegistry _registry;
    private Thread? _worker;
    private volatile bool _running;

    /// <summary>The fully-qualified URL the listener is bound to (e.g. <c>http://127.0.0.1:9090/</c>).</summary>
    public string Prefix { get; }

    /// <summary>Construct against a registry, binding to 127.0.0.1:&lt;port&gt;.</summary>
    public MetricsHttpServer(IMetricsRegistry registry, int port)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "port must be in 1..65535.");
        }
        _registry = registry;
        Prefix = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(Prefix);
    }

    /// <summary>Start accepting connections. Idempotent.</summary>
    public void Start()
    {
        if (_running)
            return;
        _listener.Start();
        _running = true;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "sts2-metrics-listener" };
        _worker.Start();
    }

    /// <summary>Stop accepting connections and join the worker. Idempotent.</summary>
    public void Stop()
    {
        if (!_running)
            return;
        _running = false;
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
            // already disposed — fine
        }
        _worker?.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Stop();
        ((IDisposable)_listener).Dispose();
    }

    private void WorkerLoop()
    {
        while (_running)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = _listener.GetContext();
            }
            catch (HttpListenerException)
            {
                // Listener stopped (e.g. Stop() called) — exit gracefully.
                break;
            }
            catch (InvalidOperationException)
            {
                // Both ObjectDisposedException (a subtype) and direct
                // InvalidOperationException land here — both signal that the
                // listener has been shut down externally.
                break;
            }

            try
            {
                Handle(ctx);
            }
            catch
            {
                // Never let an exception kill the listener.
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch
                { /* swallow */
                }
            }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        HttpListenerRequest req = ctx.Request;
        HttpListenerResponse resp = ctx.Response;
        try
        {
            string path = req.Url?.AbsolutePath ?? "/";
            if (req.HttpMethod == "GET" && path == "/metrics")
            {
                string body = _registry.RenderPrometheus();
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                resp.StatusCode = 200;
                resp.ContentType = "text/plain; version=0.0.4; charset=utf-8";
                resp.ContentLength64 = bytes.Length;
                resp.OutputStream.Write(bytes, 0, bytes.Length);
            }
            else if (req.HttpMethod == "GET" && path == "/")
            {
                byte[] bytes = Encoding.UTF8.GetBytes("sts2-headless — see /metrics\n");
                resp.StatusCode = 200;
                resp.ContentType = "text/plain; charset=utf-8";
                resp.ContentLength64 = bytes.Length;
                resp.OutputStream.Write(bytes, 0, bytes.Length);
            }
            else
            {
                resp.StatusCode = 404;
            }
        }
        finally
        {
            resp.OutputStream.Close();
            resp.Close();
        }
    }
}
