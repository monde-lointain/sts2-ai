using System.Text.Json;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;
using Sts2Headless.Host;

namespace Sts2Headless.Tests.Tools.Fixtures;

/// <summary>
/// Single source of truth for the six Q2 handoff fixture state-blobs at
/// <c>engine/headless/test/fixtures/state-blobs/</c>.
///
/// <para>
/// <b>What this owns:</b>
/// <list type="bullet">
///   <item>The fixture roster (slot number, dir name, seed, encounter id, role text).</item>
///   <item>The in-process boot procedure — drive
///     <see cref="CompositionRoot.Build(CliArgs)"/> for the (seed, encounter)
///     pair, capture the post-StartCombat state, package the codec carriers
///     using the SAME conventions as <see cref="FileProbeStream"/> so the
///     fixture is byte-identical with the probe stream's per-step recordings
///     of the same boot.</item>
///   <item>Hash + bytes computation — <c>CanonicalHash.Sha256Hex</c> over the
///     M1-serialized bytes; lowercase 64-char hex.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Reproducibility contract:</b> the regen test in <see cref="StateBlobFixtureRegressionTests"/>
/// re-invokes <see cref="ProduceBootBlob(int, string)"/> for every recipe and
/// asserts:
/// <list type="number">
///   <item>The blob bytes on disk equal the freshly-produced bytes (byte-for-byte).</item>
///   <item>The recorded <c>expected_canonical_hash_hex</c> in <c>metadata.json</c>
///         matches the freshly-computed hash.</item>
///   <item>The recorded <c>blob_bytes</c> in <c>metadata.json</c> matches the
///         actual file size.</item>
/// </list>
/// CI failure here means either Q1's M1 codec or one of its inputs drifted —
/// regenerate the fixtures (see <c>test/fixtures/state-blobs/README.md</c>).
/// </para>
///
/// <para>
/// <b>Why we share the recipe between the generator and the regression test:</b>
/// the test passes iff a clean Q1 boot produces byte-identical output. Sharing
/// the recipe keeps "what the fixture is" and "what the test asserts" wired to
/// the same code so they can't drift relative to each other.
/// </para>
/// </summary>
public static class StateBlobFixtureRecipe
{
    /// <summary>Describes one fixture slot.</summary>
    /// <param name="Number">1-based slot number; matches the directory ordinal.</param>
    /// <param name="DirName">Directory name under <c>test/fixtures/state-blobs/</c>.</param>
    /// <param name="Seed">CLI <c>--seed</c> value.</param>
    /// <param name="EncounterId">CLI <c>--encounter</c> value (PascalCase canonical id).</param>
    /// <param name="Role">Free-form role description recorded in <c>metadata.json</c>.</param>
    public sealed record Slot(
        int Number,
        string DirName,
        int Seed,
        string EncounterId,
        string Role
    );

    /// <summary>
    /// The eight fixture slots, project-lead approved. Order is the catalog order
    /// recorded in the README; do not reorder without updating the README.
    /// Slots 07 + 08 added in Wave-24/K.q1 for the Nibbit port.
    /// </summary>
    public static IReadOnlyList<Slot> AllSlots { get; } =
        new Slot[]
        {
            new(
                1,
                "01-cultists-normal-seed42",
                Seed: 42,
                EncounterId: "CultistsNormal",
                Role: "Smoke per-step gold standard (full Godot per-step parity). Indexed 2-monster slot."
            ),
            new(
                2,
                "02-fossil-stalker-elite-seed42",
                Seed: 42,
                EncounterId: "FossilStalkerElite",
                Role: "Elite, initial-state-only. Single-monster."
            ),
            new(
                3,
                "03-fossil-stalker-elite-seed1337",
                Seed: 1337,
                EncounterId: "FossilStalkerElite",
                Role: "Seed variation — tests Q2 seed-dependence for same encounter."
            ),
            new(
                4,
                "04-kaiser-crab-boss-seed42",
                Seed: 42,
                EncounterId: "KaiserCrabBoss",
                Role: "Phase-1 boss; spawns Crusher + Rocket. Exercises named-slot encoding (\"crusher\" / \"rocket\"); spawn-time powers reference ids absent from Phase-1 power catalog (escalated to Q2 S0 ADRs)."
            ),
            new(
                5,
                "05-louse-progenitor-normal-seed42",
                Seed: 42,
                EncounterId: "LouseProgenitorNormal",
                Role: "Single-monster non-smoke normal. Drop-in for the deleted TwoLouseNormal slot."
            ),
            new(
                6,
                "06-small-slimes-seed42",
                Seed: 42,
                EncounterId: "SmallSlimes",
                Role: "B.1-ε DEFER — encounter cannot run end-to-end in Q1 Phase-1A; encounter-RNG plumbing deferred. Initial-state only; stresses Q2 MissingUpstream path."
            ),
            new(
                7,
                "07-nibbits-weak-seed42",
                Seed: 42,
                EncounterId: "NibbitsWeak",
                Role: "Wave-24/K.q1 Nibbit port. Single-Nibbit encounter; initial move BUTT_MOVE. Q2 K.γ_setup prerequisite."
            ),
            new(
                8,
                "08-nibbits-normal-seed42",
                Seed: 42,
                EncounterId: "NibbitsNormal",
                Role: "Wave-24/K.q1 Nibbit port. 2-Nibbit encounter; slot-0=SLICE_MOVE, slot-1=HISS_MOVE. Q2 K.γ_setup prerequisite."
            ),
        };

    /// <summary>
    /// Stable build-id stamped into the manifest for every fixture. We do NOT
    /// pull this from the running build (CI != dev != reviewer), or the
    /// fixture bytes wouldn't be reproducible. Choose any constant; matches
    /// the probe-stream conventions in spirit.
    /// </summary>
    public const string FixtureBuildId = "Q1-Phase1-D3-handoff";

    /// <summary>
    /// Stable git-sha stamped into the manifest for every fixture. Same
    /// rationale as <see cref="FixtureBuildId"/>: must be a constant.
    /// </summary>
    public const string FixtureGitSha = "D3-handoff";

    /// <summary>
    /// Boot Q1 in-process for <paramref name="seed"/> + <paramref name="encounterId"/>,
    /// capture the post-StartCombat state, package it with the standard codec
    /// carriers, and return the resulting M1 blob bytes.
    ///
    /// <para>
    /// <b>Carrier conventions</b> (must match <see cref="FileProbeStream"/> at boot):
    /// </para>
    /// <list type="bullet">
    ///   <item><c>RunRngSet</c> + <c>PlayerRngSet</c> freshly re-seeded from
    ///     <c>$"seed-{N}"</c> / <c>seed:uint</c> respectively (NO advance —
    ///     this is the boot-time snapshot).</item>
    ///   <item><c>TokenMap</c> built by registering all catalog ids in canonical
    ///     order: Cards → Relics → Powers → Monsters → Encounters.</item>
    ///   <item><c>ManifestStamp</c>: <see cref="FixtureGitSha"/> +
    ///     <see cref="FixtureBuildId"/> + <c>ContentHashFromIds</c> over the
    ///     same id stream.</item>
    /// </list>
    /// </summary>
    public static byte[] ProduceBootBlob(int seed, string encounterId)
    {
        ArgumentNullException.ThrowIfNull(encounterId);
        if (seed < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seed),
                seed,
                "Seed must be a non-negative int (CLI is uint)."
            );
        }

        CliArgs args = new(
            Seed: (uint)seed,
            Character: "silent",
            Deck: "starter",
            Relics: new[] { "ring_of_the_snake" },
            Encounter: encounterId,
            Ascension: 0,
            MetricsPort: null,
            ScriptPath: null,
            OutPath: null,
            ProbeOutPath: null,
            RegistryPath: null
        );

        CompositionRoot.CompositionRootBundle bundle = CompositionRoot.Build(args);
        CombatState state = bundle.Context.State;

        // Fresh, non-advanced RNG carriers — the boot-time view. (FileProbeStream
        // does the same in its per-step emit path: it re-seeds rather than
        // serializing the live bucket counter, because the post-StartCombat RNG
        // state is what Q2 needs at boot — not the engine's in-progress bucket
        // counters.)
        RunRngSet runRng = new($"seed-{seed}");
        PlayerRngSet playerRng = new((uint)seed);

        // Token map and content hash: enumerate every catalog id in
        // registration order. ContentTable.EnumerateIds is deterministic.
        TokenMap tokens = new();
        List<string> ids = new();
        foreach (string id in bundle.Cards.EnumerateIds())
        {
            tokens.GetOrAddId(id);
            ids.Add(id);
        }
        foreach (string id in bundle.Relics.EnumerateIds())
        {
            tokens.GetOrAddId(id);
            ids.Add(id);
        }
        foreach (string id in bundle.Powers.EnumerateIds())
        {
            tokens.GetOrAddId(id);
            ids.Add(id);
        }
        foreach (string id in bundle.Monsters.EnumerateIds())
        {
            tokens.GetOrAddId(id);
            ids.Add(id);
        }
        foreach (string id in bundle.Encounters.EnumerateIds())
        {
            tokens.GetOrAddId(id);
            ids.Add(id);
        }

        ManifestStamp stamp = new(
            GitSha: FixtureGitSha,
            BuildId: FixtureBuildId,
            ContentHash: ManifestStamp.ContentHashFromIds(ids)
        );

        return StateCodec.Serialize(state, runRng, playerRng, tokens, stamp);
    }

    /// <summary>
    /// On-disk metadata JSON shape. Keys are pinned by the D3 spec; their
    /// presence in this record means a serializer change auto-renames will
    /// fail to compile rather than silently break the fixture file format.
    /// </summary>
    /// <param name="Seed">CLI seed.</param>
    /// <param name="EncounterId">CLI encounter id.</param>
    /// <param name="Role">Free-form role description.</param>
    /// <param name="ExpectedCanonicalHashHex">Lowercase-hex SHA-256 of the blob bytes.</param>
    /// <param name="BlobBytes">Byte count of the blob file.</param>
    public sealed record Metadata(
        int Seed,
        string EncounterId,
        string Role,
        string ExpectedCanonicalHashHex,
        int BlobBytes
    );

    /// <summary>
    /// Serialize a <see cref="Metadata"/> to the on-disk JSON shape. Keys are
    /// snake_case (matches metadata-style conventions in the contracts repo).
    /// </summary>
    public static string SerializeMetadata(Metadata m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var doc = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["seed"] = m.Seed,
            ["encounter_id"] = m.EncounterId,
            ["role"] = m.Role,
            ["expected_canonical_hash_hex"] = m.ExpectedCanonicalHashHex,
            ["blob_bytes"] = m.BlobBytes,
        };
        var opts = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(doc, opts);
    }

    /// <summary>
    /// Parse on-disk JSON back into a <see cref="Metadata"/>. Throws on
    /// missing keys.
    /// </summary>
    public static Metadata ParseMetadata(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        return new Metadata(
            Seed: root.GetProperty("seed").GetInt32(),
            EncounterId: root.GetProperty("encounter_id").GetString()!,
            Role: root.GetProperty("role").GetString()!,
            ExpectedCanonicalHashHex: root.GetProperty("expected_canonical_hash_hex").GetString()!,
            BlobBytes: root.GetProperty("blob_bytes").GetInt32()
        );
    }
}
