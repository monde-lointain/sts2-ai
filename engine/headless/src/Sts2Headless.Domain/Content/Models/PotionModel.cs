namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Abstract base for all potion content. Q1-headless analogue of upstream
/// <c>MegaCrit.Sts2.Core.Models.PotionModel</c>
/// (~/development/projects/godot/sts2/src/Core/Models/PotionModel.cs).
///
/// <para>
/// Each potion has a stable id + display name + rarity. The actual "use potion"
/// effect (apply Strength, deal damage, etc.) wires in S13 once the consumable
/// queue is in place; the model itself just exposes the metadata used by the
/// content catalog and reward selection.
/// </para>
/// </summary>
public abstract class PotionModel : IPotionModel
{
    public string Id { get; }
    public string Name { get; }
    public PotionRarity Rarity { get; }

    protected PotionModel(string id, string name, PotionRarity rarity)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new System.ArgumentException("PotionModel id must be non-empty.", nameof(id));
        }
        Id = id;
        Name = name ?? string.Empty;
        Rarity = rarity;
    }
}

/// <summary>Concrete <see cref="IPotionModel"/> marker — populated alongside <see cref="PotionModel"/>.</summary>
public interface IPotionModelExt : IPotionModel { }
