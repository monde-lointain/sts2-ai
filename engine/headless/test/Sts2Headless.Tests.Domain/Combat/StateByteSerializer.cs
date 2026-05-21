using System.IO;
using System.Text;
using Sts2Headless.Domain.Combat;

namespace Sts2Headless.Tests.Domain.Combat;

/// <summary>
/// Test-side byte serializer for <see cref="CombatState"/> used by the
/// reference-combat smoke harness. S7 (M1 State Codec) ships the production
/// version; this is a deliberately-stripped surface so we can compute
/// canonical hashes today without depending on S7 (which hasn't shipped
/// yet).
///
/// <para>
/// <b>Field order (per the S6 prompt's smoke spec):</b>
/// </para>
/// <list type="number">
///   <item>TurnCounter (i32 LE)</item>
///   <item>Phase (i32 LE)</item>
///   <item>Player HP (i32 LE)</item>
///   <item>Player Block (i32 LE)</item>
///   <item>Player power count (i32 LE) + per-power (id-len-LE | id-bytes | stacks-LE | source-LE | justApplied-byte)</item>
///   <item>Enemy[0] HP+Block+Powers (same shape)</item>
///   <item>Enemy[1] HP+Block+Powers</item>
///   <item>Energy (i32 LE)</item>
///   <item>DrawPile count (i32 LE)</item>
///   <item>HandPile count (i32 LE)</item>
///   <item>DiscardPile count (i32 LE)</item>
///   <item>ExhaustPile count (i32 LE)</item>
/// </list>
///
/// <para>
/// <b>NOT included (per smoke-spec):</b> card-instance identity (only counts).
/// S7 will include identity; this is deliberately reduced so the smoke test
/// surfaces real-world hash collisions if state structure changes
/// meaningfully.
/// </para>
/// </summary>
internal static class StateByteSerializer
{
    /// <summary>
    /// Compute canonical bytes for <paramref name="state"/> per the smoke
    /// spec. The output is fed to <c>CanonicalHash.Sha256Hex</c>.
    /// </summary>
    public static byte[] Serialize(CombatState state)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: false);

        bw.Write(state.TurnCounter);
        bw.Write((int)state.Phase);
        WriteCreature(bw, state.Player);

        // Enemies — fixed two-slot shape; if fewer enemies, pad with zeroes.
        for (int i = 0; i < state.Enemies.Count; i++)
        {
            WriteCreature(bw, state.Enemies[i]);
        }
        // Padding for hash stability when an enemy dies and is removed (we
        // don't remove enemies on death in S6, but defensive in case S12 does).
        for (int i = state.Enemies.Count; i < 2; i++)
        {
            WriteCreature(bw, EmptyCreaturePadding(i));
        }

        bw.Write(state.Energy);
        bw.Write(state.DrawPile.Count);
        bw.Write(state.HandPile.Count);
        bw.Write(state.DiscardPile.Count);
        bw.Write(state.ExhaustPile.Count);

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
            bw.Write(p.SourceCreatureId.Value);
            bw.Write(p.JustApplied);
        }
    }

    /// <summary>Stand-in creature for missing enemy slots in the smoke spec's 2-enemy schema.</summary>
    private static Creature EmptyCreaturePadding(int slotIndex) =>
        new(
            Id: new global::Sts2Headless.Domain.Combat.CreatureId(0xFFFFFFFFu),
            Name: $"__pad_{slotIndex}",
            CurrentHp: 0,
            MaxHp: 0,
            Block: 0,
            Powers: System.Collections.Immutable.ImmutableList<PowerInstance>.Empty,
            Intent: null,
            IsPlayer: false
        );
}
