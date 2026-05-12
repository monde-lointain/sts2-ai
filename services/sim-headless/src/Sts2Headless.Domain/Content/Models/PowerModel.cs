namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Abstract base for all power content. Q1-headless analogue of upstream
/// <c>MegaCrit.Sts2.Core.Models.PowerModel</c>
/// (~/development/projects/godot/sts2/src/Core/Models/PowerModel.cs:18).
///
/// <para>
/// A <see cref="PowerModel"/> is the catalog-singleton metadata for a power: stable
/// <see cref="Id"/>, <see cref="Type"/> (Buff vs Debuff), and <see cref="StackType"/>
/// (Counter vs Single). Per-instance stack counts and per-instance flags live on the
/// combat-side <c>PowerInstance</c> record, not here.
/// </para>
/// </summary>
public abstract class PowerModel : IPowerModel
{
    /// <summary>Stable string id matching upstream <c>ModelId.Entry</c>.</summary>
    public string Id { get; }

    /// <summary>Power type (Buff vs Debuff). Drives UI color upstream; pure metadata in Q1.</summary>
    public PowerType Type { get; }

    /// <summary>How re-application interacts with existing stacks.</summary>
    public PowerStackType StackType { get; }

    /// <summary>
    /// Construct with a canonical configuration. Per-instance stack count starts at
    /// zero on the combat-side <c>PowerInstance</c>; callers apply the initial stack
    /// count through the combat context.
    /// </summary>
    protected PowerModel(string id, PowerType type, PowerStackType stackType)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new System.ArgumentException("PowerModel id must be non-empty.", nameof(id));
        }
        if (stackType == PowerStackType.None)
        {
            throw new System.ArgumentException(
                $"PowerModel '{id}': StackType must be Counter or Single (not None).",
                nameof(stackType));
        }
        Id = id;
        Type = type;
        StackType = stackType;
    }
}
