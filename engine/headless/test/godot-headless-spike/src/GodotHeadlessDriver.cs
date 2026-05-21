// GodotHeadlessDriver.cs — entry point documentation for option-B spike.
// Explains the blocked execution path and why criteria #2-#6 are
// unreachable WITHOUT source-level modifications.
//
// This class is a documentation artifact — it does not execute.
// See README §Per-criterion findings for full analysis.

using System.Diagnostics;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.Nodes;

namespace GodotHeadlessSpike;

/// <summary>
/// Driver design intent (not runnable without upstream modification):
///
/// The ideal execution flow for criteria #2-#6 was:
///   1. Inject via mods/spike/ → SpikeModInitializer.Initialize() fires
///   2. Apply HarmonyPatch: NGame.IsReleaseGame() → false
///   3. Pass --autoslay --seed=SEED via CLI
///   4. NGame.GameStartup() reaches autoslay branch (line 275)
///   5. AutoSlayer.Start(seed) called → cultist combat runs to terminal
///   6. Stopwatch hooks on CombatRoomHandler.HandleAsync measure latency
///   7. 100 same-seed runs for determinism check (#6)
///
/// BLOCKED by:
///   - Criterion #2: AutoSlayer.PlayMainMenuAsync() calls UiHelper.Click()
///     on NButton nodes that require a real scene tree, not just headless.
///     In headless mode the game lacks a display driver. Node creation via
///     PackedScene.Instantiate() requires a rendering context for many UI
///     nodes. The main menu scene's NButton/NCharacterSelectButton depend
///     on theme resources and control sizing — headless rendering sets
///     CanvasItem.VisibleInTree() = false, breaking WaitHelper.ForNode()
///     and UiHelper.Click(). AutoSlayer.PlayMainMenuAsync() would hang
///     indefinitely at WaitHelper.Until(() => button.Visible, ...).
///
///   - This is a STRUCTURAL blocker, not a configuration issue. The AutoSlayer
///     architecture assumes a full UI scene tree. Stubbing all UI nodes via
///     Harmony would require patching 10+ node types (NButton, NOverlayStack,
///     NMapScreen, NCombatRoom, NModalContainer, etc.) — this constitutes
///     upstream source modification in spirit, even if done via IL patches.
///
/// CONCLUSION: Criteria #2-#6 are FAIL/UNREACHABLE without extensive
/// Harmony stubs of the full UI layer — which is outside option-B framing.
/// Option B is dead. See README for full verdict.
/// </summary>
public static class GodotHeadlessDriver
{
    // Latency measurement methodology (intended, per plan criterion #3):
    // Would use Stopwatch around CombatRoomHandler.HandleAsync per-turn.
    // Patch site: ~/development/projects/godot/sts2/src/Core/AutoSlay/
    //   Handlers/Rooms/CombatRoomHandler.cs:33 (HandleAsync entry)
    //   and :91 (PlayerCmd.EndTurn — turn end boundary)

    // GC measurement methodology (intended, per plan criterion #6):
    // GC.RegisterForFullGCNotification(10, 10) on sentinel thread
    // + Stopwatch-3σ-outlier detection per decision

    // Neither methodology is reachable given the #2 structural blocker.
}
