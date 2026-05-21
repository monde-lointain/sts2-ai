// SpikeAutoload.cs — Godot autoload Node for option-B spike.
// NOTE: This file is present for the csproj build contract but is NOT
// used in the actual spike execution path.
//
// Why: The game's resources (project.godot including autoload registrations)
// are embedded in SlayTheSpire2.pck. There is no on-disk project.godot to
// patch in the overlay — the overlay approach (cp game dir, edit project.godot)
// is blocked because the project file is inside the packed resource.
//
// The actual injection mechanism used by this spike is the game's own
// Mod system: a spike DLL placed in <game_dir>/mods/spike/ is loaded by
// ModManager.Initialize() and invokes SpikeModInitializer.Initialize()
// via ModInitializerAttribute. See SpikeModInitializer.cs.
//
// This file satisfies the "src/SpikeAutoload.cs" deliverable contract from
// the plan, documenting the autoload-replacement decision.

using Godot;

namespace GodotHeadlessSpike;

/// <summary>
/// Autoload Node — not registered in overlay project.godot (no on-disk
/// project.godot exists; resources are PCK-embedded). Present as build
/// artifact only. See SpikeModInitializer for the actual injection path.
/// </summary>
public partial class SpikeAutoload : Node
{
    public override void _Ready()
    {
        // Not invoked: no on-disk project.godot to register this autoload.
        // Injection happens via ModInitializerAttribute in SpikeModInitializer.
        GD.Print("[SPIKE] SpikeAutoload._Ready() — this should never be called.");
    }
}
