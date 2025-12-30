using Content.Shared._PS.Stats.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.Stats.Components;

/// <summary>
/// Component for items that have stats, rarity, and affixes.
/// Stats are shown in the examine tooltip when shift-clicking.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ItemStatsComponent : Component
{
    /// <summary>
    /// The rarity tier of this item.
    /// </summary>
    [DataField(required: true)]
    [AutoNetworkedField]
    public ProtoId<ItemRarityPrototype> Rarity { get; set; } = "Uncommon";

    /// <summary>
    /// Core stat bonuses this item provides.
    /// Key is stat type, value is the bonus amount.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public Dictionary<StatType, int> StatBonuses { get; set; } = new();

    /// <summary>
    /// Affixes on this item with their rolled values.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public List<ItemAffix> Affixes { get; set; } = new();

    /// <summary>
    /// Weapon damage multiplier from rarity (for weapons only).
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float WeaponDamageMultiplier { get; set; } = 1f;

    /// <summary>
    /// Softcrit threshold bonus from rarity (for armor only).
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public int SoftcritBonus { get; set; } = 0;

    /// <summary>
    /// Death threshold bonus from rarity (for armor only).
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public int DeathThresholdBonus { get; set; } = 0;
}

/// <summary>
/// Represents a single affix on an item with its rolled value.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class ItemAffix
{
    /// <summary>
    /// The affix prototype ID.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<ItemAffixPrototype> AffixId { get; set; } = default!;

    /// <summary>
    /// The rolled value for this affix (percentage).
    /// </summary>
    [DataField]
    public float Value { get; set; }
}
