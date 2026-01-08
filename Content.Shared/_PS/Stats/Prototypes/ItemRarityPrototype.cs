using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._PS.Stats.Prototypes;

/// <summary>
/// Defines item rarity tiers (T1-T5) with their properties.
/// </summary>
[Prototype("itemRarity")]
public sealed partial class ItemRarityPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Display name of this rarity tier.
    /// </summary>
    [DataField(required: true)]
    public LocId Name { get; private set; } = string.Empty;

    /// <summary>
    /// Tier number (1-5).
    /// </summary>
    [DataField(required: true)]
    public int Tier { get; private set; }

    /// <summary>
    /// Color used for displaying this rarity (hex color code).
    /// </summary>
    [DataField(required: true)]
    public Color Color { get; private set; } = Color.White;

    /// <summary>
    /// Base weapon damage multiplier range (min).
    /// </summary>
    [DataField]
    public float WeaponDamageMultMin { get; private set; } = 0f;

    /// <summary>
    /// Base weapon damage multiplier range (max).
    /// </summary>
    [DataField]
    public float WeaponDamageMultMax { get; private set; } = 0f;

    /// <summary>
    /// Bonus to softcrit threshold for body armor.
    /// </summary>
    [DataField]
    public int ArmorSoftcritBonus { get; private set; } = 0;

    /// <summary>
    /// Bonus to death threshold for body armor.
    /// </summary>
    [DataField]
    public int ArmorDeathBonus { get; private set; } = 0;

    /// <summary>
    /// Number of affixes items of this rarity can have.
    /// </summary>
    [DataField]
    public int AffixCount { get; private set; } = 1;

    /// <summary>
    /// Multiplier for affix effect values (higher rarity = stronger affixes).
    /// </summary>
    [DataField]
    public float AffixEffectMultiplier { get; private set; } = 1f;

    /// <summary>
    /// Number of artifact traits items of this rarity can have.
    /// </summary>
    [DataField]
    public int ArtifactTraitCount { get; private set; } = 0;
}
