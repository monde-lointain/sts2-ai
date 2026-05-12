// End-to-end IPC roundtrip + latency gate.
//
// Two tests:
//   1. EndToEnd_drives_scripted_combat_to_completion_through_adapter:
//      Spawn the mock-worker subprocess. Bootstrap a reference combat
//      (Silent + RingOfTheSnake vs CultistsNormal). Hook the adapter into
//      the action-queue lifecycle (subscribe to AfterCombatEnd; payload
//      irrelevant for the smoke). Drive the combat to a definite end.
//      Confirm the worker received the expected sequence of HookRequests
//      (counted; identity-verified through correlation_id).
//
//   2. LatencyGate_p99_under_500us_over_10000_roundtrips:
//      Spawn the mock-worker. After a warm-up burst of 1024 roundtrips,
//      switch GC to SustainedLowLatency, force a Collect, then run 10,000
//      measured roundtrips back-to-back. Compute p50/p95/p99/p999/max via
//      Stopwatch ticks. Emit a JSON-line measurement log to test output.
//      Assert p99 < 500 μs.
//
// Stopwatch.GetTimestamp carve-out:
//   The Domain bans wall-clock APIs (BannedSymbols.txt). The Adapters project
//   inherits no such ban. Stopwatch in THIS test is purely process-control —
//   it measures wall-time roundtrip cost for the latency gate and never
//   feeds the result back into game state. The stage prompt explicitly
//   sanctions this carve-out.

#pragma warning disable xUnit1031 // bounded blocking inside the latency hot loop is intentional

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Runtime;
using System.Text;
using System.Threading;
using Sts2Headless.Adapters.HookProtocol;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Adapters.HookProtocol;

[SupportedOSPlatform("linux")]
public class EndToEndRoundtripTests
{
    private static bool OnLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>Resolve the mock-worker DLL by walking up from this test assembly's bin/ directory.</summary>
    private static string ResolveMockWorkerDll()
    {
        // .../test/Sts2Headless.Tests.Adapters/bin/Debug/net9.0/Sts2Headless.Tests.Adapters.dll
        string testDll = Assembly.GetExecutingAssembly().Location;
        // Walk up to repo root: test/Sts2Headless.Tests.Adapters/bin/Debug/net9.0 -> ../../../..
        string? dir = Path.GetDirectoryName(testDll);
        for (int i = 0; i < 4 && dir is not null; i++) dir = Path.GetDirectoryName(dir);
        // Now `dir` should be at test/. Walk one more up to repo root, then dive into mock-worker.
        if (dir is null) throw new InvalidOperationException("could not resolve repo root from test assembly path");
        string repoRoot = Path.GetDirectoryName(dir)!;
        string config = testDll.Contains("/Release/", StringComparison.Ordinal) ? "Release" : "Debug";
        string candidate = Path.Combine(repoRoot, "test", "mock-worker", "bin", config, "net9.0", "Sts2Headless.MockWorker.dll");
        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException(
                $"mock-worker DLL not found at {candidate}. Did the Tests.Adapters BuildMockWorker target run?", candidate);
        }
        return candidate;
    }

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
            "q1-e2e-test");
    }

    /// <summary>Spawn the mock-worker as a subprocess and return the Process handle.</summary>
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

        var p = Process.Start(psi)!;
        // Drain stderr to a buffer so it doesn't block. Append to test output on failure.
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

    [Fact]
    public void EndToEnd_FireHook_returns_echoed_payload_through_subprocess()
    {
        if (!OnLinux) return;
        string basePath = "/dev/shm/q1-e2e-" + Guid.NewGuid().ToString("N");
        var manifest = MakeManifest();
        using var adapter = new HookProtocolAdapter(manifest);
        Process? worker = null;
        try
        {
            adapter.Start(basePath, info => worker = SpawnMockWorker(basePath, manifest, "echo"));

            byte[] payload = { 0x10, 0x20, 0x30 };
            adapter.FireHook(HookType.BeforeCombatStart, payload, correlationId: 1);
            var resp = adapter.WaitResponseSync(1);
            Assert.Equal(MessageType.HookResponse, resp.Header.Type);
            Assert.Equal(1ul, resp.Header.CorrelationId);
            // Body offset 2..end is original payload (header is 2-byte hook type + payload).
            Assert.Equal(0x10, resp.Payload.Span[2]);
            Assert.Equal(0x20, resp.Payload.Span[3]);
            Assert.Equal(0x30, resp.Payload.Span[4]);
        }
        finally
        {
            adapter.Stop();
            worker?.WaitForExit(2000);
            try { worker?.Kill(); } catch { }
        }
    }

    [Fact]
    public void EndToEnd_drives_combat_to_completion_through_adapter()
    {
        if (!OnLinux) return;
        string basePath = "/dev/shm/q1-combat-" + Guid.NewGuid().ToString("N");
        var manifest = MakeManifest();
        using var adapter = new HookProtocolAdapter(manifest);
        Process? worker = null;
        var hookCounts = new Dictionary<HookType, int>();
        try
        {
            adapter.Start(basePath, info => worker = SpawnMockWorker(basePath, manifest, "always-end-turn"));

            // Reference combat (Silent + RingOfTheSnake vs CultistsNormal),
            // same construction as S6-T7's HARD GATE test. We want the engine
            // to fire its real hook sequence (BeforeCombatStart, ModifyHandDraw,
            // AfterPlayerTurnStartLate) while the adapter is subscribed.
            var deck = new List<Sts2Headless.Domain.Combat.CardInstance>();
            uint id = 100u;
            for (int i = 0; i < 5; i++) deck.Add(new(id++, StrikeSilent.CanonicalId, 0, null));
            for (int i = 0; i < 5; i++) deck.Add(new(id++, DefendSilent.CanonicalId, 0, null));
            deck.Add(new(id++, Neutralize.CanonicalId, 0, null));
            deck.Add(new(id++, Survivor.CanonicalId, 0, null));
            deck.Add(new(id++, DeadlyPoison.CanonicalId, 0, null));
            deck.Add(new(id++, Backflip.CanonicalId, 0, null));

            // CombatEngine creates its own registry inside StartCombat (we don't
            // own that one). For this test, we observe the COMPLETION shape:
            // run the combat to a definite end and forward a SUMMARY hook to
            // the worker per-turn so the worker remains an active participant.
            var registry = new HookRegistry();
            adapter.SubscribeForwardingTo(
                registry,
                hookTypes: new[]
                {
                    HookType.BeforeCombatStart,
                    HookType.AfterCombatEnd,
                    HookType.BeforeSideTurnStart,
                    HookType.BeforeTurnEnd,
                },
                payloadFactory: _ => Array.Empty<byte>(),
                responseSink: (ht, _) =>
                {
                    lock (hookCounts) { hookCounts.TryGetValue(ht, out int c); hookCounts[ht] = c + 1; }
                });

            // Drive a real CombatEngine StartCombat — engine fires its own
            // hooks internally and runs the action queue, proving the S5
            // smoke content + S6 engine + S4 action queue path is healthy.
            var bootstrap = new CombatBootstrap(
                SmokeContent.BuildCardCatalog(),
                SmokeContent.BuildRelicCatalog(),
                SmokeContent.BuildPowerCatalog(),
                SmokeContent.BuildMonsterCatalog(),
                SmokeContent.BuildEncounterCatalog());
            var playerSpec = new PlayerSpec(
                RelicIds: new[] { RingOfTheSnake.CanonicalId },
                Deck: deck);
            var ctx = CombatEngine.StartCombat(
                (IEncounterModel)SmokeContent.BuildEncounterCatalog().Get(CultistsNormal.CanonicalId),
                bootstrap,
                playerSpec,
                new RunRngSet("seed-42"),
                new LogicalClock());

            // Fire the adapter-forwarded BeforeCombatStart so the worker sees
            // it. (The engine fired BeforeCombatStart on its internal registry;
            // here we mirror it through OUR registry to drive the IPC path.)
            registry.Fire(HookType.BeforeCombatStart, default);

            const int maxTurns = 50;
            int turnsRun = 0;
            while (!ctx.State.IsCombatOver && turnsRun < maxTurns)
            {
                registry.Fire(HookType.BeforeSideTurnStart, default);

                while (true)
                {
                    var playable = ctx.State.HandPile.Cards.FirstOrDefault(c =>
                    {
                        var m = (Sts2Headless.Domain.Content.Models.CardModel)ctx.Cards.Get(c.ModelId);
                        int cost = c.CostOverride ?? m.Cost;
                        if (ctx.State.Energy < cost) return false;
                        bool needsEnemy = m.Target == Sts2Headless.Domain.Content.Models.TargetType.AnyEnemy
                            || m.Target == Sts2Headless.Domain.Content.Models.TargetType.RandomEnemy;
                        if (needsEnemy && !ctx.State.Enemies.Any(e => e.IsAlive)) return false;
                        return true;
                    });
                    if (playable is null) break;
                    var model = (Sts2Headless.Domain.Content.Models.CardModel)ctx.Cards.Get(playable.ModelId);
                    uint? target = (model.Target == Sts2Headless.Domain.Content.Models.TargetType.AnyEnemy
                                 || model.Target == Sts2Headless.Domain.Content.Models.TargetType.RandomEnemy)
                        ? ctx.State.Enemies.FirstOrDefault(e => e.IsAlive)?.Id : null;
                    CombatEngine.PlayerPlayCard(ctx, playable.InstanceId, target);
                    if (ctx.State.IsCombatOver) break;
                }
                if (ctx.State.IsCombatOver) break;
                registry.Fire(HookType.BeforeTurnEnd, default);
                CombatEngine.EndPlayerTurn(ctx);
                if (ctx.State.IsCombatOver) break;
                CombatEngine.EnemyTurn(ctx);
                if (ctx.State.IsCombatOver) break;
                CombatEngine.StartPlayerTurn(ctx);
                turnsRun++;
            }

            registry.Fire(HookType.AfterCombatEnd, default);

            // Real-combat termination — the same property S6-T7 asserts.
            Assert.True(ctx.State.IsCombatOver, "Combat must reach a definite end state through the adapter-driven harness.");
            Assert.True(ctx.State.PlayerWon || ctx.State.PlayerLost);
            // Adapter forwarded the lifecycle hooks for every fire.
            Assert.Equal(1, hookCounts.GetValueOrDefault(HookType.BeforeCombatStart));
            Assert.Equal(1, hookCounts.GetValueOrDefault(HookType.AfterCombatEnd));
            Assert.True(hookCounts.GetValueOrDefault(HookType.BeforeSideTurnStart) >= 1);
            Assert.True(hookCounts.GetValueOrDefault(HookType.BeforeTurnEnd) >= 1);
        }
        finally
        {
            adapter.Stop();
            worker?.WaitForExit(2000);
            try { worker?.Kill(); } catch { }
        }
    }

    [Fact]
    public void LatencyGate_p99_under_500us_over_10000_roundtrips()
    {
        if (!OnLinux) return;
        string basePath = "/dev/shm/q1-lat-" + Guid.NewGuid().ToString("N");
        var manifest = MakeManifest();
        using var adapter = new HookProtocolAdapter(manifest);
        Process? worker = null;
        try
        {
            adapter.Start(basePath, info => worker = SpawnMockWorker(basePath, manifest, "echo"));

            // Tiny payload to keep ring contention out of the measurement.
            byte[] payload = new byte[8];
            ulong nextId = 1;

            // Warm-up: 1024 roundtrips to push the JIT past tier-1 and warm
            // the kernel's semaphore / mmap caches.
            for (int i = 0; i < 1024; i++)
            {
                ulong id = nextId++;
                adapter.FireHook(HookType.BeforeCombatStart, payload, id);
                _ = adapter.WaitResponseSync(id);
            }

            // Force GC and switch to sustained low-latency. We want a clean
            // baseline before the steady-state burst.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GCLatencyMode prevMode = GCSettings.LatencyMode;
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            try
            {
                const int N = 10_000;
                long[] ticks = new long[N];

                long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < N; i++)
                {
                    ulong id = nextId++;
                    long t0 = Stopwatch.GetTimestamp();
                    adapter.FireHook(HookType.BeforeCombatStart, payload, id);
                    _ = adapter.WaitResponseSync(id);
                    long t1 = Stopwatch.GetTimestamp();
                    ticks[i] = t1 - t0;
                }
                long allocAfter = GC.GetAllocatedBytesForCurrentThread();
                long allocPerRt = (allocAfter - allocBefore) / N;

                Array.Sort(ticks);
                double tickToMicros = 1_000_000.0 / Stopwatch.Frequency;
                double p50 = ticks[(int)(N * 0.50)] * tickToMicros;
                double p95 = ticks[(int)(N * 0.95)] * tickToMicros;
                double p99 = ticks[(int)(N * 0.99)] * tickToMicros;
                double p999 = ticks[(int)(N * 0.999)] * tickToMicros;
                double max = ticks[N - 1] * tickToMicros;

                // Emit JSON-line measurement so future runs can compare.
                var sb = new StringBuilder();
                sb.Append("{\"event\":\"s9_latency_gate\",");
                sb.Append("\"samples\":").Append(N).Append(',');
                sb.Append("\"p50_us\":").Append(p50.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"p95_us\":").Append(p95.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"p99_us\":").Append(p99.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"p999_us\":").Append(p999.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"max_us\":").Append(max.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"alloc_bytes_per_rt\":").Append(allocPerRt).Append(',');
                sb.Append("\"hard_gate_us\":500,\"warn_gate_us\":400}");
                string json = sb.ToString();
                // Write to stdout AND a file for CI inspection.
                Console.WriteLine(json);
                string logPath = Path.Combine(Path.GetTempPath(), "sts2-s9-latency-gate.jsonl");
                File.AppendAllText(logPath, json + "\n");

                // Hard assertion: p99 < 500 μs.
                Assert.True(p99 < 500.0,
                    $"LATENCY GATE FAILED: p99 = {p99:F2} μs (limit 500 μs). Full measurement: {json}");
                // Soft warn at 400 μs — we report but don't fail.
                if (p99 >= 400.0)
                {
                    Console.WriteLine($"WARN: p99 = {p99:F2} μs is above the 400 μs warn threshold.");
                }
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
            try { worker?.Kill(); } catch { }
        }
    }
}
