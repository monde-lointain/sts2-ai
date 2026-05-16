// Per-category inertia tests for Sentry / Steamworks / Vortice / 0Harmony vendor stubs.

using HarmonyLib;
using Sentry;
using Steamworks;
using Sts2Headless.EngineStrip;
using Vortice.DXGI;

namespace Sts2Headless.Tests.EngineStrip;

[Collection("StubRegistry")]
public class SentryStubsTests
{
    public SentryStubsTests() => StubRegistry.Reset();

    [Fact]
    public void Scope_TagsAndExtras_AreInertAndRecorded()
    {
        using var capture = StubRegistry.Capture();
        var scope = new Scope();
        scope.SetTag("k", "v");
        scope.SetExtra("k2", 42);

        Assert.Contains(
            capture.Hits,
            h => h.Type == nameof(Scope) && h.Member == nameof(Scope.SetTag)
        );
        Assert.Contains(
            capture.Hits,
            h => h.Type == nameof(Scope) && h.Member == nameof(Scope.SetExtra)
        );
        Assert.Contains(StubCategory.Sentry, capture.Categories);
    }

    [Fact]
    public void SentrySdk_CaptureMessage_ReturnsEmptyId_And_InvokesScopeConfigurator()
    {
        using var capture = StubRegistry.Capture();
        bool configured = false;
        var id = SentrySdk.CaptureMessage(
            "boom",
            s =>
            {
                configured = true;
                s.SetTag("env", "test");
            },
            SentryLevel.Error
        );

        Assert.Equal(SentryId.Empty, id);
        Assert.True(configured);
        Assert.Contains(
            capture.Hits,
            h => h.Type == nameof(SentrySdk) && h.Member == nameof(SentrySdk.CaptureMessage)
        );
        Assert.Contains(
            capture.Hits,
            h => h.Type == nameof(Scope) && h.Member == nameof(Scope.SetTag)
        );
    }

    [Fact]
    public void SentrySdk_CaptureException_ReturnsEmptyId()
    {
        using var capture = StubRegistry.Capture();
        var id1 = SentrySdk.CaptureException(new InvalidOperationException());
        var id2 = SentrySdk.CaptureException(new InvalidOperationException(), _ => { });
        Assert.Equal(SentryId.Empty, id1);
        Assert.Equal(SentryId.Empty, id2);
        Assert.Contains(capture.Hits, h => h.Member == nameof(SentrySdk.CaptureException));
    }
}

[Collection("StubRegistry")]
public class SteamworksStubsTests
{
    public SteamworksStubsTests() => StubRegistry.Reset();

    [Fact]
    public void SteamworksMarker_IsInitialized_ReturnsFalse_AndRecords()
    {
        using var capture = StubRegistry.Capture();
        Assert.False(SteamworksMarker.IsInitialized());
        Assert.Contains(
            capture.Hits,
            h =>
                h.Type == nameof(SteamworksMarker)
                && h.Member == nameof(SteamworksMarker.IsInitialized)
        );
        Assert.Contains(StubCategory.Steamworks, capture.Categories);
    }
}

[Collection("StubRegistry")]
public class VorticeStubsTests
{
    public VorticeStubsTests() => StubRegistry.Reset();

    [Fact]
    public void VorticeMarker_IsAvailable_ReturnsFalse_AndRecords()
    {
        using var capture = StubRegistry.Capture();
        Assert.False(VorticeMarker.IsAvailable());
        Assert.Contains(
            capture.Hits,
            h => h.Type == nameof(VorticeMarker) && h.Member == nameof(VorticeMarker.IsAvailable)
        );
        Assert.Contains(StubCategory.Vortice, capture.Categories);
    }
}

[Collection("StubRegistry")]
public class HarmonyStubsTests
{
    public HarmonyStubsTests() => StubRegistry.Reset();

    [Fact]
    public void Harmony_PatchAll_AndGetAllPatchedMethods_AreInert()
    {
        using var capture = StubRegistry.Capture();
        var h = new Harmony("test.mod");
        Assert.Equal("test.mod", h.Id);

        h.PatchAll();
        h.PatchAll(typeof(HarmonyStubsTests).Assembly);
        var patched = Harmony.GetAllPatchedMethods();

        Assert.NotNull(patched);
        Assert.Empty(patched);
        Assert.Contains(
            capture.Hits,
            hh => hh.Type == nameof(Harmony) && hh.Member == nameof(Harmony.PatchAll)
        );
        Assert.Contains(
            capture.Hits,
            hh => hh.Type == nameof(Harmony) && hh.Member == nameof(Harmony.GetAllPatchedMethods)
        );
        Assert.Contains(StubCategory.Harmony, capture.Categories);
    }
}
