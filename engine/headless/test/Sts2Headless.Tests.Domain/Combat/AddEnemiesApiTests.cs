using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Combat;

/// <summary>
/// Tests for the mid-combat AddEnemies substrate: <see cref="CombatState.WithSpawnedEnemies"/>,
/// <see cref="CombatContext.AddEnemies"/>, and <see cref="CreatureIdAllocator"/>.
///
/// <para>
/// Wave-26/Q1.B — spawned-enemy substrate for SurprisePower (GremlinMerc OnDeath).
/// </para>
/// </summary>
public sealed class AddEnemiesApiTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static Creature MakePlayer(int hp = 70) =>
        new(
            Id: 0u,
            Name: "Silent",
            CurrentHp: hp,
            MaxHp: 70,
            Block: 0,
            Powers: ImmutableList<PowerInstance>.Empty,
            Intent: null,
            IsPlayer: true
        );

    private static Creature MakeEnemy(uint id, string name = "Enemy", int hp = 20) =>
        new(
            Id: id,
            Name: name,
            CurrentHp: hp,
            MaxHp: hp,
            Block: 0,
            Powers: ImmutableList<PowerInstance>.Empty,
            Intent: MonsterIntent.None,
            IsPlayer: false
        );

    private static CombatState MakeState(Creature? player = null, params Creature[] enemies) =>
        new(
            TurnCounter: 1,
            Phase: CombatPhase.PlayerActing,
            Player: player ?? MakePlayer(),
            Enemies: ImmutableList.CreateRange(enemies),
            Energy: 3,
            BaseEnergyPerTurn: 3,
            HandDrawSize: 5,
            DrawPile: CardPile.Empty,
            HandPile: CardPile.Empty,
            DiscardPile: CardPile.Empty,
            ExhaustPile: CardPile.Empty,
            PlayerRngCounter: 0,
            MonsterRngCounter: 0
        );

    private static CombatContext NewContext(CombatState state) =>
        new(
            initialState: state,
            runRng: new RunRngSet("test-seed"),
            clock: new LogicalClock(),
            cards: SmokeContent.BuildCardCatalog(),
            relics: SmokeContent.BuildRelicCatalog(),
            powers: SmokeContent.BuildPowerCatalog(),
            monsters: SmokeContent.BuildMonsterCatalog(),
            encounters: SmokeContent.BuildEncounterCatalog()
        );

    // =========================================================================
    // CombatState.WithSpawnedEnemies — base state API
    // =========================================================================

    [Fact]
    public void WithSpawnedEnemies_1_plus_2_yields_3_enemies()
    {
        var gremlinMerc = MakeEnemy(1u, "GremlinMerc", 30);
        var state = MakeState(null, gremlinMerc);

        var allocator = new CreatureIdAllocator(state);
        var sneaky = MakeEnemy(allocator.Next(), "SneakyGremlin", 10);
        var fat = MakeEnemy(allocator.Next(), "FatGremlin", 13);

        var after = state.WithSpawnedEnemies(new[] { sneaky, fat });

        Assert.Equal(3, after.Enemies.Count);
        Assert.Equal(1u, after.Enemies[0].Id); // GremlinMerc unchanged
        Assert.Equal("SneakyGremlin", after.Enemies[1].Name);
        Assert.Equal("FatGremlin", after.Enemies[2].Name);
    }

    [Fact]
    public void WithSpawnedEnemies_0_plus_3_yields_3_enemies()
    {
        var state = MakeState(); // no initial enemies

        var allocator = new CreatureIdAllocator(state);
        var e1 = MakeEnemy(allocator.Next(), "A");
        var e2 = MakeEnemy(allocator.Next(), "B");
        var e3 = MakeEnemy(allocator.Next(), "C");

        var after = state.WithSpawnedEnemies(new[] { e1, e2, e3 });

        Assert.Equal(3, after.Enemies.Count);
    }

    [Fact]
    public void WithSpawnedEnemies_does_not_mutate_original_state()
    {
        var enemy = MakeEnemy(1u, "GremlinMerc");
        var original = MakeState(null, enemy);

        var allocator = new CreatureIdAllocator(original);
        var spawned = MakeEnemy(allocator.Next(), "SneakyGremlin");
        _ = original.WithSpawnedEnemies(new[] { spawned });

        Assert.Single(original.Enemies);
    }

    [Fact]
    public void WithSpawnedEnemies_empty_sequence_returns_same_enemy_list()
    {
        var enemy = MakeEnemy(1u, "GremlinMerc");
        var state = MakeState(null, enemy);

        var after = state.WithSpawnedEnemies(Array.Empty<Creature>());

        Assert.Single(after.Enemies);
        Assert.Equal(state.Enemies[0], after.Enemies[0]);
    }

    [Fact]
    public void WithSpawnedEnemies_throws_on_IsPlayer_true()
    {
        var state = MakeState();
        var notAnEnemy = MakePlayer();

        Assert.Throws<ArgumentException>(() =>
            state.WithSpawnedEnemies(new[] { notAnEnemy })
        );
    }

    [Fact]
    public void WithSpawnedEnemies_throws_on_id_collision_with_existing_enemy()
    {
        var enemy = MakeEnemy(1u, "GremlinMerc");
        var state = MakeState(null, enemy);

        // Deliberately use id=1 again — must throw.
        var duplicate = MakeEnemy(1u, "SneakyGremlin");

        Assert.Throws<ArgumentException>(() =>
            state.WithSpawnedEnemies(new[] { duplicate })
        );
    }

    [Fact]
    public void WithSpawnedEnemies_throws_on_id_collision_with_player()
    {
        var state = MakeState(); // player.Id = 0

        // id=0 is the player — must throw.
        var collidesWithPlayer = MakeEnemy(0u, "Bad");

        Assert.Throws<ArgumentException>(() =>
            state.WithSpawnedEnemies(new[] { collidesWithPlayer })
        );
    }

    // =========================================================================
    // CreatureIdAllocator — id uniqueness + determinism
    // =========================================================================

    [Fact]
    public void Allocator_mints_ids_above_all_existing_ids()
    {
        var e1 = MakeEnemy(1u, "A");
        var e2 = MakeEnemy(2u, "B");
        var state = MakeState(null, e1, e2);

        var allocator = new CreatureIdAllocator(state);
        uint id1 = allocator.Next();
        uint id2 = allocator.Next();

        // Must be strictly greater than max existing id (2).
        Assert.True(id1 > 2u, $"Expected id1 > 2 but got {id1}");
        Assert.True(id2 > id1, $"Expected id2 > id1 but got {id2} vs {id1}");
    }

    [Fact]
    public void Allocator_ids_do_not_collide_with_player_id()
    {
        var state = MakeState(); // player.Id = 0, no enemies

        var allocator = new CreatureIdAllocator(state);
        uint id = allocator.Next();

        Assert.NotEqual(state.Player.Id, id);
    }

    [Fact]
    public void Allocator_is_deterministic_for_same_state()
    {
        var e1 = MakeEnemy(1u, "A");
        var state = MakeState(null, e1);

        var a1 = new CreatureIdAllocator(state);
        var a2 = new CreatureIdAllocator(state);

        Assert.Equal(a1.Next(), a2.Next());
        Assert.Equal(a1.Next(), a2.Next());
        Assert.Equal(a1.Next(), a2.Next());
    }

    [Fact]
    public void Allocator_repeated_calls_yield_unique_ids()
    {
        var e1 = MakeEnemy(1u, "A");
        var state = MakeState(null, e1);
        var allocator = new CreatureIdAllocator(state);

        var ids = new System.Collections.Generic.HashSet<uint>();
        for (int i = 0; i < 10; i++)
        {
            uint id = allocator.Next();
            Assert.True(ids.Add(id), $"Duplicate id {id} at iteration {i}");
        }
    }

    [Fact]
    public void Allocator_stable_across_serialize_deserialize_cycle_state()
    {
        // Allocator derives entirely from state fields — serializing and
        // rebuilding the state must yield the same allocation sequence.
        var e1 = MakeEnemy(3u, "Enemy3");
        var e2 = MakeEnemy(5u, "Enemy5"); // non-contiguous ids
        var state = MakeState(null, e1, e2);

        // Simulate a serialize/deserialize cycle by cloning via with-expression.
        var restored = state with { TurnCounter = state.TurnCounter };

        var allocator1 = new CreatureIdAllocator(state);
        var allocator2 = new CreatureIdAllocator(restored);

        uint id1a = allocator1.Next();
        uint id1b = allocator1.Next();
        uint id2a = allocator2.Next();
        uint id2b = allocator2.Next();

        Assert.Equal(id1a, id2a);
        Assert.Equal(id1b, id2b);
    }

    // =========================================================================
    // CombatContext.AddEnemies — mutable context surface
    // =========================================================================

    [Fact]
    public void Context_AddEnemies_appends_to_live_state()
    {
        var merc = MakeEnemy(1u, "GremlinMerc");
        var initialState = MakeState(null, merc);
        var ctx = NewContext(initialState);

        var allocator = new CreatureIdAllocator(ctx.State);
        var sneaky = MakeEnemy(allocator.Next(), "SneakyGremlin", 10);
        var fat = MakeEnemy(allocator.Next(), "FatGremlin", 13);

        ctx.AddEnemies(new[] { sneaky, fat });

        Assert.Equal(3, ctx.State.Enemies.Count);
        Assert.Equal("GremlinMerc", ctx.State.Enemies[0].Name);
        Assert.Equal("SneakyGremlin", ctx.State.Enemies[1].Name);
        Assert.Equal("FatGremlin", ctx.State.Enemies[2].Name);
    }

    [Fact]
    public void Context_AddEnemies_spawned_ids_unique_vs_player_and_existing()
    {
        var merc = MakeEnemy(1u, "GremlinMerc");
        var initialState = MakeState(null, merc);
        var ctx = NewContext(initialState);

        var allocator = new CreatureIdAllocator(ctx.State);
        var sneaky = MakeEnemy(allocator.Next(), "SneakyGremlin");
        var fat = MakeEnemy(allocator.Next(), "FatGremlin");

        ctx.AddEnemies(new[] { sneaky, fat });

        var allIds = ctx.State.Enemies.Select(e => e.Id).Prepend(ctx.State.Player.Id).ToList();
        Assert.Equal(allIds.Count, allIds.Distinct().Count());
    }

    [Fact]
    public void Context_AddEnemies_throws_on_null()
    {
        var state = MakeState();
        var ctx = NewContext(state);

        Assert.Throws<ArgumentNullException>(() => ctx.AddEnemies(null!));
    }

    // =========================================================================
    // GremlinMerc scenario: dead merc stays + 2 live spawns = 3 enemies
    // =========================================================================

    [Fact]
    public void Surprise_scenario_merc_dead_plus_2_spawns_yields_3_enemy_state()
    {
        // GremlinMerc dies (HP=0, stays in list per dead-enemy-stability contract).
        var mercDead = MakeEnemy(1u, "GremlinMerc", hp: 0);
        var state = MakeState(null, mercDead);

        var allocator = new CreatureIdAllocator(state);
        var sneaky = MakeEnemy(allocator.Next(), "SneakyGremlin", 10);
        var fat = MakeEnemy(allocator.Next(), "FatGremlin", 13);

        var after = state.WithSpawnedEnemies(new[] { sneaky, fat });

        Assert.Equal(3, after.Enemies.Count);
        Assert.Equal(0, after.Enemies[0].CurrentHp);  // dead merc retained
        Assert.Equal(10, after.Enemies[1].CurrentHp); // sneaky alive
        Assert.Equal(13, after.Enemies[2].CurrentHp); // fat alive
    }
}
