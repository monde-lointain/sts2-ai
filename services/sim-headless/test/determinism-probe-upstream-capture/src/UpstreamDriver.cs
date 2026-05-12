using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Sts2Headless.UpstreamCapture;

/// <summary>
/// Drives upstream <c>MegaCrit.Sts2</c> via reflection: constructs a Silent
/// run state, builds a CombatState with the requested encounter's monsters,
/// runs <c>CombatManager.SetUpCombat</c>, then serializes the post-SetUpCombat
/// snapshot using the same canonical byte format as Q1's
/// <c>StateByteSerializer</c>.
///
/// <para>
/// <b>Why reflection:</b> the upstream <c>sts2.csproj</c> drags in dozens of
/// types and we'd have to track them all in our .csproj if we used compile-time
/// references. Reflection keeps the .csproj surface small (just the assembly
/// references in <see cref="UpstreamCapture"/>'s csproj) at the cost of some
/// type-safety lost at the host level. The Domain stays in Q1, where we DO
/// have compile-time safety against StateByteSerializer.
/// </para>
///
/// <para>
/// <b>Scene-tree safety:</b> all reflection invocations target methods on
/// types reachable from <c>CombatManager.SetUpCombat</c>'s call chain (see
/// Stream-C-T1 inspection report). Trying to touch
/// <c>NRunMusicController.Instance</c>, <c>NCombatRoom.Instance</c>,
/// <c>NModalContainer.Instance</c>, <c>NCombatStartBanner</c>,
/// <c>Cmd.CustomScaledWait</c>, <c>SaveManager.Instance</c>,
/// <c>RunManager.Instance.ActionExecutor</c> would cause a
/// <see cref="TypeLoadException"/> or NRE the first time we tried to invoke
/// any. We intentionally never look any of those up.
/// </para>
/// </summary>
public sealed class UpstreamDriver
{
    private readonly Assembly _sts2;

    /// <summary>Exposed for diagnose-mode in <see cref="Program"/>.</summary>
    public Assembly Sts2Assembly => _sts2;

    /// <summary>
    /// Reflectively clear <c>CombatManager.Instance._state</c> so a subsequent
    /// <c>SetUpCombat</c> call doesn't throw "Make sure to reset the combat
    /// before setting up a new one". This is the minimum we need for batch
    /// mode — upstream's full <c>Reset(bool)</c> calls
    /// <c>RunManager.Instance.ActionQueueSynchronizer</c> (scene-tree gated)
    /// which we cannot satisfy headless. We only touch the one field that
    /// blocks the next <c>SetUpCombat</c>.
    /// </summary>
    public void ResetCombatManagerState()
    {
        Type combatManagerType = TypeOrThrow("MegaCrit.Sts2.Core.Combat.CombatManager");
        object instance = combatManagerType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)
            ?? throw new InvalidOperationException("CombatManager.Instance returned null.");
        FieldInfo stateField = combatManagerType.GetField("_state",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CombatManager._state field not found.");
        stateField.SetValue(instance, null);
    }

    /// <summary>Load the upstream sts2 assembly. Throws if it can't be found.</summary>
    public UpstreamDriver()
    {
        // Hook AssemblyResolve so the runtime can find GodotSharp.dll, 0Harmony.dll,
        // and the other upstream-bundled DLLs when sts2.dll references them.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromSteamDir;
        // Force-load sts2.dll. The MSBuild reference makes it available on
        // probing path; AppDomain.GetAssemblies() may not yet contain it.
        _sts2 = LoadSts2Assembly();
    }

    /// <summary>
    /// Resolve referenced upstream assemblies from the Steam install directory.
    /// </summary>
    private static Assembly? ResolveFromSteamDir(object? sender, ResolveEventArgs args)
    {
        string steamDir = SteamDir();
        string asmFile = new AssemblyName(args.Name).Name + ".dll";
        string path = Path.Combine(steamDir, asmFile);
        if (File.Exists(path))
        {
            try
            {
                return Assembly.LoadFrom(path);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static string SteamDir() => Environment.GetEnvironmentVariable("STEAM_STS2_DIR")
        ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "",
                        "snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64");

    private static Assembly LoadSts2Assembly()
    {
        // Try AppDomain first.
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(a.GetName().Name, "sts2", StringComparison.Ordinal))
            {
                return a;
            }
        }
        // Try loading by name.
        try
        {
            return Assembly.Load("sts2");
        }
        catch
        {
            // Try by path next to this binary.
            string? thisDir = Path.GetDirectoryName(typeof(UpstreamDriver).Assembly.Location);
            string steamDir = SteamDir();
            string sts2Path = Path.Combine(steamDir, "sts2.dll");
            if (File.Exists(sts2Path))
            {
                return Assembly.LoadFile(sts2Path);
            }
            if (thisDir is not null)
            {
                string colocated = Path.Combine(thisDir, "sts2.dll");
                if (File.Exists(colocated))
                {
                    return Assembly.LoadFile(colocated);
                }
            }
            throw new FileNotFoundException(
                $"Could not locate upstream sts2.dll. Set STEAM_STS2_DIR or place sts2.dll next to UpstreamCapture binary. Tried: {sts2Path}.");
        }
    }

    /// <summary>
    /// Capture canonical bytes for the (seed, encounter) tuple. Drives
    /// CombatManager.SetUpCombat then serializes the result.
    /// </summary>
    public byte[] Capture(int seed, EncounterCatalog.EncounterPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Kind != EncounterCatalog.PlanKind.UpstreamComparable)
        {
            throw new InvalidOperationException(
                $"Capture() called for non-comparable encounter '{plan.EncounterId}'; " +
                $"caller should have routed to MissingUpstream path. Reason: {plan.Reason}.");
        }

        // --- 0a. Turn on upstream's TestMode. This switches several singletons
        // (SaveManager, GodotFileIo) to mock variants that don't require the
        // Godot scene tree.
        Type testModeType = TypeOrThrow("MegaCrit.Sts2.Core.TestSupport.TestMode");
        testModeType.GetProperty("IsOn", BindingFlags.Public | BindingFlags.Static)!
            .SetValue(null, true);
        // Inject a fake SaveManager instance (private static field _mockInstance)
        // so Player ctor's `SaveManager.Instance?.Progress.GetStatsForCharacter`
        // line short-circuits to null without trying to construct the real one
        // (which would call Steam / Godot natives that segfault outside game
        // runtime).
        Type saveManagerType = TypeOrThrow("MegaCrit.Sts2.Core.Saves.SaveManager");
        FieldInfo mockField = saveManagerType.GetField("_mockInstance",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SaveManager._mockInstance field not found.");
        // Build a SaveManager via uninitialized object, then poke its
        // _progressSaveManager field with a ProgressSaveManager whose
        // Progress is the default ProgressState. Player ctor reads
        // `SaveManager.Instance?.Progress.GetStatsForCharacter(...)` which
        // chains through Progress; GetStatsForCharacter returns
        // CharacterStats? so null is acceptable downstream.
        object fakeSaveManager = System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(saveManagerType);
        Type progressSaveManagerType = TypeOrThrow("MegaCrit.Sts2.Core.Saves.Managers.ProgressSaveManager");
        object fakeProgressSaveManager = System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(progressSaveManagerType);
        // Set ProgressSaveManager.Progress backing field to default ProgressState.
        Type progressStateType = TypeOrThrow("MegaCrit.Sts2.Core.Saves.ProgressState");
        object defaultProgressState = progressStateType.GetMethod("CreateDefault",
            BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        FieldInfo progressBackingField = progressSaveManagerType.GetField(
            "<Progress>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ProgressSaveManager <Progress>k__BackingField not found.");
        progressBackingField.SetValue(fakeProgressSaveManager, defaultProgressState);
        // Plug into SaveManager._progressSaveManager.
        FieldInfo psmField = saveManagerType.GetField("_progressSaveManager",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("SaveManager._progressSaveManager field not found.");
        psmField.SetValue(fakeSaveManager, fakeProgressSaveManager);
        mockField.SetValue(null, fakeSaveManager);

        // --- 0. Bootstrap upstream's ModelDb. Without this, UnlockState.cctor
        // (and a hundred other type-inits) try to look up models by id from an
        // empty dictionary and throw. ModelDb.Init() instantiates every
        // AbstractModel subtype by reflection — it's the same call upstream
        // makes during game startup.
        EnsureModelDbInitialized();

        // --- 1. Build run state with Silent character ----------------------
        // Use the same stringSeed format Q1 uses in Host/FileProbeStream.cs:
        // $"seed-{int}". This ensures both sides feed the same uint hash into
        // RunRngSet, keeping the RNG sequence aligned.
        string stringSeed = $"seed-{seed}";

        // We need a Player but want to bypass UnlockState's static cctor
        // (which iterates ModelDb.AllEncounters and crashes when any of the
        // 90+ encounter classes isn't registered). Construct Player directly
        // via the private 10-arg ctor with a fresh UnlockState built the same
        // way — only ever touching its instance constructor, never its type.
        Type playerType = TypeOrThrow("MegaCrit.Sts2.Core.Entities.Players.Player");
        Type unlockStateType = TypeOrThrow("MegaCrit.Sts2.Core.Unlocks.UnlockState");
        Type silentType = TypeOrThrow("MegaCrit.Sts2.Core.Models.Characters.Silent");
        Type modelIdType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModelId");
        Type characterModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.CharacterModel");
        Type modelDbType_local = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModelDb");
        // The UnlockState ctor itself calls ModelDb.AllEncounters via the
        // encountersSeen hashset population. To dodge that, we BYPASS the ctor
        // using FormatterServices.GetUninitializedObject and then poke the
        // private fields to plausible empty defaults.
        object unlockStateInstance = MakeUninitializedUnlockState(unlockStateType, modelIdType);

        // Reset cached RNGs etc. that may have been left over from a previous
        // capture in the same process. Currently we run one capture per
        // process (the host CLI), so no reset is required, but if we ever
        // batch we'd need to reset CombatManager + NetCombatCardDb here.

        MethodInfo modelDbCharacterGeneric = modelDbType_local.GetMethod("Character",
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("ModelDb.Character not found.");
        object silentCharacter = modelDbCharacterGeneric.MakeGenericMethod(silentType).Invoke(null, null)
            ?? throw new InvalidOperationException("ModelDb.Character<Silent> returned null.");

        // Player has a private 10-arg ctor:
        //   Player(CharacterModel, ulong netId, int currentHp, int maxHp,
        //          int maxEnergy, int gold, int potionSlotCount, int orbSlotCount,
        //          RelicGrabBag, UnlockState, ...optional discovered lists)
        // We invoke it directly with Silent's defaults.
        int startingHp = ToInt(characterModelType.GetProperty("StartingHp")!.GetValue(silentCharacter)!);
        int maxEnergy = ToInt(characterModelType.GetProperty("MaxEnergy")!.GetValue(silentCharacter)!);
        int startingGold = ToInt(characterModelType.GetProperty("StartingGold")!.GetValue(silentCharacter)!);
        int orbSlotCount = ToInt(characterModelType.GetProperty("BaseOrbSlotCount")!.GetValue(silentCharacter)!);
        Type relicGrabBagType = TypeOrThrow("MegaCrit.Sts2.Core.Runs.RelicGrabBag");
        object relicGrabBag = Activator.CreateInstance(relicGrabBagType, args: new object[] { /* refreshAllowed: */ false })
            ?? throw new InvalidOperationException("RelicGrabBag ctor returned null.");

        ConstructorInfo? playerCtor = playerType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(c => c.GetParameters().Length == 15);
        if (playerCtor is null)
        {
            throw new InvalidOperationException(
                $"Player ctor with 15 params not found. Found these ctors: " +
                string.Join("; ", playerType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(c => $"({c.GetParameters().Length} params)")));
        }
        // Match the upstream Player ctor signature exactly:
        //   (CharacterModel, ulong, int, int, int, int, int, int, RelicGrabBag,
        //    UnlockState, List<ModelId>?, List<ModelId>?, List<string>?,
        //    List<ModelId>?, List<ModelId>?)
        object player = playerCtor.Invoke(new object?[]
        {
            silentCharacter, /* netId */ 1uL,
            /* currentHp */ startingHp, /* maxHp */ startingHp,
            /* maxEnergy */ maxEnergy, /* gold */ startingGold,
            /* potionSlotCount */ 3, /* orbSlotCount */ orbSlotCount,
            relicGrabBag, unlockStateInstance,
            /* discoveredCards */ null, /* discoveredEnemies */ null,
            /* discoveredEpochs */ null, /* discoveredPotions */ null,
            /* discoveredRelics */ null,
        });
        // Populate the player's starting inventory (mimic CreateForNewRun
        // which calls player.PopulateStartingInventory() after the ctor).
        MethodInfo populateInventoryMi = playerType.GetMethod("PopulateStartingInventory",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("PopulateStartingInventory not found.");
        populateInventoryMi.Invoke(player, null);

        // RunState.CreateForNewRun(players, ActModel.GetDefaultList(), [], GameMode.Standard, 0, seed)
        Type runStateType = TypeOrThrow("MegaCrit.Sts2.Core.Runs.RunState");
        Type actModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ActModel");
        Type modifierModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModifierModel");
        Type gameModeType = TypeOrThrow("MegaCrit.Sts2.Core.Runs.GameMode");
        object gameModeStandard = Enum.Parse(gameModeType, "Standard");
        object canonicalActs = actModelType.GetMethod("GetDefaultList", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, null)
            ?? throw new InvalidOperationException("ActModel.GetDefaultList returned null.");
        // RunState.CreateForNewRun asserts each act is mutable. Convert.
        MethodInfo actToMutableMi = actModelType.GetMethod("ToMutable",
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ActModel.ToMutable not found.");
        var actList = (System.Collections.IEnumerable)canonicalActs;
        Type listOfAct = typeof(List<>).MakeGenericType(actModelType);
        object mutableActs = Activator.CreateInstance(listOfAct)!;
        MethodInfo addActMi = listOfAct.GetMethod("Add")!;
        foreach (object a in actList)
        {
            object mutA = actToMutableMi.Invoke(a, null)!;
            addActMi.Invoke(mutableActs, new[] { mutA });
        }
        object acts = mutableActs;
        object emptyModifiers = Array.CreateInstance(modifierModelType, 0);

        // Convert player to IReadOnlyList<Player>.
        Type readOnlyListOfPlayer = typeof(IReadOnlyList<>).MakeGenericType(playerType);
        object players = MakeReadOnlyList(playerType, new[] { player });
        Type readOnlyListOfAct = typeof(IReadOnlyList<>).MakeGenericType(actModelType);
        Type readOnlyListOfModifier = typeof(IReadOnlyList<>).MakeGenericType(modifierModelType);
        // acts is already IReadOnlyList<ActModel> per GetDefaultList signature.

        MethodInfo createRunFromNew = runStateType.GetMethod("CreateForNewRun",
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("RunState.CreateForNewRun not found.");
        object runState = createRunFromNew.Invoke(null, new object[]
        {
            players, acts, emptyModifiers, gameModeStandard, /* ascensionLevel */ 0, stringSeed,
        }) ?? throw new InvalidOperationException("RunState.CreateForNewRun returned null.");

        // --- 2. Resolve the encounter list (Q1 invented encounters; we drive
        // by direct monster instantiation rather than EncounterModel.ToMutable
        // because Q1's encounter ids don't map 1:1 to upstream encounter
        // classes — see EncounterCatalog).
        Type combatStateType = TypeOrThrow("MegaCrit.Sts2.Core.Combat.CombatState");
        Type combatSideType = TypeOrThrow("MegaCrit.Sts2.Core.Combat.CombatSide");
        object combatSideEnemy = Enum.Parse(combatSideType, "Enemy");

        // Find IRunState and the modifiers/multiplayer fields.
        object multiplayerScalingModel = runStateType.GetProperty("MultiplayerScalingModel")!
            .GetValue(runState)!;
        object modifiers = runStateType.GetProperty("Modifiers")!.GetValue(runState)!;

        // new CombatState(encounter: null, runState, modifiers, multiplayerScalingModel)
        // (Encounter is optional, used only by certain hooks. We pass null and
        // let SetUpCombat call AddCreature for our pre-built monster list.)
        ConstructorInfo combatStateCtor = combatStateType.GetConstructors()
            .Single(c => c.GetParameters().Length == 4);
        object combatState = combatStateCtor.Invoke(new object?[]
        {
            null /* encounter */, runState, modifiers, multiplayerScalingModel,
        });

        // state.AddPlayer(player) — registers player.Creature in _allies.
        combatStateType.GetMethod("AddPlayer", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(combatState, new object[] { player });

        // --- 3. Spawn enemies via CombatState.CreateCreature.
        // We bypass EncounterModel.GenerateMonstersWithSlots because Q1 has
        // some encounters not 1:1 with upstream. Instead, instantiate each
        // monster class directly and call state.CreateCreature + AddCreature.
        Type monsterModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.MonsterModel");
        Type modelDbType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModelDb");
        MethodInfo modelDbMonsterGeneric = modelDbType.GetMethod("Monster",
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("ModelDb.Monster not found.");

        var monsters = new List<object>();
        for (int mi = 0; mi < plan.MonsterIds.Count; mi++)
        {
            string monsterId = plan.MonsterIds[mi];
            string? slot = plan.Slots[mi];

            // ModelDb.Monster<T>() — fetch canonical instance.
            Type monsterClassType = TypeOrThrow($"MegaCrit.Sts2.Core.Models.Monsters.{monsterId}");
            MethodInfo modelDbMonster = modelDbMonsterGeneric.MakeGenericMethod(monsterClassType);
            object canonicalMonster = modelDbMonster.Invoke(null, null)
                ?? throw new InvalidOperationException($"ModelDb.Monster<{monsterId}> returned null.");

            // monster.ToMutable() — get a mutable clone to add to combat.
            object mutableMonster = monsterModelType.GetMethod("ToMutable",
                    BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(canonicalMonster, null)
                ?? throw new InvalidOperationException($"{monsterId}.ToMutable returned null.");
            monsters.Add(mutableMonster);

            // creature = combatState.CreateCreature(monster, CombatSide.Enemy, slot)
            MethodInfo createCreatureMi = combatStateType.GetMethod("CreateCreature",
                BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("CombatState.CreateCreature not found.");
            object creature = createCreatureMi.Invoke(combatState,
                new object?[] { mutableMonster, combatSideEnemy, slot })
                ?? throw new InvalidOperationException($"CreateCreature returned null for {monsterId}.");

            // combatState.AddCreature(creature)
            combatStateType.GetMethod("AddCreature", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(combatState, new object[] { creature });
        }

        // --- 4. Invoke CombatManager.SetUpCombat ---------------------------
        Type combatManagerType = TypeOrThrow("MegaCrit.Sts2.Core.Combat.CombatManager");
        object combatManagerInstance = combatManagerType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)
            ?? throw new InvalidOperationException("CombatManager.Instance returned null.");

        // NOTE: We DON'T call CombatManager.Reset() — its last line calls
        // RunManager.Instance.ActionQueueSynchronizer (scene-tree singleton)
        // and NREs. Since we run one capture per process invocation, the
        // CombatManager state is fresh.
        InvokeMethod(combatManagerType, combatManagerInstance, "SetUpCombat",
            new object[] { combatState });

        // --- 5. Serialize canonical bytes ---------------------------------
        return SerializeCanonical(combatState, player, monsters);
    }

    /// <summary>
    /// Produce canonical bytes matching Q1's StateByteSerializer field order
    /// and shape (combat-state snapshot post-SetUpCombat, pre-StartCombatInternal).
    ///
    /// <para>
    /// Note: post-SetUpCombat the upstream state has:
    /// </para>
    /// <list type="bullet">
    ///   <item>TurnCounter ~= RoundNumber=1 (upstream's RoundNumber starts at 1)</item>
    ///   <item>Phase = PlayerActing-equivalent (upstream CurrentSide=Player)</item>
    ///   <item>Player.CurrentHp = StartingHp (no damage taken)</item>
    ///   <item>Player.Block = 0</item>
    ///   <item>Player.Powers = empty (no relic-applied powers yet)</item>
    ///   <item>Enemies: HP rolled per-monster by upstream RNG; Block=0; Powers=empty</item>
    ///   <item>Energy = 0 (StartCombatInternal's SetupPlayerTurn hasn't run)</item>
    ///   <item>DrawPile.Count = 12 (Silent starter; deck cloned + shuffled)</item>
    ///   <item>HandPile.Count = 0 / DiscardPile = 0 / ExhaustPile = 0</item>
    /// </list>
    /// </summary>
    private byte[] SerializeCanonical(object combatState, object player, IReadOnlyList<object> monsters)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: false);

        // TurnCounter (Q1 uses int starting at 0 pre-StartCombat / 1 post; for
        // post-SetUpCombat snapshot the canonical equivalent is 0 — we haven't
        // entered turn 1 yet.) Upstream uses RoundNumber which starts at 1.
        // For byte parity at the post-SetUpCombat snapshot point, we choose 0
        // (matching what Q1's `initial` CombatState looks like before its
        // L205-onward transition to TurnCounter=1).
        bw.Write((int)0);

        // Phase: Q1 uses CombatPhase enum (CombatStart=0). Write 0 to match
        // the post-SetUpCombat (pre-StartCombatInternal) snapshot.
        bw.Write((int)0);  // CombatPhase.CombatStart

        // Player creature
        object playerCreature = GetProperty(player, "Creature")!;
        int playerHp = ToInt(GetProperty(playerCreature, "CurrentHp")!);
        int playerBlock = ToInt(GetProperty(playerCreature, "Block")!);
        WriteCreature(bw, playerHp, playerBlock, GetPowers(playerCreature), playerSourceId: 0);

        // Enemies — match StateByteSerializer (test/Sts2Headless.Tests.Domain
        // /Combat/StateByteSerializer.cs) exactly: write each enemy in spawn
        // order, then pad to 2 with empty creatures only if count < 2. NO
        // extra-loop for count > 2 (that was an earlier bug that double-wrote
        // the 3rd enemy).
        Type combatStateType = combatState.GetType();
        object enemiesProp = GetProperty(combatState, "Enemies")!;
        var enemyCreatures = ((IEnumerable)enemiesProp).Cast<object>().ToList();
        for (int i = 0; i < enemyCreatures.Count; i++)
        {
            object ec = enemyCreatures[i];
            int hp = ToInt(GetProperty(ec, "CurrentHp")!);
            int block = ToInt(GetProperty(ec, "Block")!);
            WriteCreature(bw, hp, block, GetPowers(ec), playerSourceId: 0);
        }
        for (int i = enemyCreatures.Count; i < 2; i++)
        {
            WriteCreature(bw, currentHp: 0, block: 0,
                powers: Array.Empty<(string id, int stacks, uint source, bool justApplied)>(),
                playerSourceId: 0);
        }

        // Energy: post-SetUpCombat = 0 (StartCombatInternal not run).
        object playerCombatState = GetProperty(player, "PlayerCombatState")!;
        int energy = ToInt(GetProperty(playerCombatState, "Energy")!);
        bw.Write(energy);

        // Pile counts.
        object drawPile = GetProperty(playerCombatState, "DrawPile")!;
        object handPile = GetProperty(playerCombatState, "Hand")!;
        object discardPile = GetProperty(playerCombatState, "DiscardPile")!;
        object exhaustPile = GetProperty(playerCombatState, "ExhaustPile")!;
        bw.Write(PileCount(drawPile));
        bw.Write(PileCount(handPile));
        bw.Write(PileCount(discardPile));
        bw.Write(PileCount(exhaustPile));

        bw.Flush();
        return ms.ToArray();
    }

    private static void WriteCreature(BinaryWriter bw, int currentHp, int block,
        IReadOnlyList<(string id, int stacks, uint source, bool justApplied)> powers,
        uint playerSourceId)
    {
        bw.Write(currentHp);
        bw.Write(block);
        bw.Write(powers.Count);
        foreach (var p in powers)
        {
            byte[] idBytes = Encoding.UTF8.GetBytes(p.id);
            bw.Write(idBytes.Length);
            bw.Write(idBytes);
            bw.Write(p.stacks);
            bw.Write(p.source);
            bw.Write(p.justApplied);
        }
    }

    private static IReadOnlyList<(string id, int stacks, uint source, bool justApplied)> GetPowers(object creature)
    {
        // Upstream: creature.Powers : IReadOnlyList<Power>
        object? powersObj = GetProperty(creature, "Powers");
        if (powersObj is null) return Array.Empty<(string, int, uint, bool)>();
        var result = new List<(string id, int stacks, uint source, bool justApplied)>();
        foreach (object pw in (IEnumerable)powersObj)
        {
            object model = GetProperty(pw, "Model") ?? pw;
            string modelId = (GetProperty(model, "Id")?.ToString()) ?? model.GetType().Name;
            // Upstream Power has `Amount` (decimal); StateByteSerializer wants
            // `stacks` (int). Coerce.
            object? amount = GetProperty(pw, "Amount") ?? GetProperty(pw, "Stacks");
            int stacks = amount switch
            {
                int i => i,
                decimal d => (int)d,
                long l => (int)l,
                _ => 0,
            };
            // SourceCreatureId in Q1 schema; upstream uses Source : Creature?
            object? source = GetProperty(pw, "Source") ?? GetProperty(pw, "SourceCreatureId");
            uint sourceId = 0;
            if (source is not null)
            {
                object? cid = GetProperty(source, "CombatId");
                if (cid is not null && cid is not uint)
                {
                    cid = GetProperty(cid, "Value");
                }
                if (cid is uint u) sourceId = u;
            }
            bool justApplied = (GetProperty(pw, "JustApplied") as bool?) ?? false;
            result.Add((modelId, stacks, sourceId, justApplied));
        }
        return result;
    }

    private static int PileCount(object pile)
    {
        object cards = GetProperty(pile, "Cards")
            ?? throw new InvalidOperationException("CardPile.Cards not found.");
        if (cards is ICollection c) return c.Count;
        int count = 0;
        foreach (var _ in (IEnumerable)cards) count++;
        return count;
    }

    /// <summary>
    /// Inject every concrete subtype of the given base into ModelDb. Wraps
    /// each Inject in try/catch so a single bad-actor doesn't abort the
    /// batch. Used to populate the ModelDb dictionary before UnlockState's
    /// static cctor fires.
    /// </summary>
    private void InjectAllSubtypes(MethodInfo injectMi, string baseTypeName, string kind)
    {
        Type? baseType = _sts2.GetType(baseTypeName);
        if (baseType is null) return;
        var subtypes = _sts2.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t)
                        // Skip mock/test types — they tend to call into
                        // upstream's TestSupport which crashes outside the
                        // game runtime.
                        && !t.Namespace?.Contains("Mocks", StringComparison.Ordinal) is true
                        && !t.Name.StartsWith("Mock", StringComparison.Ordinal))
            .ToList();
        int succ = 0, fail = 0;
        foreach (Type t in subtypes)
        {
            try
            {
                injectMi.Invoke(null, new object?[] { t });
                succ++;
            }
            catch (Exception)
            {
                fail++;
            }
        }
        if (Environment.GetEnvironmentVariable("UPSTREAM_CAPTURE_VERBOSE") is not null)
        {
            Console.Error.WriteLine($"inject {kind}: {succ} ok, {fail} skipped (of {subtypes.Count})");
        }
    }

    /// <summary>
    /// Manufacture an "empty" <c>UnlockState</c> WITHOUT running its
    /// instance ctor (the ctor calls <c>encountersSeen.ToHashSet()</c> which
    /// would force <c>ModelDb.AllEncounters</c> evaluation if the input
    /// pulls from there — and the type's static cctor independently does the
    /// same when <c>UnlockState.all</c> is touched). We use
    /// <c>RuntimeHelpers.GetUninitializedObject</c> to allocate an instance
    /// without invoking any constructor, then poke the two private hashset
    /// fields to empty defaults.
    /// </summary>
    private static object MakeUninitializedUnlockState(Type unlockStateType, Type modelIdType)
    {
        object instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(unlockStateType);
        // Set the two private readonly fields to empty hashsets.
        FieldInfo epochsField = unlockStateType.GetField("_unlockedEpochIds",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("UnlockState._unlockedEpochIds field not found.");
        Type hashSetOfString = typeof(HashSet<>).MakeGenericType(typeof(string));
        object emptyEpochs = Activator.CreateInstance(hashSetOfString)!;
        epochsField.SetValue(instance, emptyEpochs);

        FieldInfo encField = unlockStateType.GetField("_encountersSeen",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("UnlockState._encountersSeen field not found.");
        Type hashSetOfModelId = typeof(HashSet<>).MakeGenericType(modelIdType);
        object emptyEnc = Activator.CreateInstance(hashSetOfModelId)!;
        encField.SetValue(instance, emptyEnc);

        // NumberOfRuns is a get-only auto-property; backing field is "<NumberOfRuns>k__BackingField".
        FieldInfo? runsBacking = unlockStateType.GetField("<NumberOfRuns>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);
        runsBacking?.SetValue(instance, 0);

        return instance;
    }

    /// <summary>
    /// Call <c>MegaCrit.Sts2.Core.Models.ModelDb.Init</c> (idempotent: tracks
    /// done locally to avoid the DuplicateModelException upstream's
    /// AbstractModel ctor throws on re-init).
    /// </summary>
    private bool _modelDbInitialized;
    private void EnsureModelDbInitialized()
    {
        if (_modelDbInitialized) return;
        Type modelDbType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModelDb");
        // ModelDb.Init iterates all AbstractModel subtypes and Activator.CreateInstance
        // each. Some constructors (Godot-bound resources, multiplayer types) crash
        // the runtime. We instead call Inject() for only the specific types we need
        // for SetUpCombat (Silent, the encounter's monsters, starter cards/relic, acts).
        // This skips the unsafe types entirely.
        MethodInfo injectMi = modelDbType.GetMethod("Inject",
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("ModelDb.Inject not found.");
        // The seed-types we need to populate. Each Inject() call also reaches
        // their AbstractModel-typed dependencies via the type's parameterless
        // ctor; we add the minimum set and rely on transitive registration.
        string[] seedTypeNames =
        {
            // Acts — referenced by ModelDb.Acts (UnlockState.cctor pulls this)
            "MegaCrit.Sts2.Core.Models.Acts.Overgrowth",
            "MegaCrit.Sts2.Core.Models.Acts.Hive",
            "MegaCrit.Sts2.Core.Models.Acts.Glory",
            "MegaCrit.Sts2.Core.Models.Acts.Underdocks",
            // Character
            "MegaCrit.Sts2.Core.Models.Characters.Silent",
            // Silent starter cards
            "MegaCrit.Sts2.Core.Models.Cards.StrikeSilent",
            "MegaCrit.Sts2.Core.Models.Cards.DefendSilent",
            "MegaCrit.Sts2.Core.Models.Cards.Neutralize",
            "MegaCrit.Sts2.Core.Models.Cards.Survivor",
            // Silent starter relic
            "MegaCrit.Sts2.Core.Models.Relics.RingOfTheSnake",
            // Card / relic / potion pools (referenced by character)
            "MegaCrit.Sts2.Core.Models.CardPools.SilentCardPool",
            "MegaCrit.Sts2.Core.Models.RelicPools.SilentRelicPool",
            "MegaCrit.Sts2.Core.Models.PotionPools.SilentPotionPool",
            // MultiplayerScalingModel singleton (touched by RunState.CreateShared)
            "MegaCrit.Sts2.Core.Models.Singleton.MultiplayerScalingModel",
        };
        foreach (string tn in seedTypeNames)
        {
            Type? t = _sts2.GetType(tn);
            if (t is not null)
            {
                try { injectMi.Invoke(null, new object?[] { t }); }
                catch (Exception ex) { /* log later */ Console.Error.WriteLine($"warn: Inject {tn}: {(ex.InnerException ?? ex).Message}"); }
            }
        }

        // To unblock UnlockState's cctor (which iterates ModelDb.AllEncounters
        // → all 4 acts' GenerateAllEncounters → ~88 encounter classes by
        // ModelDb.Encounter<X>()), inject every concrete encounter class in
        // the upstream assembly. Some construct cleanly; some (those touching
        // Godot resources at ctor) will fail — we skip those.
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.EncounterModel", "Encounter");
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.MonsterModel", "Monster");
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.EventModel", "Event");
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.AncientEventModel", "Ancient");
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.PowerModel", "Power");
        // NOTE: We DON'T call ModelDb.InitIds() — it iterates _contentById and
        // calls ModelIdSerializationCache.GetNetIdForCategory("ACHIEVEMENT")
        // (and similar), which the cache only knows about after upstream's
        // multiplayer subsystem has registered them at process startup. Since
        // we don't need net-ids for combat-state capture (no multiplayer
        // serialization happens in our path), we skip InitIds and let
        // AbstractModel's CategorySortingId/EntrySortingId stay at 0. The
        // SetUpCombat code path doesn't read those fields.
        _modelDbInitialized = true;
    }

    // ===== Reflection helpers ============================================

    private Type TypeOrThrow(string fullName)
    {
        Type? t = _sts2.GetType(fullName);
        if (t is null)
        {
            throw new InvalidOperationException($"Upstream type '{fullName}' not found in sts2.dll.");
        }
        return t;
    }

    private static object GetStaticProperty(Type t, string name)
    {
        PropertyInfo? p = t.GetProperty(name, BindingFlags.Static | BindingFlags.Public);
        if (p is null)
        {
            FieldInfo? f = t.GetField(name, BindingFlags.Static | BindingFlags.Public);
            if (f is null)
            {
                throw new InvalidOperationException($"Static member '{name}' on {t.FullName} not found.");
            }
            return f.GetValue(null)
                ?? throw new InvalidOperationException($"Static field {t.FullName}.{name} is null.");
        }
        return p.GetValue(null)
            ?? throw new InvalidOperationException($"Static property {t.FullName}.{name} is null.");
    }

    private static object? GetProperty(object instance, string name)
    {
        Type t = instance.GetType();
        PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (p is not null) return p.GetValue(instance);
        FieldInfo? f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        return f?.GetValue(instance);
    }

    private static void InvokeMethod(Type t, object? instance, string name, object?[] args)
    {
        MethodInfo? mi = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.Name == name && m.GetParameters().Length == args.Length)
            .FirstOrDefault();
        if (mi is null)
        {
            throw new InvalidOperationException($"{t.FullName}.{name}({args.Length} args) not found.");
        }
        mi.Invoke(instance, args);
    }

    private static int ToInt(object value) => value switch
    {
        int i => i,
        long l => (int)l,
        uint u => (int)u,
        decimal d => (int)d,
        _ => Convert.ToInt32(value),
    };

    private static object MakeReadOnlyList(Type elementType, object[] items)
    {
        // Build a List<T> of the element type and return it (works for
        // IReadOnlyList<T> parameters via covariance).
        Type listType = typeof(List<>).MakeGenericType(elementType);
        object list = Activator.CreateInstance(listType)!;
        MethodInfo addMethod = listType.GetMethod("Add")!;
        foreach (object item in items)
        {
            addMethod.Invoke(list, new[] { item });
        }
        return list;
    }
}
