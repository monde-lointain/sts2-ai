// 0Harmony / HarmonyLib vendor category — M8 stubs.
//
// Upstream usage (Core/Modding/ModManager.cs):
//   * HarmonyLib.Harmony            — runtime-patch utility class.
//   * Harmony.GetAllPatchedMethods  — query patched methods (returns IEnumerable<MethodBase>).
//   * Harmony.PatchAll(assembly)    — instance method that patches an assembly's [HarmonyPatch] types.
//
// Per `engine-strip.md` § Stub Categories: "0Harmony reflection hooks — used by upstream
// for runtime patching; mostly stubbed; selective forwarding only where modding integration
// requires it." Phase-1 has no mod-integration requirement, so this is pure no-op.

using System.Reflection;
using Sts2Headless.EngineStrip;

namespace HarmonyLib;

/// <summary>
/// Headless stub for HarmonyLib's <c>Harmony</c>. <see cref="PatchAll"/> is a no-op;
/// <see cref="GetAllPatchedMethods"/> returns empty. No reflection scan, no IL emission.
/// </summary>
public class Harmony
{
    public string Id { get; }

    public Harmony(string id)
    {
        Id = id ?? string.Empty;
        StubRegistry.Record(StubCategory.Harmony, nameof(Harmony), ".ctor", $"id={Id}");
    }

    public void PatchAll(Assembly? assembly = null)
    {
        StubRegistry.Record(
            StubCategory.Harmony,
            nameof(Harmony),
            nameof(PatchAll),
            $"asm={assembly?.GetName().Name ?? "(null)"}");
        // No-op: do not actually patch anything in headless. Modding hooks are out of scope
        // for Q1 (pipeline ADR-002).
    }

    public static IEnumerable<MethodBase> GetAllPatchedMethods()
    {
        StubRegistry.Record(StubCategory.Harmony, nameof(Harmony), nameof(GetAllPatchedMethods));
        return Array.Empty<MethodBase>();
    }
}
