// Steamworks vendor category — M8 stubs.
//
// Upstream files that import `Steamworks` namespace are platform / multiplayer code:
//   * Core/Multiplayer/Transport/Steam/* — DELETED per Q1-ADR-009 (strip multiplayer).
//   * Core/Platform/Steam/*              — single-player save / leaderboards.
//   * Core/Saves/SaveManager.cs           — Steam cloud-save coordination.
//
// Phase-1 model code (Cards/Relics/Powers/Monsters/Combat) does NOT touch Steamworks.
// We provide minimal categorical placeholders so the framework is in place and any
// inherited file that does `using Steamworks;` won't fail to find the namespace.
// Specific types are added reactively per R4 as upstream call sites are extracted.

using Sts2Headless.EngineStrip;

namespace Steamworks;

/// <summary>
/// Headless placeholder for the Steamworks namespace. Inert sentinel — calling any member
/// records under <see cref="StubCategory.Steamworks"/>. Specific Steam APIs (SteamUser,
/// SteamApps, SteamRemoteStorage, ...) are added as upstream code is extracted; until then
/// any reference will fail to compile, which is the correct gate per the spec ("CI rule
/// fails the build if a Godot-namespace API is called from outside M8").
/// </summary>
public static class SteamworksMarker
{
    /// <summary>Probe / smoke-test entry. Returns true. No I/O.</summary>
    public static bool IsInitialized()
    {
        StubRegistry.Record(
            StubCategory.Steamworks,
            nameof(SteamworksMarker),
            nameof(IsInitialized));
        return false;
    }
}
