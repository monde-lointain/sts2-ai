using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sts2Headless.DeterminismProbe;

/// <summary>
/// Compares Q1-side and upstream-side mid-combat snapshot sequences
/// (<see cref="MidCombatRecord"/> sequences) for one (encounter, seed) tuple.
/// Reports the first divergence as <c>(encounter, seed, turn, side, field)</c>
/// with Q1 and golden values — mirrors
/// <c>UpstreamInitialStateComparer.BuildDiffSummary</c> for the mid-combat case.
///
/// <para>
/// <b>Phase-2 multi-turn comparison (wave-50/A.3):</b> goldens contain one
/// <c>MidCombatRecord</c> per turn-side (<c>"player-pre"</c>, <c>"player-end"</c>,
/// <c>"enemy-end"</c>) × N turns per action-sequence, captured via A.2's
/// multi-turn <c>UpstreamDriver.CaptureMidCombat</c>. This is the sole
/// comparison path; the Phase-1 Turn-0 single-snapshot fast-path (wave-49/E6)
/// is deleted per ADR-035 Amendment #2 §2.
/// </para>
///
/// <para>
/// Usage: for each (encounter, seed) entry in the probe corpus, load the
/// golden file from disk, capture fresh Q1 bytes, diff. A golden file
/// missing from disk is an ERROR (not a skip) — mid-combat goldens must be
/// pre-committed via <c>make probe-upstream-mid-combat-capture</c>.
/// </para>
/// </summary>
public sealed class MidCombatComparer
{
    private readonly string _goldensRoot;

    /// <summary>
    /// Construct a comparer rooted at <paramref name="goldensRoot"/>.
    /// Conventionally <c>test/determinism-probe/goldens-upstream/mid-combat/</c>.
    /// </summary>
    public MidCombatComparer(string goldensRoot)
    {
        ArgumentNullException.ThrowIfNull(goldensRoot);
        _goldensRoot = goldensRoot;
    }

    // -------------------------------------------------------------------------
    // Result types
    // -------------------------------------------------------------------------

    /// <summary>Outcome of comparing one (encounter, seed) pair.</summary>
    public enum EntryOutcome
    {
        /// <summary>All snapshots matched byte-for-byte.</summary>
        Pass,

        /// <summary>At least one snapshot field diverged. <see cref="EntryResult.DiffSummary"/> is populated.</summary>
        Diverged,

        /// <summary>Golden file not found on disk.</summary>
        GoldenMissing,

        /// <summary>Q1 or upstream capture threw, or golden is corrupt.</summary>
        Error,
    }

    /// <summary>Result for one (encounter, seed) comparison run.</summary>
    public sealed record EntryResult(
        string EncounterId,
        int Seed,
        EntryOutcome Outcome,
        string? DiffSummary,
        string? ErrorMessage
    );

    // -------------------------------------------------------------------------
    // Compare methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Compare the Q1 capture sequence against the stored golden for
    /// (<paramref name="encounterId"/>, <paramref name="seed"/>).
    /// </summary>
    public EntryResult CompareOne(
        string encounterId,
        int seed,
        IReadOnlyList<MidCombatRecord> q1Records
    )
    {
        ArgumentNullException.ThrowIfNull(encounterId);
        ArgumentNullException.ThrowIfNull(q1Records);

        string goldenPath = GoldenPath(encounterId, seed);
        if (!File.Exists(goldenPath))
        {
            return new EntryResult(
                encounterId,
                seed,
                EntryOutcome.GoldenMissing,
                DiffSummary: null,
                ErrorMessage: $"golden missing: {goldenPath}"
            );
        }

        IReadOnlyList<MidCombatRecord> golden;
        try
        {
            golden = MidCombatRecord.ReadFile(goldenPath);
        }
        catch (Exception ex)
        {
            return new EntryResult(
                encounterId,
                seed,
                EntryOutcome.Error,
                DiffSummary: null,
                ErrorMessage: $"golden read failed: {ex.GetType().Name}: {ex.Message}"
            );
        }

        string? diff = BuildDiffSummary(encounterId, seed, q1Records, golden);
        if (diff is null)
        {
            return new EntryResult(
                encounterId,
                seed,
                EntryOutcome.Pass,
                DiffSummary: null,
                ErrorMessage: null
            );
        }
        return new EntryResult(
            encounterId,
            seed,
            EntryOutcome.Diverged,
            DiffSummary: diff,
            ErrorMessage: null
        );
    }

    // -------------------------------------------------------------------------
    // File path helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Path to the golden file for (encounterId, seed).
    /// Layout: <c>goldens-upstream/mid-combat/{encounterId}/{seed}.bin</c>.
    /// </summary>
    public string GoldenPath(string encounterId, int seed) =>
        Path.Combine(_goldensRoot, encounterId, $"{seed}.bin");

    // -------------------------------------------------------------------------
    // Diff summary builder
    // -------------------------------------------------------------------------

    /// <summary>
    /// Field-level diff of two multi-turn record sequences. Returns null on full match.
    /// On first divergence returns a human-readable summary naming
    /// (encounter, seed, turn, side, field) with Q1 and golden values.
    /// Modeled on <c>UpstreamInitialStateComparer.BuildDiffSummary</c>.
    ///
    /// <para>
    /// Goldens are Phase-2 multi-turn format: one record per turn-side
    /// (<c>"player-pre"</c>, <c>"player-end"</c>, <c>"enemy-end"</c>) × N turns.
    /// The Phase-1 Turn-0 single-snapshot fast-path is deleted (wave-50/A.3;
    /// ADR-035 Amendment #2 §2).
    /// </para>
    /// </summary>
    private static string? BuildDiffSummary(
        string encounterId,
        int seed,
        IReadOnlyList<MidCombatRecord> q1,
        IReadOnlyList<MidCombatRecord> golden
    )
    {
        if (q1.Count != golden.Count)
        {
            return $"encounter={encounterId} seed={seed}: record count mismatch q1={q1.Count} golden={golden.Count}";
        }

        for (int i = 0; i < q1.Count; i++)
        {
            MidCombatRecord qr = q1[i];
            MidCombatRecord gr = golden[i];

            // Key fields match check.
            if (qr.Turn != gr.Turn || qr.Side != gr.Side)
            {
                return $"encounter={encounterId} seed={seed} " +
                       $"record[{i}]: turn/side mismatch q1=({qr.Turn},{qr.Side}) golden=({gr.Turn},{gr.Side})";
            }

            int turn = qr.Turn;
            string side = qr.Side;

            // Scalar player fields.
            if (qr.PlayerHp != gr.PlayerHp)
                return DivergeLine(encounterId, seed, turn, side, "Player.Hp", qr.PlayerHp, gr.PlayerHp);
            if (qr.PlayerBlock != gr.PlayerBlock)
                return DivergeLine(encounterId, seed, turn, side, "Player.Block", qr.PlayerBlock, gr.PlayerBlock);
            if (qr.Energy != gr.Energy)
                return DivergeLine(encounterId, seed, turn, side, "Energy", qr.Energy, gr.Energy);
            if (qr.RngCounter != gr.RngCounter)
                return DivergeLine(encounterId, seed, turn, side, "RngCounter", qr.RngCounter, gr.RngCounter);

            // Player powers.
            string? powerDiff = DiffPowerLists(
                encounterId,
                seed,
                turn,
                side,
                "Player.Powers",
                qr.PowerStacks,
                gr.PowerStacks
            );
            if (powerDiff is not null)
                return powerDiff;

            // Enemies.
            if (qr.Enemies.Count != gr.Enemies.Count)
                return DivergeLine(
                    encounterId,
                    seed,
                    turn,
                    side,
                    "Enemy.Count",
                    qr.Enemies.Count,
                    gr.Enemies.Count
                );

            for (int ei = 0; ei < qr.Enemies.Count; ei++)
            {
                EnemySnapshot qe = qr.Enemies[ei];
                EnemySnapshot ge = gr.Enemies[ei];
                string ePfx = $"Enemy[{ei}]({qe.Name})";

                if (qe.Hp != ge.Hp)
                    return DivergeLine(encounterId, seed, turn, side, $"{ePfx}.Hp", qe.Hp, ge.Hp);
                if (qe.Block != ge.Block)
                    return DivergeLine(encounterId, seed, turn, side, $"{ePfx}.Block", qe.Block, ge.Block);
                if (qe.MoveId != ge.MoveId)
                    return DivergeLine(encounterId, seed, turn, side, $"{ePfx}.MoveId", qe.MoveId, ge.MoveId);
                if (qe.IntentKind != ge.IntentKind)
                    return DivergeLine(
                        encounterId,
                        seed,
                        turn,
                        side,
                        $"{ePfx}.Intent.Kind",
                        qe.IntentKind,
                        ge.IntentKind
                    );
                if (qe.IntentDamagePerHit != ge.IntentDamagePerHit)
                    return DivergeLine(
                        encounterId,
                        seed,
                        turn,
                        side,
                        $"{ePfx}.Intent.DmgPerHit",
                        qe.IntentDamagePerHit,
                        ge.IntentDamagePerHit
                    );
                if (qe.IntentHitCount != ge.IntentHitCount)
                    return DivergeLine(
                        encounterId,
                        seed,
                        turn,
                        side,
                        $"{ePfx}.Intent.HitCount",
                        qe.IntentHitCount,
                        ge.IntentHitCount
                    );
                if (qe.IntentSelfBlockGain != ge.IntentSelfBlockGain)
                    return DivergeLine(
                        encounterId,
                        seed,
                        turn,
                        side,
                        $"{ePfx}.Intent.SelfBlock",
                        qe.IntentSelfBlockGain,
                        ge.IntentSelfBlockGain
                    );

                string? ePowerDiff = DiffPowerLists(
                    encounterId,
                    seed,
                    turn,
                    side,
                    $"{ePfx}.Powers",
                    qe.Powers,
                    ge.Powers
                );
                if (ePowerDiff is not null)
                    return ePowerDiff;
            }
        }

        return null; // full match
    }

    private static string? DiffPowerLists(
        string encounterId,
        int seed,
        int turn,
        string side,
        string prefix,
        IReadOnlyList<PowerStackEntry> q1Powers,
        IReadOnlyList<PowerStackEntry> goldenPowers
    )
    {
        if (q1Powers.Count != goldenPowers.Count)
            return DivergeLine(
                encounterId,
                seed,
                turn,
                side,
                $"{prefix}.Count",
                q1Powers.Count,
                goldenPowers.Count
            );

        for (int pi = 0; pi < q1Powers.Count; pi++)
        {
            PowerStackEntry qp = q1Powers[pi];
            PowerStackEntry gp = goldenPowers[pi];
            if (qp.ModelId != gp.ModelId)
                return DivergeLine(
                    encounterId,
                    seed,
                    turn,
                    side,
                    $"{prefix}[{pi}].ModelId",
                    qp.ModelId,
                    gp.ModelId
                );
            if (qp.Stacks != gp.Stacks)
                return DivergeLine(
                    encounterId,
                    seed,
                    turn,
                    side,
                    $"{prefix}[{pi}]({qp.ModelId}).Stacks",
                    qp.Stacks,
                    gp.Stacks
                );
        }

        return null;
    }

    private static string DivergeLine<T>(
        string encounter, int seed, int turn, string side, string field, T q1Val, T goldenVal
    ) =>
        $"encounter={encounter} seed={seed} turn={turn} side={side} field={field} q1={q1Val} golden={goldenVal}";
}
