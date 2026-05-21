namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Allocates unique creature ids for mid-combat enemy spawns.
///
/// <para>
/// <b>Collision-free contract:</b> minted ids are strictly greater than every
/// existing creature id in the state (player + all enemies), so they can never
/// collide with ids assigned at combat-start. Because id 0 is the player and
/// ids 1..N are initial enemies, new ids start at <c>max(all existing ids) + 1</c>.
/// </para>
///
/// <para>
/// <b>Determinism contract:</b> given the same <see cref="CombatState"/> and the
/// same sequence of <see cref="Next"/> calls, the allocated ids are identical
/// across processes and re-serialized runs. The allocator derives its initial
/// counter entirely from the state — no external entropy.
/// </para>
///
/// <para>
/// <b>Immutability note:</b> the allocator is a lightweight, per-spawn-call
/// value; construct one, call <see cref="Next"/> once per spawned enemy, then
/// discard. The caller owns collecting the returned ids and building new
/// <see cref="Creature"/> records.
/// </para>
/// </summary>
public sealed class CreatureIdAllocator
{
    private uint _next;

    /// <summary>
    /// Construct an allocator seeded from <paramref name="state"/>. The first
    /// <see cref="Next"/> call returns <c>max(all creature ids in state) + 1</c>.
    /// </summary>
    public CreatureIdAllocator(CombatState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Start above the highest id currently in the state.
        uint maxId = state.Player.Id.Value;
        for (int i = 0; i < state.Enemies.Count; i++)
        {
            if (state.Enemies[i].Id.Value > maxId)
                maxId = state.Enemies[i].Id.Value;
        }
        _next = maxId + 1;
    }

    /// <summary>
    /// Mint the next unique creature id. Ids are allocated monotonically; each
    /// call returns a value strictly greater than the previous one.
    /// </summary>
    public CreatureId Next() => new(_next++);
}
