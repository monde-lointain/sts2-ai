// Audio category — M8 stubs.
//
// Upstream Godot surfaces covered:
//   * Godot.AudioStreamPlayer — referenced by Audio/* namespace; not directly used by
//     core model code (Cards/Relics/Powers/Monsters/Combat) but present as a marker for
//     DI substitution per Q1-ADR-004 T2. Inertia tests assert it constructs without
//     side effects.
//
// Per spec: stubs are pure no-ops. No allocation in decision path, no IO, no clock reads.

using Sts2Headless.EngineStrip;

namespace Godot;

/// <summary>
/// Headless stub for Godot's <c>AudioStreamPlayer</c>. Default no-op; constructor records
/// a hit. Methods like <c>Play()</c> / <c>Stop()</c> are provided as no-ops; further
/// playback queries throw <see cref="StubRegistry.ThrowNotStubbed"/> so S5+ can extend
/// reactively (R4 mitigation).
/// </summary>
public class AudioStreamPlayer : Node
{
    public AudioStreamPlayer()
    {
        StubRegistry.Record(StubCategory.Audio, nameof(AudioStreamPlayer), ".ctor");
    }

    public void Play(float fromPosition = 0f)
    {
        StubRegistry.Record(StubCategory.Audio, nameof(AudioStreamPlayer), nameof(Play));
    }

    public void Stop()
    {
        StubRegistry.Record(StubCategory.Audio, nameof(AudioStreamPlayer), nameof(Stop));
    }
}
