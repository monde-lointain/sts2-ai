using System.Globalization;
using System.Text;
using System.Text.Json;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tools.StateBlobDumper;

/// <summary>
/// <para>
/// Debugging companion for the M1 State Codec: reads an
/// <c>engine/headless/test/fixtures/state-blobs/&lt;slot&gt;/state.blob</c>
/// file (or any blob produced by <see cref="StateCodec.Serialize"/>) and emits
/// a human-readable AND machine-parseable JSONL stream describing the
/// envelope and every section's contents.
/// </para>
///
/// <para>
/// <b>Output format</b> (one JSON object per line):
/// </para>
/// <list type="bullet">
///   <item><c>kind: "envelope"</c> — schema version + 5-tuple manifest stamp
///         + trailer-validation flag.</item>
///   <item><c>kind: "section"</c> — one per decoded section, with
///         <c>id</c> (numeric), <c>name</c> (enum string), <c>size_bytes</c>,
///         and a section-specific <c>body</c> object.</item>
///   <item><c>kind: "canonical-hash"</c> — final line: lowercase-hex
///         SHA-256 of the entire blob (matches the recipe used by the Q2
///         handoff fixtures' <c>expected_canonical_hash_hex</c>).</item>
/// </list>
///
/// <para>
/// Section bodies (Phase 1):
/// </para>
/// <list type="bullet">
///   <item><c>Rng</c> body — sizes of run / player blobs (the inner M5 bytes
///         are opaque from the codec's view, so we surface just enough to
///         distinguish "decoded" from "garbage").</item>
///   <item><c>Tokens</c> body — count + a deterministic preview of the first
///         N (token, id) pairs in insertion order.</item>
///   <item><c>CombatState</c> body — turn / phase / energy / player + enemy
///         summaries + pile sizes + per-creature power summaries.</item>
/// </list>
///
/// <para>
/// <b>Exit code:</b> 0 on success, 1 on usage error, 2 on decode error. The
/// process never throws to the caller; all errors are translated into
/// structured stderr JSON.
/// </para>
/// </summary>
public static class Program
{
    public const int ExitOk = 0;
    public const int ExitUsage = 1;
    public const int ExitDecode = 2;

    public const string UsageText =
        "Usage: StateBlobDumper <path-to-state.blob>\n"
        + "Reads the blob produced by Sts2Headless.Adapters.StateCodec.StateCodec.Serialize\n"
        + "and emits JSONL describing the envelope + per-section pretty-print to stdout.";

    public static int Main(string[] args)
    {
        // Defer to the testable Run entrypoint so the integration tests can
        // exercise the same code path without spawning a subprocess.
        return Run(args, Console.Out, Console.Error);
    }

    /// <summary>
    /// Testable entrypoint. Returns an exit code; never throws.
    /// </summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Length != 1)
        {
            stderr.WriteLine(UsageText);
            return ExitUsage;
        }
        string path = args[0];
        if (string.IsNullOrWhiteSpace(path))
        {
            stderr.WriteLine(UsageText);
            return ExitUsage;
        }
        if (!File.Exists(path))
        {
            EmitError(stderr, "file_not_found", $"No file at '{path}'.");
            return ExitUsage;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            EmitError(stderr, "read_failed", ex.Message);
            return ExitDecode;
        }

        StateBlob blob;
        try
        {
            blob = StateCodec.Deserialize(bytes);
        }
        catch (StateCodecException ex)
        {
            EmitError(stderr, "state_codec_exception", ex.Message);
            return ExitDecode;
        }

        EmitEnvelopeLine(stdout, blob, path, bytes.Length);
        foreach (StateSection section in blob.Sections)
        {
            EmitSectionLine(stdout, blob, section);
        }
        EmitCanonicalHashLine(stdout, bytes);

        return ExitOk;
    }

    // === Emit helpers =====================================================

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        // Stable key ordering is enforced manually below — System.Text.Json
        // emits keys in property-insertion order for Dictionary<string, ?>,
        // so we use a Dictionary throughout and add keys in canonical order.
    };

    private static void EmitEnvelopeLine(
        TextWriter stdout,
        StateBlob blob,
        string path,
        int blobBytes
    )
    {
        var doc = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = "envelope",
            ["path"] = path,
            ["blob_bytes"] = blobBytes,
            ["schema_version"] = (int)blob.SchemaVersion,
            ["trailer_validated"] = blob.TrailerValidated,
            ["manifest_stamp"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["git_sha"] = blob.Stamp.GitSha,
                ["build_id"] = blob.Stamp.BuildId,
                ["content_hash_hex"] = Convert.ToHexStringLower(blob.Stamp.ContentHash),
            },
            ["section_count"] = blob.Sections.Count,
        };
        stdout.WriteLine(JsonSerializer.Serialize(doc, JsonOpts));
    }

    private static void EmitSectionLine(TextWriter stdout, StateBlob blob, StateSection section)
    {
        var doc = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = "section",
            ["id"] = (int)section.Id,
            ["name"] = section.Id.ToString(),
            ["size_bytes"] = section.Bytes.Length,
            ["body"] = BodyFor(blob, section),
        };
        stdout.WriteLine(JsonSerializer.Serialize(doc, JsonOpts));
    }

    private static void EmitCanonicalHashLine(TextWriter stdout, byte[] bytes)
    {
        var doc = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = "canonical-hash",
            ["sha256_hex"] = CanonicalHash.Sha256Hex(bytes),
        };
        stdout.WriteLine(JsonSerializer.Serialize(doc, JsonOpts));
    }

    private static void EmitError(TextWriter stderr, string code, string message)
    {
        var doc = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = "error",
            ["code"] = code,
            ["message"] = message,
        };
        stderr.WriteLine(JsonSerializer.Serialize(doc, JsonOpts));
    }

    // === Per-section bodies ==============================================

    /// <summary>Project a section into a structured body object. Falls back
    /// to <c>{"unknown": true, "size_bytes": N}</c> for ids the dumper doesn't
    /// understand (Phase 2+ sections appear here until the dumper learns them).</summary>
    private static object BodyFor(StateBlob blob, StateSection section)
    {
        try
        {
            return section.Id switch
            {
                SectionId.Rng => RngBody(blob),
                SectionId.Tokens => TokensBody(blob),
                SectionId.CombatState => CombatStateBody(blob),
                _ => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["unknown"] = true,
                    ["size_bytes"] = section.Bytes.Length,
                },
            };
        }
        catch (StateCodecException ex)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["decode_error"] = ex.Message,
                ["size_bytes"] = section.Bytes.Length,
            };
        }
    }

    private static object RngBody(StateBlob blob)
    {
        (RunRngSet run, PlayerRngSet player) = StateCodec.ToRngBundle(blob);
        // M5 RNG buckets are opaque from M1's view; we surface the seed
        // identifiers so a human can sanity-check the boot at-a-glance.
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["run_rng_seed"] = run.Seed,
            ["player_rng_seed"] = player.Seed,
        };
    }

    private static object TokensBody(StateBlob blob)
    {
        TokenMap map = StateCodec.ToTokenMap(blob);
        const int previewCount = 8;
        var preview = new List<Dictionary<string, object?>>(previewCount);
        int i = 0;
        foreach ((string tok, int id) in map.Enumerate())
        {
            if (i >= previewCount)
                break;
            preview.Add(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["token"] = tok,
                    ["id"] = id,
                }
            );
            i++;
        }
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["count"] = map.Count,
            ["preview"] = preview,
        };
    }

    private static object CombatStateBody(StateBlob blob)
    {
        CombatState state = StateCodec.ToCombatState(blob);
        var enemies = new List<object>(state.Enemies.Count);
        foreach (Creature e in state.Enemies)
        {
            enemies.Add(CreatureSummary(e));
        }
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["turn_counter"] = state.TurnCounter,
            ["phase"] = state.Phase.ToString(),
            ["energy"] = state.Energy,
            ["base_energy_per_turn"] = state.BaseEnergyPerTurn,
            ["hand_draw_size"] = state.HandDrawSize,
            ["player_rng_counter"] = state.PlayerRngCounter,
            ["monster_rng_counter"] = state.MonsterRngCounter,
            ["attacks_played_this_turn"] = state.AttacksPlayedThisTurn,
            ["cards_drawn_this_combat"] = state.CardsDrawnThisCombat,
            ["last_spent_energy"] = state.LastSpentEnergy,
            ["exhausted_shiv_count"] = state.ExhaustedShivCount,
            ["player"] = CreatureSummary(state.Player),
            ["enemy_count"] = state.Enemies.Count,
            ["enemies"] = enemies,
            ["pile_sizes"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["draw"] = state.DrawPile.Cards.Count,
                ["hand"] = state.HandPile.Cards.Count,
                ["discard"] = state.DiscardPile.Cards.Count,
                ["exhaust"] = state.ExhaustPile.Cards.Count,
            },
        };
    }

    private static object CreatureSummary(Creature c)
    {
        var powers = new List<object>(c.Powers.Count);
        foreach (PowerInstance p in c.Powers)
        {
            powers.Add(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["model_id"] = p.ModelId,
                    ["stacks"] = p.Stacks,
                    ["source_creature_id"] = (long)p.SourceCreatureId,
                    ["just_applied"] = p.JustApplied,
                }
            );
        }
        var summary = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = (long)c.Id,
            ["name"] = c.Name,
            ["current_hp"] = c.CurrentHp,
            ["max_hp"] = c.MaxHp,
            ["block"] = c.Block,
            ["is_player"] = c.IsPlayer,
            ["power_count"] = c.Powers.Count,
            ["powers"] = powers,
        };
        if (c.Intent is not null)
        {
            summary["intent"] = IntentSummary(c.Intent);
        }
        return summary;
    }

    private static object IntentSummary(MonsterIntent m)
    {
        var applies = new List<object>(m.AppliesPowers.Count);
        foreach (MonsterIntentPower p in m.AppliesPowers)
        {
            applies.Add(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["power_id"] = p.PowerId,
                    ["stacks"] = p.Stacks,
                }
            );
        }
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = m.Kind.ToString(),
            ["damage_per_hit"] = m.DamagePerHit,
            ["hit_count"] = m.HitCount,
            ["move_id"] = m.MoveId,
            ["applies_powers"] = applies,
        };
    }
}
