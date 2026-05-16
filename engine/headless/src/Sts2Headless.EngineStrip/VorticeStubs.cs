// Vortice (DXGI / DirectX bindings) vendor category — M8 stubs.
//
// Upstream usage of Vortice.* is exclusively from windowing / GPU-init code; the headless
// pipeline never spins up a display surface (Q1-ADR-002 + pipeline ADR-002). Phase-1 model
// code does NOT touch Vortice. We provide the categorical placeholder so the framework is
// in place and any future inherited file referencing the namespace will resolve.

using Sts2Headless.EngineStrip;

namespace Vortice.DXGI;

/// <summary>
/// Headless placeholder for the Vortice.DXGI namespace. Inert sentinel. Specific types are
/// added reactively as upstream call sites are extracted; until then any reference fails
/// to compile, which is the correct gate per the spec.
/// </summary>
public static class VorticeMarker
{
    public static bool IsAvailable()
    {
        StubRegistry.Record(StubCategory.Vortice, nameof(VorticeMarker), nameof(IsAvailable));
        return false;
    }
}
