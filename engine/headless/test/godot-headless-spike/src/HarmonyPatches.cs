// HarmonyPatches.cs — runtime IL patches for option-B spike.
// Applied at mod-load time via SpikeModInitializer.Initialize().
// These are in-memory patches ONLY; no files on disk are modified.
//
// Patch sites documented per README §Harmony patch sites contract.

using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace GodotHeadlessSpike;

/// <summary>
/// Patch #1: NGame.IsReleaseGame() — forces return false so the --autoslay
/// CLI path in NGame.GameStartup() is not gated behind release-mode check.
///
/// Upstream site: ~/development/projects/godot/sts2/src/Core/Nodes/NGame.cs:331
///   public static bool IsReleaseGame() { return true; }
/// Without this patch, the autoslay path (NGame.cs:275) is unreachable in
/// the shipped binary because IsReleaseGame() is hardcoded to true.
/// </summary>
[HarmonyPatch(typeof(NGame), nameof(NGame.IsReleaseGame))]
internal static class PatchIsReleaseGame
{
    private static bool Prefix(ref bool __result)
    {
        __result = false;
        return false; // skip original
    }
}
