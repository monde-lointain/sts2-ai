using System.Text.Json;
using Sts2Headless.Adapters.ControlPlane;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Adapters.ControlPlane;

/// <summary>
/// S11-T7 — THE HARD GATE. A mock orchestrator drives the full M4 control
/// plane through:
///
/// <code>
///   save → set_seed(42) → step_until_decision
///        → apply_action(end_turn) → step_until_decision
///        → apply_action(end_turn) → save
/// </code>
///
/// <para>
/// And asserts the final state matches a self-pinned golden hash. This
/// proves the in-process engine state is reproducible across an
/// orchestrator-driven session — the M4 contract the curriculum generator
/// and eval harness depend on.
/// </para>
///
/// <para>
/// <b>Self-pinning policy:</b> the test records the final post-hash on first
/// run and asserts equality on every subsequent run. To regenerate after an
/// intentional engine change, replace <see cref="GoldenFinalHash"/> with the
/// new value from the test output.
/// </para>
/// </summary>
public sealed class MockOrchestratorEndToEndTests : IDisposable
{
    /// <summary>
    /// Pinned hash of the final state after the full T7 sequence. Generated
    /// by running this test once locally; commit the value to lock the
    /// trajectory. If the engine intentionally changes (e.g., S12 lands
    /// content alterations), regenerate.
    ///
    /// <para>
    /// Stream-B-T3 schema bump: MonsterIntent gained a MoveId field so
    /// multi-state monsters can rotate independently when sharing a catalog
    /// model. Stream-B-T4 schema bump: CombatState gained two aggregate
    /// counters (AttacksPlayedThisTurn / CardsDrawnThisCombat). Both shift
    /// the StateCodec layout, hence the new hash.
    /// </para>
    /// </summary>
    // B.1-alpha-T4 (2026-05-12, post-RC-2+RC-3): mechanical regen. RC-2
    // shifted master seed derivation (raw uint -> hash($"seed-{N}")); RC-3
    // split HP rolls onto .Niche and shuffles onto .Shuffle. The
    // canonical hash of the final state blob shifts as a consequence.
    //
    // B.1-gamma-T5 (2026-05-11): codec schema 2->3 (LastSpentEnergy +
    // ExhaustedShivCount appended to CombatState). Mechanical regen.
    // Wave-38/B: codec schema 3->4 (MonsterIntentPower.Target + MonsterIntent.SelfBlockGain
    // appended to MonsterIntent wire layout). Mechanical regen.
    private static readonly string GoldenFinalHash =
        "3dedd79f487c64204cf992958df379074adb32460e55024a1126d4016e4ced86";

    private const string PinSentinel = "PIN_AT_FIRST_RUN";

    private readonly string _socketPath;
    private ControlPlaneServer? _server;

    public MockOrchestratorEndToEndTests()
    {
        _socketPath = Path.Combine(Path.GetTempPath(), $"sts2-headless-t7-{Guid.NewGuid():N}.sock");
    }

    public void Dispose()
    {
        try
        {
            _server?.Stop();
        }
        catch
        { /* swallowed */
        }
        _server?.Dispose();
        if (File.Exists(_socketPath))
        {
            try
            {
                File.Delete(_socketPath);
            }
            catch
            { /* swallowed */
            }
        }
    }

    [Fact]
    public void HardGate_SaveSetSeedStepApplyStepApplySave_pins_final_hash()
    {
        // === Server side ===================================================
        ControlPlaneSession session = SessionFactory.BootSmokeSession(seed: 42u);
        _server = new ControlPlaneServer(_socketPath, session);
        _server.Start();

        // === Client side ==================================================
        using var client = new MockOrchestratorClient(_socketPath);

        // 1. save (snapshot of fresh combat).
        JsonElement save1 = client.CallExpectResult("save_state");
        string blob1 = save1.GetProperty("state_blob").GetString()!;
        Assert.False(string.IsNullOrEmpty(blob1));

        // 2. set_seed(42) — replaces RNG (current is also 42; the test verifies
        //    the orchestrator can issue this RPC without breaking state).
        JsonElement setSeed = client.CallExpectResult("set_seed", new { seed = 42 });
        Assert.True(setSeed.GetProperty("ok").GetBoolean());

        // 3. step_until_decision — combat is already at PlayerActing; this
        //    is essentially a no-op that returns the current decision shape.
        JsonElement step1 = client.CallExpectResult("step_until_decision");
        Assert.Equal("PlayerActing", step1.GetProperty("phase").GetString());

        // 4. apply_action(end_turn) — engine moves to EnemyTurnStart.
        JsonElement apply1 = client.CallExpectResult(
            "apply_action",
            new { action = new { type = "end_turn" } }
        );
        Assert.True(apply1.GetProperty("ok").GetBoolean());

        // 5. step_until_decision — engine drives through enemy turn and
        //    starts player turn 2, returning at PlayerActing.
        JsonElement step2 = client.CallExpectResult("step_until_decision");
        string phaseAfterStep2 = step2.GetProperty("phase").GetString()!;
        Assert.True(
            phaseAfterStep2 is "PlayerActing" or "CombatEnd",
            $"Expected PlayerActing or CombatEnd, got {phaseAfterStep2}."
        );

        // 6. apply_action(end_turn) — only if still in PlayerActing.
        if (phaseAfterStep2 == "PlayerActing")
        {
            JsonElement apply2 = client.CallExpectResult(
                "apply_action",
                new { action = new { type = "end_turn" } }
            );
            Assert.True(apply2.GetProperty("ok").GetBoolean());
        }

        // 7. save (final snapshot).
        JsonElement save2 = client.CallExpectResult("save_state");
        string blob2 = save2.GetProperty("state_blob").GetString()!;
        Assert.False(string.IsNullOrEmpty(blob2));

        // Compute the canonical hash of the final blob — this is the
        // value we self-pin.
        byte[] finalBytes = Convert.FromBase64String(blob2);
        string finalHash = CanonicalHash.Sha256Hex(finalBytes);

        // Verify the final state is meaningful (different from initial).
        Assert.NotEqual(blob1, blob2);

        // === HARD GATE: pinned hash =======================================
        // First-run policy: if GoldenFinalHash is the sentinel, fail with
        // the value to pin. Once pinned, any drift fails the test.
        if (string.Equals(GoldenFinalHash, PinSentinel, StringComparison.Ordinal))
        {
            Assert.Fail(
                $"First-run: pin the golden hash by replacing GoldenFinalHash with:\n"
                    + $"    \"{finalHash}\""
            );
        }
        Assert.Equal(GoldenFinalHash, finalHash);
    }

    [Fact]
    public void Sequence_is_reproducible_across_independent_servers()
    {
        // Two servers started with same seed + same RPC sequence should
        // produce the same final hash. Proves M4 deterministic round-trip
        // at the Q11 / Q12 contract level.
        string socketPathA = Path.Combine(
            Path.GetTempPath(),
            $"sts2-headless-t7-a-{Guid.NewGuid():N}.sock"
        );
        string socketPathB = Path.Combine(
            Path.GetTempPath(),
            $"sts2-headless-t7-b-{Guid.NewGuid():N}.sock"
        );

        ControlPlaneSession sessionA = SessionFactory.BootSmokeSession(seed: 42u);
        ControlPlaneSession sessionB = SessionFactory.BootSmokeSession(seed: 42u);

        using var serverA = new ControlPlaneServer(socketPathA, sessionA);
        using var serverB = new ControlPlaneServer(socketPathB, sessionB);
        serverA.Start();
        serverB.Start();

        try
        {
            string hashA = RunSequence(socketPathA);
            string hashB = RunSequence(socketPathB);
            Assert.Equal(hashA, hashB);
        }
        finally
        {
            serverA.Stop();
            serverB.Stop();
            if (File.Exists(socketPathA))
                try
                {
                    File.Delete(socketPathA);
                }
                catch
                { /* swallowed */
                }
            if (File.Exists(socketPathB))
                try
                {
                    File.Delete(socketPathB);
                }
                catch
                { /* swallowed */
                }
        }

        static string RunSequence(string path)
        {
            using var client = new MockOrchestratorClient(path);
            client.CallExpectResult("set_seed", new { seed = 42 });
            client.CallExpectResult("step_until_decision");
            client.CallExpectResult("apply_action", new { action = new { type = "end_turn" } });
            client.CallExpectResult("step_until_decision");
            JsonElement save = client.CallExpectResult("save_state");
            byte[] bytes = Convert.FromBase64String(save.GetProperty("state_blob").GetString()!);
            return CanonicalHash.Sha256Hex(bytes);
        }
    }
}
