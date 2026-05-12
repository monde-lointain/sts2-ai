using System.Collections.Generic;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Encounters;

namespace Sts2Headless.DeterminismProbe;

/// <summary>
/// Deterministic corpus generator for the Phase-1 determinism probe.
/// Produces a stable, reproducible <see cref="Corpus"/> from code so the JSON
/// can be regenerated rather than hand-maintained.
///
/// <para>
/// <b>Composition (Phase-1 Gate scope):</b>
/// </para>
/// <list type="bullet">
///   <item><b>per_step</b> — 50 seeds against the smoke encounter
///         <c>cultists_normal</c> with the standard end-turn script,
///         <em>plus</em> 1 per-step entry per non-smoke encounter (21 in
///         Phase 1) driving the same end-turn script. Stream-B-T5 added the
///         non-smoke per-step coverage — the HARD GATE for P-Combat
///         dispatch readiness: all 22 encounters must per-step-pass against
///         a real Silent starter deck.</item>
///   <item><b>initial_state</b> — 10 seeds × all 22 encounters, no script
///         (probe asserts the initial-state hash matches the golden).</item>
///   <item><b>structural</b> — 1 entry per encounter that just confirms the
///         encounter is registered + spawns the expected monster ids.</item>
/// </list>
/// </summary>
public static class CorpusGenerator
{
    /// <summary>Number of seeds used for smoke per-step coverage.</summary>
    public const int SmokeSeedCount = 50;

    /// <summary>Number of seeds used per encounter for initial-state coverage.</summary>
    public const int InitialStateSeedCount = 10;

    /// <summary>Pinned base seed; all corpus seeds derive deterministically from this.</summary>
    public const uint BaseSeed = 42u;

    /// <summary>The smoke encounter id (snake_case CLI form).</summary>
    public const string SmokeEncounterCliId = "cultists_normal";

    /// <summary>
    /// Standard end-turn drive script — drives the smoke combat to definite
    /// completion through deterministic enemy ramp. 50 end_turn lines matches
    /// the host's S8-T7 golden test.
    /// </summary>
    public static IReadOnlyList<string> StandardEndTurnScript { get; } =
        Enumerable.Repeat("end_turn", 50).ToList();

    /// <summary>
    /// Stream-B-T5: longer end-turn-only script used by per-step entries for
    /// the 21 non-smoke encounters. Some monsters (e.g., FungalBoss with 200
    /// HP) outlast a 50-turn ramp; the extended script ensures the probe
    /// reaches <c>combat_end</c> on every encounter so per-step validation
    /// can confirm a deterministic termination.
    /// </summary>
    public static IReadOnlyList<string> ExtendedEndTurnScript { get; } =
        Enumerable.Repeat("end_turn", 200).ToList();

    /// <summary>
    /// Build the full Phase-1 corpus. Entries are emitted in a stable order:
    /// structural-all, then initial-state-all (encounter × seed), then
    /// per-step-smoke (seed only). The probe runs them in file order, so this
    /// order is part of the determinism contract.
    /// </summary>
    public static Corpus BuildPhase1Corpus()
    {
        var entries = new List<CorpusEntry>();
        // Order: all 22 encounters in catalog registration order.
        IReadOnlyList<string> encounterIds = AllPhase1EncounterIds();

        // === Structural per encounter ====================================
        foreach (string enc in encounterIds)
        {
            entries.Add(new CorpusEntry(
                Id: $"structural-{enc}",
                Mode: CorpusEntry.ModeStructural,
                Seed: BaseSeed,
                Encounter: enc,
                Relics: SmokeRelics,
                Script: EmptyScript));
        }

        // === Initial-state: 10 seeds × 22 encounters =====================
        for (int seedIdx = 0; seedIdx < InitialStateSeedCount; seedIdx++)
        {
            uint seed = BaseSeed + (uint)seedIdx;
            foreach (string enc in encounterIds)
            {
                entries.Add(new CorpusEntry(
                    Id: $"initial-{enc}-seed{seed}",
                    Mode: CorpusEntry.ModeInitialState,
                    Seed: seed,
                    Encounter: enc,
                    Relics: SmokeRelics,
                    Script: EmptyScript));
            }
        }

        // === Per-step smoke: 50 seeds × cultists_normal ==================
        for (int seedIdx = 0; seedIdx < SmokeSeedCount; seedIdx++)
        {
            uint seed = BaseSeed + (uint)seedIdx;
            entries.Add(new CorpusEntry(
                Id: $"perstep-smoke-seed{seed}",
                Mode: CorpusEntry.ModePerStep,
                Seed: seed,
                Encounter: SmokeEncounterCliId,
                Relics: SmokeRelics,
                Script: StandardEndTurnScript));
        }

        // === Per-step Stream-B-T5: 1 entry per non-smoke encounter ========
        // Stream-B-T5 HARD GATE: extend per-step coverage to all 21 non-smoke
        // encounters using a real Silent starter deck + ring_of_the_snake +
        // a deterministic end-turn-only script. Each encounter contributes
        // one per-step entry at the base seed; per-step validation asserts
        // the probe trace reaches combat_end and matches the captured golden.
        //
        // B.1-gamma-T3 skip list: monsters whose ported rotation gates on
        // damage-induced HP threshold (e.g., Lagavulin's wake-on-half-HP).
        // An end_turn-only script never deals damage to the enemy, so a
        // Lagavulin asleep never wakes and the combat stands off forever.
        // The probe's per-step termination check fails as a result. These
        // encounters are intentionally excluded from per-step until the probe
        // harness gains a play-cards-first mode.
        var perStepSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LagavulinElite",
        };
        int nonSmokePerStepCount = 0;
        foreach (string enc in encounterIds)
        {
            // Skip the smoke encounter — already covered above with 50 seeds.
            if (string.Equals(enc, SmokeEncounterCliId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(enc, "CultistsNormal", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (perStepSkip.Contains(enc))
            {
                continue;
            }
            entries.Add(new CorpusEntry(
                Id: $"perstep-{enc}-seed{BaseSeed}",
                Mode: CorpusEntry.ModePerStep,
                Seed: BaseSeed,
                Encounter: enc,
                Relics: SmokeRelics,
                Script: ExtendedEndTurnScript));
            nonSmokePerStepCount++;
        }

        return new Corpus(
            Version: Corpus.CurrentVersion,
            Description:
                $"Q1 Phase-1 determinism corpus. " +
                $"{entries.Count} entries: " +
                $"{encounterIds.Count} structural + " +
                $"{InitialStateSeedCount}×{encounterIds.Count} initial-state + " +
                $"{SmokeSeedCount} per-step-smoke + " +
                $"{nonSmokePerStepCount} per-step-non-smoke (Stream-B-T5 HARD GATE).",
            Entries: entries);
    }

    /// <summary>The smoke encounter is included in this list as the first entry.</summary>
    public static IReadOnlyList<string> AllPhase1EncounterIds()
    {
        EncounterCatalog cat = Phase1Content.BuildEncounterCatalog();
        var list = new List<string>();
        foreach (string id in cat.EnumerateIds())
        {
            list.Add(id);
        }
        return list;
    }

    /// <summary>Smoke relic loadout used across the corpus.</summary>
    private static IReadOnlyList<string> SmokeRelics { get; } = new[] { "ring_of_the_snake" };

    /// <summary>Empty script — structural + initial-state entries don't need actions.</summary>
    private static IReadOnlyList<string> EmptyScript { get; } = Array.Empty<string>();
}
