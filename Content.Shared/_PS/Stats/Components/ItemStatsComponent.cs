using Content.Shared._PS.Stats.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.Stats.Components;

/// <summary>
/// Component for items that have stats, rarity, and affixes.
/// Stats are shown in the examine tooltip when shift-clicking.
/// On spawn, stats are randomized based on the defined ranges.
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
    /// Whether this item's stats have been initialized/rolled.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public bool Initialized { get; set; } = false;

    /// <summary>
    /// The terradrop level this item was spawned at.
    /// Higher levels mean better stat rolls.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public int SpawnLevel { get; set; } = 0;

    #region Rolled Values (final values after randomization)

    /// <summary>
    /// Core stat bonuses this item provides (rolled values).
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
    /// Weapon damage multiplier (rolled value, for weapons only).
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float WeaponDamageMultiplier { get; set; } = 1f;

    /// <summary>
    /// Softcrit threshold bonus (rolled value, for armor only).
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public int SoftcritBonus { get; set; } = 0;

    /// <summary>
    /// Death threshold bonus (rolled value, for armor only).
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public int DeathThresholdBonus { get; set; } = 0;

    #endregion

    #region Ranges (for randomization on spawn)

    /// <summary>
    /// Stat ranges for randomization. Key is stat type, value is min/max range.
    /// </summary>
    [DataField]
    public Dictionary<StatType, StatRange> StatRanges { get; set; } = new();

    /// <summary>
    /// Affix templates with their value ranges for randomization.
    /// </summary>
    [DataField]
    public List<AffixRange> AffixRanges { get; set; } = new();

    /// <summary>
    /// Weapon damage multiplier range (min/max).
    /// </summary>
    [DataField]
    public FloatRange? WeaponDamageRange { get; set; }

    /// <summary>
    /// Softcrit bonus range (min/max).
    /// </summary>
    [DataField]
    public IntRange? SoftcritRange { get; set; }

    /// <summary>
    /// Death threshold bonus range (min/max).
    /// </summary>
    [DataField]
    public IntRange? DeathThresholdRange { get; set; }

    #endregion
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

/// <summary>
/// Represents a range for affix value randomization.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class AffixRange
{
    /// <summary>
    /// The affix prototype ID.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<ItemAffixPrototype> AffixId { get; set; } = default!;

    /// <summary>
    /// Minimum value for this affix.
    /// </summary>
    [DataField]
    public float Min { get; set; } = 1f;

    /// <summary>
    /// Maximum value for this affix.
    /// </summary>
    [DataField]
    public float Max { get; set; } = 5f;
}

/// <summary>
/// Integer range for stat randomization.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class StatRange
{
    [DataField]
    public int Min { get; set; } = 1;

    [DataField]
    public int Max { get; set; } = 3;
}

/// <summary>
/// Float range for value randomization.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class FloatRange
{
    [DataField]
    public float Min { get; set; } = 1f;

    [DataField]
    public float Max { get; set; } = 1.1f;
}

/// <summary>
/// Integer range for threshold randomization.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class IntRange
{
    [DataField]
    public int Min { get; set; } = 0;

    [DataField]
    public int Max { get; set; } = 5;
}
