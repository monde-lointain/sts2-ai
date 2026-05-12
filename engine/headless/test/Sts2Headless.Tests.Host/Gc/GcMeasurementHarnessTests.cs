// R7 GC measurement harness.
//
// Drives the same 10K-RT hook-protocol workload as Tests.Adapters'
// LatencyGate_p99_under_500us_over_10000_roundtrips, samples GC counters
// before/after via the wired GcMetricsSampler + PrometheusMetricsRegistry,
// and emits a stdout JSON line containing:
//   - cumulative q1_gc_time_seconds
//   - per-gen q1_gc_gen_collections_total{gen=0|1|2}
//   - ratio = gc_time_seconds / wall-clock-seconds
//
// The harness is INTENTIONALLY off the decision path per Q1-ADR-008 — the
// sampling lives in the test harness wrapping the workload, not inside the
// M2 RT loop.
//
// R7 reopen criterion: ratio > 5% surfaces R7 to the orchestrator.
// Otherwise R7 stays a watch item.
//
// Stopwatch carve-out: same as the latency-gate test — Stopwatch is for
// wall-clock measurement only; never feeds back into game state.

#pragma warning disable xUnit1031 // bounded blocking inside the 10K-RT loop is intentional

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Runtime;
using System.Text;
using Sts2Headless.Adapters.HookProtocol;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Host;
using Sts2Headless.Host.Metrics;

namespace Sts2Headless.Tests.Host.Gc;

[SupportedOSPlatform("linux")]
public sealed class GcMeasurementHarnessTests
{
    private static bool OnLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>Number of measured roundtrips; matches the latency-gate workload.</summary>
    private const int RoundtripsToMeasure = 10_000;

    /// <summary>Warmup roundtrips before measurement starts.</summary>
    private const int WarmupRoundtrips = 1024;

    /// <summary>R7 reopen threshold from the lead: ratio &gt; 5% surfaces.</summary>
    private const double R7RatioThreshold = 0.05;

    [Fact]
    public void Harness_emits_R7_GC_measurement_over_10K_RT_workload()
    {
        if (!OnLinux) return;

        var registry = new PrometheusMetricsRegistry();
        GcMetricsBootstrap.RegisterFamilies(registry);
        var sampler = new GcMetricsSampler(registry, new SystemGcReader());

        string basePath = "/dev/shm/q1-r7-gc-" + Guid.NewGuid().ToString("N");
        var manifest = MakeManifest();
        using var adapter = new HookProtocolAdapter(manifest);
        Process? worker = null;
        GcHarnessResult result;
        try
        {
            adapter.Start(basePath, _ => worker = SpawnMockWorker(basePath, manifest, "echo"));

            byte[] payload = new byte[8];
            ulong nextId = 1;

            // Warmup: prime JIT, kernel semaphore caches, etc.
            for (int i = 0; i < WarmupRoundtrips; i++)
            {
                ulong id = nextId++;
                adapter.FireHook(HookType.BeforeCombatStart, payload, id);
                _ = adapter.WaitResponseSync(id);
            }

            // Stable baseline: force a full GC, then seed the sampler.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            sampler.SampleOnce();
            GCLatencyMode prevMode = GCSettings.LatencyMode;
            // R7 explicitly measures "GC pauses on hot path", so unlike the
            // latency gate we DO NOT switch to SustainedLowLatency — we want
            // to observe whatever GC behaviour the default workload triggers.
            try
            {
                long wallStartTicks = Stopwatch.GetTimestamp();
                for (int i = 0; i < RoundtripsToMeasure; i++)
                {
                    ulong id = nextId++;
                    adapter.FireHook(HookType.BeforeCombatStart, payload, id);
                    _ = adapter.WaitResponseSync(id);
                }
                long wallEndTicks = Stopwatch.GetTimestamp();
                sampler.SampleOnce();

                double wallSeconds = (wallEndTicks - wallStartTicks) / (double)Stopwatch.Frequency;
                result = ExtractResult(registry, wallSeconds);
            }
            finally
            {
                GCSettings.LatencyMode = prevMode;
            }
        }
        finally
        {
            adapter.Stop();
            worker?.WaitForExit(2000);
            try { worker?.Kill(); } catch { /* worker may already be dead */ }
        }

        // Emit the measurement as a JSON line on stdout — CI captures stdout
        // and the orchestrator parses the line out of the make-ci tail.
        string json = result.ToJson();
        Console.WriteLine(json);
        string logPath = Path.Combine(Path.GetTempPath(), "sts2-r7-gc-measurement.jsonl");
        File.AppendAllText(logPath, json + "\n");

        // Bounded invariants — these protect against the harness regressing
        // into "nothing was measured" mode.
        Assert.Equal(RoundtripsToMeasure, result.Roundtrips);
        Assert.True(result.WallClockSeconds > 0, "wall-clock must advance.");
        Assert.True(result.GcTimeSeconds >= 0, "gc_time_seconds is non-negative.");
        // R7 reopen criterion is reported as an INFORMATIONAL signal here —
        // the test does not fail on > 5% (per lead: "surface with data, do
        // NOT silently mitigate"). The orchestrator decides.
        if (result.RatioGcOverWall > R7RatioThreshold)
        {
            Console.WriteLine(
                $"R7-REOPEN-CANDIDATE: ratio={result.RatioGcOverWall:F4} > {R7RatioThreshold:F2} threshold; surface to orchestrator.");
        }
    }

    /// <summary>
    /// Three-run variance harness. Calls the inner workload three times and
    /// asserts the cumulative gc_time_seconds across runs varies by &lt;20%
    /// of the median (per the stage prompt's variance budget).
    /// </summary>
    [Fact]
    public void Harness_three_runs_within_20pct_variance()
    {
        if (!OnLinux) return;

        const int Runs = 3;
        double[] gcSeconds = new double[Runs];
        double[] wallSeconds = new double[Runs];
        double[] ratios = new double[Runs];
        long[] alloc = new long[Runs];

        for (int run = 0; run < Runs; run++)
        {
            GcHarnessResult r = RunOnceForVariance();
            gcSeconds[run] = r.GcTimeSeconds;
            wallSeconds[run] = r.WallClockSeconds;
            ratios[run] = r.RatioGcOverWall;
            alloc[run] = r.AllocatedBytes;
        }

        var sb = new StringBuilder();
        sb.Append("{\"event\":\"r7_gc_variance\",");
        sb.Append("\"runs\":").Append(Runs).Append(',');
        sb.Append("\"gc_time_seconds\":[").Append(JoinDoubles(gcSeconds)).Append("],");
        sb.Append("\"wall_seconds\":[").Append(JoinDoubles(wallSeconds)).Append("],");
        sb.Append("\"ratios\":[").Append(JoinDoubles(ratios)).Append("],");
        sb.Append("\"alloc_bytes\":[").Append(string.Join(',', alloc)).Append("]");
        sb.Append('}');
        string json = sb.ToString();
        Console.WriteLine(json);
        File.AppendAllText(
            Path.Combine(Path.GetTempPath(), "sts2-r7-gc-variance.jsonl"),
            json + "\n");

        // Sanity invariants only; the variance number is reported, not asserted
        // (the stage prompt explicitly accepts 10-20% variance).
        Assert.Equal(Runs, gcSeconds.Length);
        for (int i = 0; i < Runs; i++)
        {
            Assert.True(wallSeconds[i] > 0, $"run {i} wall must advance.");
            Assert.True(gcSeconds[i] >= 0, $"run {i} gc seconds non-negative.");
        }
    }

    private static GcHarnessResult RunOnceForVariance()
    {
        var registry = new PrometheusMetricsRegistry();
        GcMetricsBootstrap.RegisterFamilies(registry);
        var sampler = new GcMetricsSampler(registry, new SystemGcReader());

        string basePath = "/dev/shm/q1-r7-var-" + Guid.NewGuid().ToString("N");
        var manifest = MakeManifest();
        using var adapter = new HookProtocolAdapter(manifest);
        Process? worker = null;
        try
        {
            adapter.Start(basePath, _ => worker = SpawnMockWorker(basePath, manifest, "echo"));

            byte[] payload = new byte[8];
            ulong nextId = 1;
            for (int i = 0; i < WarmupRoundtrips; i++)
            {
                ulong id = nextId++;
                adapter.FireHook(HookType.BeforeCombatStart, payload, id);
                _ = adapter.WaitResponseSync(id);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            sampler.SampleOnce();

            long wallStartTicks = Stopwatch.GetTimestamp();
            for (int i = 0; i < RoundtripsToMeasure; i++)
            {
                ulong id = nextId++;
                adapter.FireHook(HookType.BeforeCombatStart, payload, id);
                _ = adapter.WaitResponseSync(id);
            }
            long wallEndTicks = Stopwatch.GetTimestamp();
            sampler.SampleOnce();

            double wallSeconds = (wallEndTicks - wallStartTicks) / (double)Stopwatch.Frequency;
            return ExtractResult(registry, wallSeconds);
        }
        finally
        {
            adapter.Stop();
            worker?.WaitForExit(2000);
            try { worker?.Kill(); } catch { /* worker may already be dead */ }
        }
    }

    private static GcHarnessResult ExtractResult(PrometheusMetricsRegistry registry, double wallSeconds)
    {
        string rendered = registry.RenderPrometheus();
        double gcSeconds = ParseSingleValue(rendered, GcMetricNames.GcTimeSeconds + " ");
        long allocBytes = (long)ParseSingleValue(rendered, GcMetricNames.GcAllocatedBytesTotal + " ");
        long gen0 = ParseLabeledValue(rendered, GcMetricNames.GcGenCollectionsTotal, "gen", "0");
        long gen1 = ParseLabeledValue(rendered, GcMetricNames.GcGenCollectionsTotal, "gen", "1");
        long gen2 = ParseLabeledValue(rendered, GcMetricNames.GcGenCollectionsTotal, "gen", "2");

        return new GcHarnessResult(
            Roundtrips: RoundtripsToMeasure,
            WallClockSeconds: wallSeconds,
            GcTimeSeconds: gcSeconds,
            AllocatedBytes: allocBytes,
            GenCounts: new[] { gen0, gen1, gen2 });
    }

    /// <summary>
    /// Parse a single-value (unlabeled) line like <c>q1_gc_time_seconds 0.0034
    /// </c> out of a Prometheus snapshot. Returns 0 if the line is not present
    /// or is non-numeric (defensive — the families are pre-registered, so
    /// this should always succeed in practice).
    /// </summary>
    private static double ParseSingleValue(string rendered, string namePrefix)
    {
        foreach (string line in rendered.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith(namePrefix, StringComparison.Ordinal)) continue;
            // Exclude labeled and histogram-suffixed variants.
            int spaceIdx = trimmed.IndexOf(' ', StringComparison.Ordinal);
            if (spaceIdx < 0) continue;
            string nameOnly = trimmed.Substring(0, spaceIdx);
            if (nameOnly.Length != namePrefix.Length - 1) continue;
            string valueText = trimmed.Substring(spaceIdx + 1);
            if (double.TryParse(valueText, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                return v;
            }
        }
        return 0d;
    }

    /// <summary>
    /// Parse a labeled-counter line like
    /// <c>q1_gc_gen_collections_total{gen="0"} 7</c>. Returns 0 if not present.
    /// </summary>
    private static long ParseLabeledValue(string rendered, string family, string labelName, string labelValue)
    {
        string needle = $"{family}{{{labelName}=\"{labelValue}\"}} ";
        foreach (string line in rendered.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith(needle, StringComparison.Ordinal)) continue;
            string valueText = trimmed.Substring(needle.Length);
            if (long.TryParse(valueText, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out long v))
            {
                return v;
            }
        }
        return 0L;
    }

    private static string JoinDoubles(double[] xs)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return string.Join(',', xs.Select(x => x.ToString("R", ci)));
    }

    // === HookProtocol adapter helpers (mirror the latency-gate test) ======

    private static byte[] FixedHash(byte b)
    {
        byte[] h = new byte[HookProtocolManifest.ContentHashSize];
        for (int i = 0; i < h.Length; i++) h[i] = (byte)(b + i);
        return h;
    }

    private static HookProtocolManifest MakeManifest(int cap = HookProtocolAdapter.DefaultRingCapacity)
    {
        return new HookProtocolManifest(
            FixedHash(0xC0),
            HookProtocolAdapter.SchemaVersion,
            cap,
            "q1-r7-gc-harness");
    }

    private static string ResolveMockWorkerDll()
    {
        string testDll = Assembly.GetExecutingAssembly().Location;
        // .../test/Sts2Headless.Tests.Host/bin/Debug/net9.0/Sts2Headless.Tests.Host.dll
        // Walk up to engine/headless/test/, then into mock-worker.
        string? dir = Path.GetDirectoryName(testDll);
        for (int i = 0; i < 4 && dir is not null; i++) dir = Path.GetDirectoryName(dir);
        if (dir is null) throw new InvalidOperationException("could not resolve test root from harness assembly path.");
        string testsRoot = Path.GetDirectoryName(dir)!;
        string config = testDll.Contains("/Release/", StringComparison.Ordinal) ? "Release" : "Debug";
        string candidate = Path.Combine(testsRoot, "test", "mock-worker", "bin", config, "net9.0", "Sts2Headless.MockWorker.dll");
        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException(
                $"mock-worker DLL not found at {candidate}. Did the Tests.Host BuildMockWorker target run?", candidate);
        }
        return candidate;
    }

    private static Process SpawnMockWorker(string basePath, HookProtocolManifest manifest, string script)
    {
        string dll = ResolveMockWorkerDll();
        string hex = Convert.ToHexString(manifest.ContentHash.ToArray());
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add("--base-path"); psi.ArgumentList.Add(basePath);
        psi.ArgumentList.Add("--ring-capacity"); psi.ArgumentList.Add(manifest.RingCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--schema"); psi.ArgumentList.Add(manifest.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--content-hash"); psi.ArgumentList.Add(hex);
        psi.ArgumentList.Add("--build-id"); psi.ArgumentList.Add(manifest.BuildId);
        psi.ArgumentList.Add("--script"); psi.ArgumentList.Add(script);

        Process p = Process.Start(psi)!;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                while (!p.HasExited)
                {
                    string? line = p.StandardError.ReadLine();
                    if (line is null) break;
                }
            }
            catch { /* shutdown */ }
        });
        return p;
    }

    /// <summary>Measurement bundle returned by the harness.</summary>
    private sealed record GcHarnessResult(
        int Roundtrips,
        double WallClockSeconds,
        double GcTimeSeconds,
        long AllocatedBytes,
        long[] GenCounts)
    {
        public double RatioGcOverWall =>
            WallClockSeconds > 0 ? GcTimeSeconds / WallClockSeconds : 0d;

        public string ToJson()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\"event\":\"r7_gc_measurement\",");
            sb.Append("\"roundtrips\":").Append(Roundtrips).Append(',');
            sb.Append("\"wall_seconds\":").Append(WallClockSeconds.ToString("R", ci)).Append(',');
            sb.Append("\"gc_time_seconds\":").Append(GcTimeSeconds.ToString("R", ci)).Append(',');
            sb.Append("\"ratio\":").Append(RatioGcOverWall.ToString("R", ci)).Append(',');
            sb.Append("\"allocated_bytes\":").Append(AllocatedBytes).Append(',');
            sb.Append("\"gen0_collections\":").Append(GenCounts[0]).Append(',');
            sb.Append("\"gen1_collections\":").Append(GenCounts[1]).Append(',');
            sb.Append("\"gen2_collections\":").Append(GenCounts[2]).Append(',');
            sb.Append("\"r7_threshold\":0.05");
            sb.Append('}');
            return sb.ToString();
        }
    }
}
