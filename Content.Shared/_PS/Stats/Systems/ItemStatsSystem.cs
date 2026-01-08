using Content.Shared._PS.Stats.Components;
using Content.Shared._PS.Stats.Prototypes;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Projectiles;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Shared._PS.Stats.Systems;

/// <summary>
/// Handles item stats display in examine tooltip and stat effect calculations.
/// Intercepts damage events to apply stat-based bonuses.
/// </summary>
public sealed class ItemStatsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    /// <summary>
    /// Damage bonus per point of Strength for melee attacks (2% per point).
    /// </summary>
    private const float StrengthMeleeDamageBonus = 0.02f;

    /// <summary>
    /// Damage bonus per point of Dexterity for ranged attacks (2% per point).
    /// </summary>
    private const float DexterityRangedDamageBonus = 0.02f;

    /// <summary>
    /// Damage reduction per point of Fortitude (1.5% per point).
    /// </summary>
    private const float FortitudeDamageReduction = 0.015f;

    /// <summary>
    /// Crit chance per point of Luck (0.5% per point, so 20 Luck = 10% crit).
    /// </summary>
    private const float LuckCritChancePerPoint = 0.005f;

    /// <summary>
    /// Dodge chance per point of Luck (0.3% per point, so 20 Luck = 6% dodge).
    /// </summary>
    private const float LuckDodgeChancePerPoint = 0.003f;

    /// <summary>
    /// Level bonus scaling per point of Luck (2.5% per point).
    /// Example: Level 50 = 50% bonus, with 10 Luck = 50% × 1.25 = 62.5% bonus.
    /// </summary>
    private const float LuckLevelBonusPerPoint = 0.025f;

    /// <summary>
    /// Maximum Luck level bonus multiplier (2.0 = double level bonus at 40+ Luck).
    /// </summary>
    private const float MaxLuckLevelMultiplier = 2.0f;

    /// <summary>
    /// Crit damage multiplier (2x = double damage).
    /// </summary>
    private const float CritDamageMultiplier = 2.0f;

    /// <summary>
    /// Global minimum damage multiplier to prevent stacking to 99%+ reduction.
    /// 0.05 = always take at least 5% of incoming damage.
    /// </summary>
    private const float GlobalMinimumDamageMultiplier = 0.05f;

    private int _levelStatModifier = 100;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.TerradropLevelStatModifier, value => _levelStatModifier = value, true);

        // Examine tooltip
        SubscribeLocalEvent<ItemStatsComponent, GetVerbsEvent<ExamineVerb>>(OnExamine);

        // Offensive: Melee weapon damage modification (raised on weapon)
        SubscribeLocalEvent<ItemStatsComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);

        // Offensive: Ranged weapon damage modification (raised on gun when firing)
        SubscribeLocalEvent<ItemStatsComponent, GunShotEvent>(OnGunShot);

        // Defensive: Damage reduction (relayed to equipped items)
        SubscribeLocalEvent<ItemStatsComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnDamageModify);

        // Defensive: Dodge chance and global damage floor (checked once per damage event on target)
        SubscribeLocalEvent<DamageableComponent, DamageModifyEvent>(OnDamageModifyTarget);
    }

    #region Damage Interception

    /// <summary>
    /// Handles melee damage modification when a weapon with stats is used.
    /// Uses additive formula: Base × (1 + LevelBonus + WeaponBonus + AffixBonus + StrengthBonus)
    /// Luck provides: crit chance, better level bonus scaling
    /// </summary>
    private void OnGetMeleeDamage(EntityUid uid, ItemStatsComponent component, ref GetMeleeDamageEvent args)
    {
        // Calculate additive bonus total
        var totalBonus = 0f;

        // Get user's Luck for level scaling bonus
        var userLuck = GetTotalStat(args.User, StatType.Luck);
        var luckLevelMultiplier = Math.Min(1f + (userLuck * LuckLevelBonusPerPoint), MaxLuckLevelMultiplier);

        // Level bonus (e.g., level 31 = 31% bonus with default modifier), enhanced by Luck
        var levelBonus = component.SpawnLevel * 0.01f * (_levelStatModifier / 100f) * luckLevelMultiplier;
        totalBonus += levelBonus;

        // Weapon damage bonus (e.g., 1.10 multiplier = 10% bonus)
        if (component.WeaponDamageMultiplier > 1f)
        {
            totalBonus += component.WeaponDamageMultiplier - 1f;
        }

        // MeleeDamage affix bonus from weapon
        var meleeDamageBonus = GetAffixValue(component, AffixEffectType.MeleeDamage);
        if (meleeDamageBonus > 0)
        {
            totalBonus += meleeDamageBonus / 100f;
        }

        // User's total Strength bonus from all equipped items
        var userStrength = GetTotalStat(args.User, StatType.Strength);
        if (userStrength > 0)
        {
            totalBonus += userStrength * StrengthMeleeDamageBonus;
        }

        // User's MeleeDamage affixes from all equipped items
        var userMeleeBonus = GetTotalAffixValue(args.User, AffixEffectType.MeleeDamage);
        if (userMeleeBonus > 0)
        {
            totalBonus += userMeleeBonus / 100f;
        }

        // Apply total additive bonus
        if (totalBonus > 0)
        {
            args.Damage *= 1f + totalBonus;
        }

        // Crit chance from Luck and CritChance affix (capped at 100%)
        var critChance = userLuck * LuckCritChancePerPoint;
        critChance += GetTotalAffixValue(args.User, AffixEffectType.CritChance) / 100f;
        critChance += GetAffixValue(component, AffixEffectType.CritChance) / 100f;

        if (critChance > 0 && _random.Prob((float)Math.Min(critChance, 1.0))) // Cap at 100%
        {
            args.Damage *= CritDamageMultiplier;
        }
    }

    /// <summary>
    /// Handles ranged damage modification when a gun with stats fires.
    /// Uses additive formula: Base × (1 + LevelBonus + WeaponBonus + AffixBonus + DexterityBonus)
    /// Luck provides: crit chance, better level bonus scaling
    /// </summary>
    private void OnGunShot(EntityUid uid, ItemStatsComponent component, ref GunShotEvent args)
    {
        // Calculate additive bonus total
        var totalBonus = 0f;

        // Get user's Luck for level scaling bonus
        var userLuck = GetTotalStat(args.User, StatType.Luck);
        var luckLevelMultiplier = Math.Min(1f + (userLuck * LuckLevelBonusPerPoint), MaxLuckLevelMultiplier);

        // Level bonus (e.g., level 31 = 31% bonus with default modifier), enhanced by Luck
        var levelBonus = component.SpawnLevel * 0.01f * (_levelStatModifier / 100f) * luckLevelMultiplier;
        totalBonus += levelBonus;

        // Weapon damage bonus (e.g., 1.10 multiplier = 10% bonus)
        if (component.WeaponDamageMultiplier > 1f)
        {
            totalBonus += component.WeaponDamageMultiplier - 1f;
        }

        // RangedDamage affix bonus from weapon
        var rangedDamageBonus = GetAffixValue(component, AffixEffectType.RangedDamage);
        if (rangedDamageBonus > 0)
        {
            totalBonus += rangedDamageBonus / 100f;
        }

        // User's total Dexterity bonus from all equipped items
        var userDexterity = GetTotalStat(args.User, StatType.Dexterity);
        if (userDexterity > 0)
        {
            totalBonus += userDexterity * DexterityRangedDamageBonus;
        }

        // User's RangedDamage affixes from all equipped items
        var userRangedBonus = GetTotalAffixValue(args.User, AffixEffectType.RangedDamage);
        if (userRangedBonus > 0)
        {
            totalBonus += userRangedBonus / 100f;
        }

        // Crit chance from Luck and CritChance affix
        var critChance = userLuck * LuckCritChancePerPoint;
        critChance += GetTotalAffixValue(args.User, AffixEffectType.CritChance) / 100f;
        critChance += GetAffixValue(component, AffixEffectType.CritChance) / 100f;
        var isCrit = critChance > 0 && _random.Prob((float)Math.Min(critChance, 1.0));

        // Apply total additive bonus to all fired projectiles
        var multiplier = 1f + totalBonus;
        if (isCrit)
            multiplier *= CritDamageMultiplier;

        if (multiplier > 1f)
        {
            foreach (var (ammo, _) in args.Ammo)
            {
                if (ammo != null && TryComp<ProjectileComponent>(ammo, out var proj))
                {
                    proj.Damage *= multiplier;
                }
            }
        }
    }

    /// <summary>
    /// Handles incoming damage modification for equipped items with stats.
    /// Applies Fortitude-based damage reduction and Armor affix bonus.
    /// </summary>
    private void OnDamageModify(EntityUid uid, ItemStatsComponent component, InventoryRelayedEvent<DamageModifyEvent> args)
    {
        // Apply Fortitude stat from this item
        if (component.StatBonuses.TryGetValue(StatType.Fortitude, out var fortitude) && fortitude > 0)
        {
            var reduction = 1f - (fortitude * FortitudeDamageReduction);
            reduction = Math.Max(reduction, 0.1f); // Cap at 90% reduction
            args.Args.Damage *= reduction;
        }

        // Apply Armor affix from this item
        var armorBonus = GetAffixValue(component, AffixEffectType.Armor);
        if (armorBonus > 0)
        {
            var reduction = 1f - (armorBonus / 100f);
            reduction = Math.Max(reduction, 0.5f); // Cap at 50% reduction per item
            args.Args.Damage *= reduction;
        }
    }

    /// <summary>
    /// Handles dodge chance and global damage floor for the target entity.
    /// Checked once per damage event on the target.
    /// </summary>
    private void OnDamageModifyTarget(EntityUid uid, DamageableComponent component, DamageModifyEvent args)
    {
        // Dodge chance from Luck stat
        var totalLuck = GetTotalStat(uid, StatType.Luck);
        if (totalLuck > 0)
        {
            var dodgeChance = totalLuck * LuckDodgeChancePerPoint;

            // Roll for dodge - if successful, negate all damage
            if (_random.Prob(Math.Min(dodgeChance, 0.5f))) // Cap at 50% dodge
            {
                args.Damage *= 0f;
                return; // Dodged, no need to apply floor
            }
        }

        // Global damage floor to prevent stacking armor to 99%+ reduction
        var originalTotal = args.OriginalDamage.GetTotal().Float();
        if (originalTotal <= 0)
            return;

        var currentTotal = args.Damage.GetTotal().Float();
        var minimumDamage = originalTotal * GlobalMinimumDamageMultiplier;

        // If current damage is below the floor, scale it back up
        if (currentTotal > 0 && currentTotal < minimumDamage)
        {
            var scale = minimumDamage / currentTotal;
            args.Damage *= scale;
        }
    }

    #endregion

    #region Stat Aggregation

    /// <summary>
    /// Gets the total value of a stat from all equipped items on an entity.
    /// </summary>
    public int GetTotalStat(EntityUid entity, StatType stat)
    {
        var total = 0;

        if (!TryComp<InventoryComponent>(entity, out var inventory))
            return total;

        var enumerator = _inventory.GetSlotEnumerator((entity, inventory));
        while (enumerator.NextItem(out var item))
        {
            if (TryComp<ItemStatsComponent>(item, out var stats) &&
                stats.StatBonuses.TryGetValue(stat, out var value))
            {
                total += value;
            }
        }

        return total;
    }

    /// <summary>
    /// Gets the total value of an affix effect from all equipped items on an entity.
    /// </summary>
    public float GetTotalAffixValue(EntityUid entity, AffixEffectType effectType)
    {
        var total = 0f;

        if (!TryComp<InventoryComponent>(entity, out var inventory))
            return total;

        var enumerator = _inventory.GetSlotEnumerator((entity, inventory));
        while (enumerator.NextItem(out var item))
        {
            if (TryComp<ItemStatsComponent>(item, out var stats))
            {
                total += GetAffixValue(stats, effectType);
            }
        }

        return total;
    }

    /// <summary>
    /// Gets the value of a specific affix effect type from a component.
    /// </summary>
    public float GetAffixValue(ItemStatsComponent component, AffixEffectType effectType)
    {
        foreach (var affix in component.Affixes)
        {
            if (_proto.TryIndex(affix.AffixId, out var affixProto) &&
                affixProto.EffectType == effectType)
            {
                return affix.Value;
            }
        }
        return 0f;
    }

    /// <summary>
    /// Gets all stats from all equipped items on an entity.
    /// </summary>
    public Dictionary<StatType, int> GetAllStats(EntityUid entity)
    {
        var stats = new Dictionary<StatType, int>();

        foreach (StatType stat in Enum.GetValues<StatType>())
        {
            var value = GetTotalStat(entity, stat);
            if (value > 0)
                stats[stat] = value;
        }

        return stats;
    }

    #endregion

    #region Examine

    private void OnExamine(EntityUid uid, ItemStatsComponent component, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract)
            return;

        var message = GetStatsExamineMessage(uid, component);

        _examine.AddHoverExamineVerb(
            args,
            component,
            Loc.GetString("item-stats-examine-verb"),
            message.ToMarkup(),
            "/Textures/Interface/VerbIcons/dot.svg.192dpi.png");
    }

    /// <summary>
    /// Generates the formatted examine message for item stats.
    /// </summary>
    public FormattedMessage GetStatsExamineMessage(EntityUid uid, ItemStatsComponent component)
    {
        var msg = new FormattedMessage();
        var rarity = _proto.Index(component.Rarity);

        // Rarity header with color and level indicator
        var colorHex = rarity.Color.ToHex();
        var rarityText = $"[color={colorHex}][bold]{Loc.GetString(rarity.Name)}[/bold][/color]";

        // Add level indicator if spawn level > 0
        if (component.SpawnLevel > 0)
        {
            rarityText += $"  [color=#888888]{Loc.GetString("item-stats-level", ("level", component.SpawnLevel))}[/color]";
        }

        msg.AddMarkupOrThrow(rarityText);
        msg.PushNewline();

        // Show base weapon damage for melee weapons
        if (TryComp<MeleeWeaponComponent>(uid, out var melee))
        {
            var totalDamage = melee.Damage.GetTotal();
            msg.AddMarkupOrThrow(Loc.GetString("item-stats-base-damage", ("value", totalDamage)));
            msg.PushNewline();
        }

        // Calculate level bonus for display
        var levelBonus = component.SpawnLevel * 0.01f * (_levelStatModifier / 100f);
        var bonusMultiplier = 1f + levelBonus;

        // Weapon damage bonus (if applicable)
        if (component.WeaponDamageMultiplier > 1f)
        {
            var dmgPercent = (component.WeaponDamageMultiplier - 1f) * 100f;
            var dmgText = Loc.GetString("item-stats-weapon-damage", ("value", dmgPercent.ToString("F1")));

            // Show base range (no level scaling - level bonus is applied separately)
            if (component.WeaponDamageRange != null)
            {
                var minPct = (component.WeaponDamageRange.Min - 1f) * 100f;
                var maxPct = (component.WeaponDamageRange.Max - 1f) * 100f;
                dmgText += $" [color=#666666]({minPct:F0}-{maxPct:F0})[/color]";
            }

            msg.AddMarkupOrThrow(dmgText);
            msg.PushNewline();
        }

        // Level bonus display (shows as separate line since it's applied additively)
        if (component.SpawnLevel > 0 && levelBonus > 0)
        {
            var levelPct = levelBonus * 100f;
            msg.AddMarkupOrThrow($"[color=#88ff88]+{levelPct:F0}% {Loc.GetString("item-stats-level-bonus")}[/color]");
            msg.PushNewline();
        }

        // Armor bonuses (if applicable)
        if (component.SoftcritBonus > 0 || component.DeathThresholdBonus > 0)
        {
            if (component.SoftcritBonus > 0)
            {
                var scText = Loc.GetString("item-stats-softcrit-bonus", ("value", component.SoftcritBonus));

                if (component.SoftcritRange != null)
                {
                    var minSc = (int)Math.Round(component.SoftcritRange.Min * bonusMultiplier);
                    var maxSc = (int)Math.Round(component.SoftcritRange.Max * bonusMultiplier);
                    scText += $" [color=#666666]({minSc}-{maxSc})[/color]";
                }

                msg.AddMarkupOrThrow(scText);
                msg.PushNewline();
            }
            if (component.DeathThresholdBonus > 0)
            {
                var dtText = Loc.GetString("item-stats-death-bonus", ("value", component.DeathThresholdBonus));

                if (component.DeathThresholdRange != null)
                {
                    var minDt = (int)Math.Round(component.DeathThresholdRange.Min * bonusMultiplier);
                    var maxDt = (int)Math.Round(component.DeathThresholdRange.Max * bonusMultiplier);
                    dtText += $" [color=#666666]({minDt}-{maxDt})[/color]";
                }

                msg.AddMarkupOrThrow(dtText);
                msg.PushNewline();
            }
        }

        // Core stat bonuses
        if (component.StatBonuses.Count > 0)
        {
            msg.PushNewline();
            msg.AddMarkupOrThrow($"[bold]{Loc.GetString("item-stats-header-stats")}[/bold]");
            msg.PushNewline();

            foreach (var (stat, value) in component.StatBonuses)
            {
                var statName = GetStatDisplayName(stat);
                var color = GetStatColor(stat);
                var statText = $"[color={color}]+{value} {statName}[/color]";

                // Show range if available
                if (component.StatRanges.TryGetValue(stat, out var range))
                {
                    var minVal = (int)Math.Round(range.Min * bonusMultiplier);
                    var maxVal = (int)Math.Round(range.Max * bonusMultiplier);
                    statText += $" [color=#666666]({minVal}-{maxVal})[/color]";
                }

                msg.AddMarkupOrThrow(statText);
                msg.PushNewline();
            }
        }

        // Affixes
        if (component.Affixes.Count > 0)
        {
            msg.PushNewline();
            msg.AddMarkupOrThrow($"[bold]{Loc.GetString("item-stats-header-affixes")}[/bold]");
            msg.PushNewline();

            foreach (var affix in component.Affixes)
            {
                if (!_proto.TryIndex(affix.AffixId, out var affixProto))
                    continue;

                var affixName = Loc.GetString(affixProto.Name);
                var affixText = $"[color=#a0a0ff]+{affix.Value:F1}% {affixName}[/color]";

                // Show range if available
                var affixRange = component.AffixRanges.Find(r => r.AffixId == affix.AffixId);
                if (affixRange != null)
                {
                    var minAff = affixRange.Min * bonusMultiplier;
                    var maxAff = affixRange.Max * bonusMultiplier;
                    affixText += $" [color=#666666]({minAff:F0}-{maxAff:F0})[/color]";
                }

                msg.AddMarkupOrThrow(affixText);
                msg.PushNewline();
            }
        }

        return msg;
    }

    /// <summary>
    /// Gets the display name for a stat type.
    /// </summary>
    private string GetStatDisplayName(StatType stat)
    {
        return stat switch
        {
            StatType.Strength => Loc.GetString("stat-strength"),
            StatType.Dexterity => Loc.GetString("stat-dexterity"),
            StatType.Agility => Loc.GetString("stat-agility"),
            StatType.Fortitude => Loc.GetString("stat-fortitude"),
            StatType.Intelligence => Loc.GetString("stat-intelligence"),
            StatType.Luck => Loc.GetString("stat-luck"),
            _ => stat.ToString()
        };
    }

    /// <summary>
    /// Gets the display color for a stat type.
    /// </summary>
    private string GetStatColor(StatType stat)
    {
        return stat switch
        {
            StatType.Strength => "#ff6b6b",     // Red
            StatType.Dexterity => "#ffd93d",    // Yellow
            StatType.Agility => "#6bcb77",      // Green
            StatType.Fortitude => "#4d96ff",    // Blue
            StatType.Intelligence => "#c9b1ff", // Purple
            StatType.Luck => "#ffc107",         // Gold
            _ => "#ffffff"
        };
    }

    #endregion
}
