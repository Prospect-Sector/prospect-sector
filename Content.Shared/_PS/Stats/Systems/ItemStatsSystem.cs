using Content.Shared._PS.Stats.Components;
using Content.Shared._PS.Stats.Prototypes;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Projectiles;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Prototypes;
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

    public override void Initialize()
    {
        base.Initialize();

        // Examine tooltip
        SubscribeLocalEvent<ItemStatsComponent, GetVerbsEvent<ExamineVerb>>(OnExamine);

        // Offensive: Melee weapon damage modification (raised on weapon)
        SubscribeLocalEvent<ItemStatsComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);

        // Offensive: Ranged weapon damage modification (raised on gun when firing)
        SubscribeLocalEvent<ItemStatsComponent, GunShotEvent>(OnGunShot);

        // Defensive: Damage reduction (relayed to equipped items)
        SubscribeLocalEvent<ItemStatsComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnDamageModify);
    }

    #region Damage Interception

    /// <summary>
    /// Handles melee damage modification when a weapon with stats is used.
    /// Applies weapon damage multiplier and user's Strength bonus.
    /// </summary>
    private void OnGetMeleeDamage(EntityUid uid, ItemStatsComponent component, ref GetMeleeDamageEvent args)
    {
        // Apply weapon's damage multiplier
        if (component.WeaponDamageMultiplier > 1f)
        {
            args.Damage *= component.WeaponDamageMultiplier;
        }

        // Apply MeleeDamage affix bonus from weapon
        var meleeDamageBonus = GetAffixValue(component, AffixEffectType.MeleeDamage);
        if (meleeDamageBonus > 0)
        {
            args.Damage *= 1f + (meleeDamageBonus / 100f);
        }

        // Apply user's total Strength bonus from all equipped items
        var userStrength = GetTotalStat(args.User, StatType.Strength);
        if (userStrength > 0)
        {
            var strengthBonus = 1f + (userStrength * StrengthMeleeDamageBonus);
            args.Damage *= strengthBonus;
        }

        // Apply user's MeleeDamage affixes from all equipped items
        var userMeleeBonus = GetTotalAffixValue(args.User, AffixEffectType.MeleeDamage);
        if (userMeleeBonus > 0)
        {
            args.Damage *= 1f + (userMeleeBonus / 100f);
        }
    }

    /// <summary>
    /// Handles ranged damage modification when a gun with stats fires.
    /// Applies weapon damage multiplier and user's Dexterity bonus to projectiles.
    /// </summary>
    private void OnGunShot(EntityUid uid, ItemStatsComponent component, ref GunShotEvent args)
    {
        // Calculate total damage multiplier
        var multiplier = 1f;

        // Apply weapon's damage multiplier
        if (component.WeaponDamageMultiplier > 1f)
        {
            multiplier *= component.WeaponDamageMultiplier;
        }

        // Apply RangedDamage affix bonus from weapon
        var rangedDamageBonus = GetAffixValue(component, AffixEffectType.RangedDamage);
        if (rangedDamageBonus > 0)
        {
            multiplier *= 1f + (rangedDamageBonus / 100f);
        }

        // Apply user's total Dexterity bonus from all equipped items
        var userDexterity = GetTotalStat(args.User, StatType.Dexterity);
        if (userDexterity > 0)
        {
            multiplier *= 1f + (userDexterity * DexterityRangedDamageBonus);
        }

        // Apply user's RangedDamage affixes from all equipped items
        var userRangedBonus = GetTotalAffixValue(args.User, AffixEffectType.RangedDamage);
        if (userRangedBonus > 0)
        {
            multiplier *= 1f + (userRangedBonus / 100f);
        }

        // Apply multiplier to all fired projectiles
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
            if (TryComp<ItemStatsComponent>(item, out var stats))
            {
                if (stats.StatBonuses.TryGetValue(stat, out var value))
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

        var message = GetStatsExamineMessage(component);

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
    public FormattedMessage GetStatsExamineMessage(ItemStatsComponent component)
    {
        var msg = new FormattedMessage();
        var rarity = _proto.Index(component.Rarity);

        // Rarity header with color
        var colorHex = rarity.Color.ToHex();
        msg.AddMarkupOrThrow($"[color={colorHex}][bold]{Loc.GetString(rarity.Name)}[/bold][/color]");
        msg.PushNewline();

        // Weapon damage multiplier (if applicable)
        if (component.WeaponDamageMultiplier > 1f)
        {
            var dmgPercent = (component.WeaponDamageMultiplier - 1f) * 100f;
            msg.AddMarkupOrThrow(Loc.GetString("item-stats-weapon-damage", ("value", dmgPercent.ToString("F1"))));
            msg.PushNewline();
        }

        // Armor bonuses (if applicable)
        if (component.SoftcritBonus > 0 || component.DeathThresholdBonus > 0)
        {
            if (component.SoftcritBonus > 0)
            {
                msg.AddMarkupOrThrow(Loc.GetString("item-stats-softcrit-bonus", ("value", component.SoftcritBonus)));
                msg.PushNewline();
            }
            if (component.DeathThresholdBonus > 0)
            {
                msg.AddMarkupOrThrow(Loc.GetString("item-stats-death-bonus", ("value", component.DeathThresholdBonus)));
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
                msg.AddMarkupOrThrow($"[color={color}]+{value} {statName}[/color]");
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
                msg.AddMarkupOrThrow($"[color=#a0a0ff]+{affix.Value:F1}% {affixName}[/color]");
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
