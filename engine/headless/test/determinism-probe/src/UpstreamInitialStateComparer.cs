using System.Collections.Immutable;
using System.IO;
using System.Text;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.DeterminismProbe;

/// <summary>
/// Builds Q1's post-SetUpCombat snapshot for a (seed, encounter) tuple and
/// byte-compares it against the upstream-derived golden produced by
/// <c>Sts2Headless.UpstreamCapture</c> (Stream-C-T2/T3).
///
/// <para>
/// <b>Snapshot point:</b> matches upstream's state immediately after
/// <c>CombatManager.SetUpCombat</c> returns — i.e., enemies spawned with
/// rolled HP, player creature alive at starting HP, deck shuffled into the
/// draw pile, no hooks fired yet, energy = 0, turn = 0,
/// <see cref="CombatPhase.CombatStart"/>. This is the snapshot
/// <c>CombatEngine.StartCombat</c> constructs at its line ~166 (the
/// <c>initial</c> local) before firing BeforeCombatStart / ModifyHandDraw
/// hooks. We replicate the same shape here so the byte recipe is identical
/// without disturbing the production engine.
/// </para>
///
/// <para>
/// <b>Byte recipe:</b> matches <c>StateByteSerializer</c> in
/// <c>test/Sts2Headless.Tests.Domain/Combat/StateByteSerializer.cs</c>
/// (internal test surface) AND the
/// <c>UpstreamDriver.SerializeCanonical</c> in
/// <c>test/determinism-probe-upstream-capture/src/UpstreamDriver.cs</c>.
/// Field order: TurnCounter, Phase, Player creature (HP+Block+powers),
/// 2 padded enemy slots, extra enemies (if &gt; 2), Energy, draw/hand/discard/exhaust
/// counts. All ints little-endian.
/// </para>
///
/// <para>
/// <b>Why a sibling implementation:</b> we deliberately keep this comparison
/// path out of <see cref="CombatEngine.StartCombat"/> to avoid coupling the
/// production engine to comparison semantics. The byte recipe is small enough
/// (~50 lines) that duplicating it here is cheaper than threading a
/// "snapshot-only" mode through StartCombat.
/// </para>
/// </summary>
public sealed class UpstreamInitialStateComparer
{
    private readonly string _goldensRoot;

    /// <summary>Resolved Phase-1 content (Q1 catalogs).</summary>
    private readonly CardCatalog _cards;
    private readonly RelicCatalog _relics;
    private readonly PowerCatalog _powers;
    private readonly MonsterCatalog _monsters;
    private readonly EncounterCatalog _encounters;

    /// <summary>
    /// </summary>
    /// <param name="goldensRoot">
    /// Directory containing <c>&lt;encounter&gt;/&lt;seed&gt;.bin</c> entries
    /// (and <c>.missing</c> sentinels for MissingUpstream encounters).
    /// Conventionally <c>test/determinism-probe/goldens-upstream/initial-state/</c>.
    /// </param>
    public UpstreamInitialStateComparer(string goldensRoot)
    {
        ArgumentNullException.ThrowIfNull(goldensRoot);
        _goldensRoot = goldensRoot;
        _cards = Phase1Content.BuildCardCatalog();
        _relics = Phase1Content.BuildRelicCatalog();
        _powers = Phase1Content.BuildPowerCatalog();
        _monsters = Phase1Content.BuildMonsterCatalog();
        _encounters = Phase1Content.BuildEncounterCatalog();
    }

    /// <summary>Outcome of a single (encounter, seed) entry.</summary>
    public enum EntryOutcome
    {
        /// <summary>Q1's byte recipe matched the upstream golden byte-for-byte.</summary>
        Pass,
        /// <summary>Bytes differ. <see cref="EntryResult.Q1Bytes"/> + Golden + DiffSummary populated.</summary>
        Diverged,
        /// <summary>
        /// Encounter is tagged MissingUpstream in
        /// <c>test/determinism-probe-upstream-capture/src/EncounterCatalog.cs</c>.
        /// Not a failure — documented divergence per Stream-C-T2's
        /// "Q1 invented encounters that don't exist in upstream STS2".
        /// </summary>
        Skipped,
        /// <summary>Q1's construction threw, or the golden file is missing.</summary>
        Error,
    }

    /// <summary>Per-entry result returned by <see cref="CompareOne"/>.</summary>
    public sealed record EntryResult(
        string EncounterId,
        int Seed,
        EntryOutcome Outcome,
        byte[]? Q1Bytes,
        byte[]? GoldenBytes,
        string? DiffSummary,
        string? ErrorMessage);

    /// <summary>
    /// Run the comparison for one (encounter, seed) tuple. Returns the outcome,
    /// the bytes produced by Q1, the bytes loaded from the golden, and a diff
    /// summary on divergence.
    /// </summary>
    public EntryResult CompareOne(string encounterId, int seed)
    {
        ArgumentNullException.ThrowIfNull(encounterId);
        string encDir = Path.Combine(_goldensRoot, encounterId);
        string goldenPath = Path.Combine(encDir, $"{seed}.bin");
        string missingPath = goldenPath + ".missing";
        if (File.Exists(missingPath))
        {
            return new EntryResult(encounterId, seed, EntryOutcome.Skipped,
                Q1Bytes: null, GoldenBytes: null,
                DiffSummary: File.ReadAllText(missingPath),
                ErrorMessage: null);
        }
        if (!File.Exists(goldenPath))
        {
            return new EntryResult(encounterId, seed, EntryOutcome.Error,
                Q1Bytes: null, GoldenBytes: null,
                DiffSummary: null,
                ErrorMessage: $"golden missing: {goldenPath}");
        }

        byte[] golden = File.ReadAllBytes(goldenPath);
        byte[] q1Bytes;
        try
        {
            q1Bytes = BuildAndSerializeQ1((uint)seed, encounterId);
        }
        catch (Exception ex)
        {
            return new EntryResult(encounterId, seed, EntryOutcome.Error,
                Q1Bytes: null, GoldenBytes: golden,
                DiffSummary: null,
                ErrorMessage: $"Q1 construction threw: {ex.GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }

        if (q1Bytes.Length == golden.Length && q1Bytes.AsSpan().SequenceEqual(golden))
        {
            return new EntryResult(encounterId, seed, EntryOutcome.Pass,
                Q1Bytes: q1Bytes, GoldenBytes: golden,
                DiffSummary: null,
                ErrorMessage: null);
        }
        return new EntryResult(encounterId, seed, EntryOutcome.Diverged,
            Q1Bytes: q1Bytes, GoldenBytes: golden,
            DiffSummary: BuildDiffSummary(q1Bytes, golden),
            ErrorMessage: null);
    }

    /// <summary>
    /// Build Q1's post-SetUpCombat initial state and serialize it via the smoke-spec
    /// byte recipe (matching upstream-capture's output format).
    /// </summary>
    private byte[] BuildAndSerializeQ1(uint seed, string encounterId)
    {
        // Resolve encounter — matches Phase1Content registration (CultistsNormal etc.).
        // Use case-insensitive match to handle CLI snake_case vs PascalCase.
        IEncounterModel encounter = ResolveEncounter(encounterId);

        // B.1-alpha-T1+T2: build the determinism kernel exactly as Q1's host
        // does post-RC-2 / RC-3 fix: RunRngSet seeded by $"seed-{N}" via the
        // upstream byte-exact StringHelpers.GetDeterministicHashCode. HP
        // rolls consume from .Niche; deck shuffles from .Shuffle.
        var runRng = new RunRngSet($"seed-{seed}");

        // Build the Silent starter deck — matches CompositionRoot.ResolveDeck.
        // RC-1 fix (B.1-beta-T2): now 12 cards = 5 StrikeSilent + 5 DefendSilent
        // + Neutralize + Survivor, matching upstream
        // src/Core/Models/Characters/Silent.cs's StartingDeck byte-for-byte.
        IReadOnlyList<CardInstance> deck = BuildSilentStarterDeck();

        // Spawn enemies. Mirrors CombatEngine.StartCombat post-RC-4: uses
        // MonsterModel.RollUniqueInitialHp(rng, takenHps) so same-type spawns
        // get distinct HP values when the envelope allows it.
        var enemies = ImmutableList.CreateBuilder<Creature>();
        uint nextEnemyId = 1u;  // CombatEngine.FirstEnemyId
        foreach (string monsterId in encounter.MonsterIds)
        {
            var monsterModel = (MonsterModel)_monsters.Get(monsterId);
            var takenHps = new List<int>();
            foreach (var existing in enemies)
            {
                if (string.Equals(existing.Name, monsterId, StringComparison.Ordinal))
                {
                    takenHps.Add(existing.MaxHp);
                }
            }
            int hp = monsterModel.RollUniqueInitialHp(runRng.Niche, takenHps);
            enemies.Add(new Creature(
                Id: nextEnemyId,
                Name: monsterId,
                CurrentHp: hp,
                MaxHp: hp,
                Block: 0,
                Powers: ImmutableList<PowerInstance>.Empty,
                Intent: MonsterIntent.FromContentIntent(monsterModel.InitialIntent, monsterModel.InitialMoveId),
                IsPlayer: false));
            nextEnemyId++;
        }

        var player = new Creature(
            Id: 0u,                         // CombatEngine.PlayerId
            Name: "Silent",
            CurrentHp: 70,                  // CombatEngine.BaseMaxHpSilent
            MaxHp: 70,
            Block: 0,
            Powers: ImmutableList<PowerInstance>.Empty,
            Intent: null,
            IsPlayer: true);

        // Shuffle the deck into the draw pile (matches CombatEngine.StartCombat
        // lines ~158-163 post-T2: routes through .Shuffle bucket).
        var drawList = new List<CardInstance>(deck);
        runRng.Shuffle.Shuffle(drawList);
        CardPile drawPile = CardPile.OfRange(drawList);

        // Build the initial CombatState exactly as CombatEngine.StartCombat
        // does at line ~166 — pre-hook snapshot.
        var initial = new CombatState(
            TurnCounter: 0,
            Phase: CombatPhase.CombatStart,
            Player: player,
            Enemies: enemies.ToImmutable(),
            Energy: 0,
            BaseEnergyPerTurn: 3,            // CombatEngine.BaseEnergyPerTurnSilent
            HandDrawSize: 5,                 // CombatEngine.BaseHandDrawCount
            DrawPile: drawPile,
            HandPile: CardPile.Empty,
            DiscardPile: CardPile.Empty,
            ExhaustPile: CardPile.Empty,
            // PlayerRngCounter mirrors the .Shuffle counter — the in-combat
            // reshuffle path consumes from .Shuffle (matches CombatEngine
            // post-T2).
            PlayerRngCounter: runRng.GetCounter(RunRngType.Shuffle),
            MonsterRngCounter: 0);

        return SerializeSmokeBytes(initial);
    }

    /// <summary>
    /// Resolve the corpus encounter id to a registered <see cref="IEncounterModel"/>.
    /// Mirrors <c>CompositionRoot.ResolveEncounter</c>'s case-insensitive match.
    /// </summary>
    private IEncounterModel ResolveEncounter(string id)
    {
        foreach (string registeredId in _encounters.EnumerateIds())
        {
            if (string.Equals(registeredId, id, StringComparison.OrdinalIgnoreCase))
            {
                return (IEncounterModel)_encounters.Get(registeredId);
            }
        }
        throw new InvalidOperationException(
            $"encounter '{id}' not registered in Phase1Content.BuildEncounterCatalog().");
    }

    /// <summary>
    /// Silent starter deck per <c>CompositionRoot.ResolveDeck</c>. Returned as
    /// a fresh list so the caller can mutate it (e.g., shuffle). RC-1 fix:
    /// 12 cards = 5 StrikeSilent + 5 DefendSilent + Neutralize + Survivor
    /// matching upstream Silent.StartingDeck byte-for-byte.
    /// </summary>
    private static IReadOnlyList<CardInstance> BuildSilentStarterDeck()
    {
        var list = new List<CardInstance>(12);
        uint id = 100u;
        for (int i = 0; i < 5; i++)
        {
            list.Add(new CardInstance(id++, StrikeSilent.CanonicalId, 0, null));
        }
        for (int i = 0; i < 5; i++)
        {
            list.Add(new CardInstance(id++, DefendSilent.CanonicalId, 0, null));
        }
        list.Add(new CardInstance(id++, Neutralize.CanonicalId, 0, null));
        list.Add(new CardInstance(id++, Survivor.CanonicalId, 0, null));
        return list;
    }

    /// <summary>
    /// Serialize <paramref name="state"/> using the smoke-spec byte recipe
    /// (mirroring <c>StateByteSerializer</c> + <c>UpstreamDriver.SerializeCanonical</c>).
    /// </summary>
    private static byte[] SerializeSmokeBytes(CombatState state)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: false);

        bw.Write(state.TurnCounter);
        bw.Write((int)state.Phase);
        WriteCreature(bw, state.Player);

        // Match StateByteSerializer (test/Sts2Headless.Tests.Domain/Combat/
        // StateByteSerializer.cs) and UpstreamDriver.SerializeCanonical: write
        // each enemy in spawn order, then pad to 2 with empty creatures only
        // if count < 2. No extra loop for count > 2; the 3rd enemy is just
        // emitted by the main loop.
        for (int i = 0; i < state.Enemies.Count; i++)
        {
            WriteCreature(bw, state.Enemies[i]);
        }
        for (int i = state.Enemies.Count; i < 2; i++)
        {
            WriteEmptyCreature(bw);
        }

        bw.Write(state.Energy);
        bw.Write(state.DrawPile.Count);
        bw.Write(state.HandPile.Count);
        bw.Write(state.DiscardPile.Count);
        bw.Write(state.ExhaustPile.Count);

        bw.Flush();
        return ms.ToArray();
    }

    private static void WriteCreature(BinaryWriter bw, Creature c)
    {
        bw.Write(c.CurrentHp);
        bw.Write(c.Block);
        bw.Write(c.Powers.Count);
        for (int i = 0; i < c.Powers.Count; i++)
        {
            PowerInstance p = c.Powers[i];
            byte[] idBytes = Encoding.UTF8.GetBytes(p.ModelId);
            bw.Write(idBytes.Length);
            bw.Write(idBytes);
            bw.Write(p.Stacks);
            bw.Write(p.SourceCreatureId);
            bw.Write(p.JustApplied);
        }
    }

    private static void WriteEmptyCreature(BinaryWriter bw)
    {
        bw.Write(0);   // CurrentHp
        bw.Write(0);   // Block
        bw.Write(0);   // Powers.Count
    }

    /// <summary>
    /// Build a short, human-readable summary of where <paramref name="q1"/>
    /// and <paramref name="golden"/> diverge — file lengths plus the first
    /// few byte differences with field labels where possible.
    ///
    /// <para>
    /// Labels assume zero-power creatures (smoke baseline). When powers are
    /// present on either side the label scheme stops being accurate after
    /// the first power-bearing creature; the offset is still reported and
    /// the raw byte diff is enough to identify the divergence point.
    /// </para>
    /// </summary>
    private static string BuildDiffSummary(byte[] q1, byte[] golden)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"size: q1={q1.Length} golden={golden.Length}");

        // Field-offset map. Labels for the prefix (TurnCounter, Phase, Player,
        // up to 2 enemy slots) are fixed; trailing fields (Energy + 4 pile
        // counts) are decoded separately by DecodeTrailing() since their
        // offset depends on enemy count. For 3+ enemy encounters the
        // "Enemy[2..]" labels would be wrong relative to the smoke-spec's
        // 2-slot+extras shape, so we don't apply labels past offset 43.
        var labels = new Dictionary<int, string>
        {
            [0] = "TurnCounter",
            [4] = "Phase",
            [8] = "Player.CurrentHp",
            [12] = "Player.Block",
            [16] = "Player.Powers.Count",
            [20] = "Enemy[0].CurrentHp",
            [24] = "Enemy[0].Block",
            [28] = "Enemy[0].Powers.Count",
            [32] = "Enemy[1].CurrentHp",
            [36] = "Enemy[1].Block",
            [40] = "Enemy[1].Powers.Count",
        };

        int n = Math.Min(q1.Length, golden.Length);
        int diffs = 0;
        for (int offset = 0; offset < n && diffs < 12; offset += 4)
        {
            if (offset + 4 > n) break;
            int q1Val = BitConverter.ToInt32(q1, offset);
            int gVal = BitConverter.ToInt32(golden, offset);
            if (q1Val != gVal)
            {
                string label = labels.TryGetValue(offset, out string? l) ? l : $"@offset {offset}";
                sb.AppendLine($"  {label}: q1={q1Val} golden={gVal}");
                diffs++;
            }
        }
        if (diffs == 0 && q1.Length != golden.Length)
        {
            sb.AppendLine("  (per-field i32 check passed for common prefix; size mismatch is the divergence)");
        }

        // Trailing-fields decode using actual q1 layout (Energy + 4 pile counts
        // at the end). Provides exact labels regardless of enemy count.
        if (q1.Length >= 20)
        {
            int tail = q1.Length - 20;
            int gTail = golden.Length - 20;
            if (gTail < 0) gTail = 0;
            DecodeTrailing(sb, "q1.tail", q1, tail);
            if (golden.Length == q1.Length)
            {
                DecodeTrailing(sb, "golden.tail", golden, tail);
            }
            else if (golden.Length >= 20)
            {
                DecodeTrailing(sb, "golden.tail", golden, gTail);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Decode the trailing 5 i32 fields (Energy + 4 pile counts) of a smoke-spec
    /// byte stream into the provided <see cref="StringBuilder"/>.
    /// </summary>
    private static void DecodeTrailing(StringBuilder sb, string label, byte[] bytes, int trailingStart)
    {
        if (trailingStart < 0 || trailingStart + 20 > bytes.Length) return;
        int energy = BitConverter.ToInt32(bytes, trailingStart);
        int draw = BitConverter.ToInt32(bytes, trailingStart + 4);
        int hand = BitConverter.ToInt32(bytes, trailingStart + 8);
        int disc = BitConverter.ToInt32(bytes, trailingStart + 12);
        int exh = BitConverter.ToInt32(bytes, trailingStart + 16);
        sb.AppendLine($"  {label}: Energy={energy} Draw={draw} Hand={hand} Discard={disc} Exhaust={exh}");
    }
}
