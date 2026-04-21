using Content.Shared._PS.Stats.Components;
using Content.Shared._PS.Stats.Prototypes;
using Content.Shared._PS.Stats.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Server._PS.Stats.Systems;

/// <summary>
/// Aggregates SoftcritBonus, DeathThresholdBonus, and HpThreshold affix values
/// from all equipped items and applies them to the player's MobThresholdsComponent.
/// Recalculates on equip/unequip.
/// </summary>
public sealed class PlayerThresholdBonusSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly ItemStatsSystem _itemStats = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemStatsComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<ItemStatsComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnEquipped(EntityUid uid, ItemStatsComponent component, GotEquippedEvent args)
    {
        RecalculateThresholds(args.Equipee);
    }

    private void OnUnequipped(EntityUid uid, ItemStatsComponent component, GotUnequippedEvent args)
    {
        RecalculateThresholds(args.Equipee);
    }

    private void RecalculateThresholds(EntityUid wearer)
    {
        if (!TryComp<MobThresholdsComponent>(wearer, out var thresholds))
            return;

        if (!TryComp<InventoryComponent>(wearer, out var inventory))
            return;

        var baseThresholds = EnsureBaseThresholds(wearer, thresholds);

        // Aggregate bonuses from all equipped items
        var totalSoftcritBonus = 0;
        var totalDeathBonus = 0;
        var totalHpThreshold = 0f;

        var enumerator = _inventory.GetSlotEnumerator((wearer, inventory));
        while (enumerator.NextItem(out var item))
        {
            if (!TryComp<ItemStatsComponent>(item, out var stats))
                continue;

            totalSoftcritBonus += stats.SoftcritBonus;
            totalDeathBonus += stats.DeathThresholdBonus;
            totalHpThreshold += _itemStats.GetAffixValue(stats, AffixEffectType.HpThreshold);
        }

        // HpThreshold affix increases both critical and dead thresholds
        var hpBonus = (int)totalHpThreshold;

        var newCritThreshold = baseThresholds.BaseCritThreshold + FixedPoint2.New(totalSoftcritBonus + hpBonus);
        var newDeadThreshold = baseThresholds.BaseDeadThreshold + FixedPoint2.New(totalDeathBonus + hpBonus);

        _mobThreshold.SetMobStateThreshold(wearer, newCritThreshold, MobState.Critical, thresholds);
        _mobThreshold.SetMobStateThreshold(wearer, newDeadThreshold, MobState.Dead, thresholds);

        Log.Debug($"Updated thresholds for {ToPrettyString(wearer)}: " +
                  $"crit={newCritThreshold} (base {baseThresholds.BaseCritThreshold} +{totalSoftcritBonus + hpBonus}), " +
                  $"dead={newDeadThreshold} (base {baseThresholds.BaseDeadThreshold} +{totalDeathBonus + hpBonus})");
    }

    /// <summary>
    /// Ensures the entity has a BaseThresholdsComponent storing its original thresholds.
    /// Snapshots the current values on first call.
    /// </summary>
    private BaseThresholdsComponent EnsureBaseThresholds(EntityUid uid, MobThresholdsComponent thresholds)
    {
        if (TryComp<BaseThresholdsComponent>(uid, out var baseComp))
            return baseComp;

        baseComp = EnsureComp<BaseThresholdsComponent>(uid);

        // Snapshot current thresholds as the base
        foreach (var (damage, state) in thresholds.Thresholds)
        {
            switch (state)
            {
                case MobState.Critical:
                    baseComp.BaseCritThreshold = damage;
                    break;
                case MobState.Dead:
                    baseComp.BaseDeadThreshold = damage;
                    break;
            }
        }

        return baseComp;
    }
}
