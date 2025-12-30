using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Stats.Prototypes;

/// <summary>
/// Defines an item affix type that can appear on equipment.
/// </summary>
[Prototype("itemAffix")]
public sealed partial class ItemAffixPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Display name of this affix.
    /// </summary>
    [DataField(required: true)]
    public LocId Name { get; private set; } = string.Empty;

    /// <summary>
    /// Description shown in tooltip.
    /// </summary>
    [DataField]
    public LocId Description { get; private set; } = string.Empty;

    /// <summary>
    /// Which stat this affix modifies, if any.
    /// </summary>
    [DataField]
    public StatType? AffectedStat { get; private set; }

    /// <summary>
    /// Minimum value for this affix (percentage, 1-5 typically).
    /// </summary>
    [DataField]
    public float MinValue { get; private set; } = 1f;

    /// <summary>
    /// Maximum value for this affix (percentage, 1-5 typically).
    /// </summary>
    [DataField]
    public float MaxValue { get; private set; } = 5f;

    /// <summary>
    /// Which equipment slots this affix can appear on.
    /// </summary>
    [DataField]
    public HashSet<string> ValidSlots { get; private set; } = new();

    /// <summary>
    /// If true, this affix only applies to weapons.
    /// </summary>
    [DataField]
    public bool WeaponOnly { get; private set; } = false;

    /// <summary>
    /// The effect type this affix provides.
    /// </summary>
    [DataField(required: true)]
    public AffixEffectType EffectType { get; private set; } = AffixEffectType.StatBonus;
}

/// <summary>
/// Types of effects an affix can provide.
/// </summary>
public enum AffixEffectType : byte
{
    /// <summary>
    /// Increases a core stat.
    /// </summary>
    StatBonus,

    /// <summary>
    /// Increases melee damage percentage.
    /// </summary>
    MeleeDamage,

    /// <summary>
    /// Increases ranged damage percentage.
    /// </summary>
    RangedDamage,

    /// <summary>
    /// Increases elemental damage percentage.
    /// </summary>
    ElementalDamage,

    /// <summary>
    /// Increases dodge chance percentage.
    /// </summary>
    Dodge,

    /// <summary>
    /// Increases movement speed percentage.
    /// </summary>
    MoveSpeed,

    /// <summary>
    /// Increases critical hit chance percentage.
    /// </summary>
    CritChance,

    /// <summary>
    /// Increases block chance percentage.
    /// </summary>
    BlockChance,

    /// <summary>
    /// Increases loot rarity percentage.
    /// </summary>
    LootRarity,

    /// <summary>
    /// Increases HP thresholds.
    /// </summary>
    HpThreshold,

    /// <summary>
    /// Increases armor percentage.
    /// </summary>
    Armor
}
