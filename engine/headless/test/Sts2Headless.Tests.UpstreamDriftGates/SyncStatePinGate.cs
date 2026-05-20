using System;
using System.IO;
using System.Security.Cryptography;
using Sts2Headless.Tests.UpstreamDriftGates.Helpers;
using Xunit;

namespace Sts2Headless.Tests.UpstreamDriftGates;

/// <summary>
/// A.1 drift gate: cross-checks the pin file against <c>.upstream-sync-state.json</c>
/// and the live DLL SHA-256.
///
/// <para>
/// Three sub-assertions, each failing independently with a clear message:
/// <list type="number">
///   <item>
///     <c>upstream-pin.json:pinned_buildid</c> == <c>.upstream-sync-state.json:last_synced_buildid</c>.
///   </item>
///   <item>
///     <c>pinned_dll_sha256</c> matches <c>sha256sum ~/snap/steam/.../sts2.dll</c>.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Expected state on current main (274beca):</b> FAIL (assertion 1).
/// <c>upstream-pin.json:pinned_buildid</c> = <c>22823976</c> (v0.103.2) but
/// <c>.upstream-sync-state.json:last_synced_buildid</c> = <c>23156356</c> (v0.105.1).
/// Mismatch is intentional — bridge-in-progress signal per ADR-026. The failure
/// message says so explicitly.
/// </para>
///
/// <para>
/// <b>Skipped</b> (not silently passed) when the Steam install is absent, via
/// <c>Xunit.SkippableFact</c>.
/// </para>
/// </summary>
public sealed class SyncStatePinGate
{
    /// <summary>
    /// Assert <c>upstream-pin.json</c> build ID matches <c>.upstream-sync-state.json</c>.
    ///
    /// <para><b>EXPECTED: FAIL on current main.</b> See class docs.</para>
    /// </summary>
    [SkippableFact]
    public void PinBuildId_MatchesSyncState()
    {
        PinFile pin = PinFile.Load();

        SyncStateFile? syncState = SyncStateFile.TryLoad();
        Skip.If(
            syncState is null,
            "Steam install not present on this runner — .upstream-sync-state.json not found. "
                + "Skipped (not a silent pass) per A.1 gate semantics."
        );

        // EXPECTED FAILURE during bridge work; pin advances at Phase B end.
        // Pin: v0.103.2 (22823976). SyncState: v0.105.1 (23156356).
        Assert.True(
            string.Equals(
                pin.PinnedBuildId,
                syncState!.LastSyncedBuildId,
                StringComparison.Ordinal
            ),
            $"BUILD-ID MISMATCH — EXPECTED FAILURE during bridge work; pin advances at Phase B end.\n"
                + $"  upstream-pin.json:pinned_buildid   = {pin.PinnedBuildId} ({pin.PinnedVersion})\n"
                + $"  .upstream-sync-state.json:last_synced_buildid = {syncState.LastSyncedBuildId} ({syncState.LastSyncedVersion})\n"
                + $"\n"
                + $"  This mismatch is intentional: content is pinned at v0.103.2 (Phase-1A baseline)\n"
                + $"  while the sync-state tracks the most-recently-synced upstream (v0.105.1).\n"
                + $"  Gate passes when Phase B bridge work completes and both IDs advance together.\n"
                + $"  See ADR-026 for pin semantics."
        );
    }

    /// <summary>
    /// Assert <c>pinned_dll_sha256</c> matches the live Steam-install DLL.
    ///
    /// <para>
    /// Skipped when Steam install is absent. Fails with "DLL HASH DRIFT" if the
    /// DLL has been updated since the pin was written (e.g., Steam auto-updated
    /// the game on the dev machine).
    /// </para>
    /// </summary>
    [SkippableFact]
    public void PinDllSha256_MatchesLiveDll()
    {
        PinFile pin = PinFile.Load();

        string? dllPath = DllLocator.TryGetDllPath();
        Skip.If(
            dllPath is null,
            "Steam install not present on this runner — sts2.dll not found. "
                + "Skipped (not a silent pass) per A.1 gate semantics."
        );

        string actualSha = DllLocator.ComputeSha256(dllPath!);
        Assert.True(
            string.Equals(actualSha, pin.PinnedDllSha256, StringComparison.OrdinalIgnoreCase),
            $"DLL HASH DRIFT — live sts2.dll sha256 does not match upstream-pin.json.\n"
                + $"  upstream-pin.json:pinned_dll_sha256 = {pin.PinnedDllSha256}\n"
                + $"  sha256(live sts2.dll)               = {actualSha}\n"
                + $"  DLL path: {dllPath}\n"
                + $"  Pin version: {pin.PinnedVersion} (buildid {pin.PinnedBuildId})\n"
                + $"\n"
                + $"  If Steam auto-updated the game, bump upstream-pin.json to match the new DLL\n"
                + $"  (run tools/upstream-sync and follow ADR-026 pin-update procedure)."
        );
    }
}
