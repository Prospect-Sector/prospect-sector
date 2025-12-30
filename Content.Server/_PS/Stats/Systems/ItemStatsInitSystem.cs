using System.Linq;
using Content.Shared._PS.Stats.Components;
using Robust.Shared.Random;

namespace Content.Server._PS.Stats.Systems;

/// <summary>
/// Server-side system that initializes item stats on spawn.
/// Rolls random values based on defined ranges when an item with ItemStatsComponent spawns.
/// </summary>
public sealed class ItemStatsInitSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemStatsComponent, MapInitEvent>(OnMapInit);
    }

    /// <summary>
    /// Called when an entity with ItemStatsComponent is initialized on the map.
    /// Rolls random stats based on the defined ranges.
    /// </summary>
    private void OnMapInit(EntityUid uid, ItemStatsComponent component, MapInitEvent args)
    {
        // Skip if already initialized (e.g., loaded from save)
        if (component.Initialized)
            return;

        RollStats(uid, component);
        component.Initialized = true;
        Dirty(uid, component);
    }

    /// <summary>
    /// Rolls all random stats for an item based on its defined ranges.
    /// </summary>
    public void RollStats(EntityUid uid, ItemStatsComponent component)
    {
        // Roll stat bonuses
        if (component.StatRanges.Count > 0)
        {
            component.StatBonuses.Clear();
            foreach (var (stat, range) in component.StatRanges)
            {
                var value = _random.Next(range.Min, range.Max + 1); // +1 because Next is exclusive on upper bound
                if (value > 0)
                    component.StatBonuses[stat] = value;
            }
        }

        // Roll affixes
        if (component.AffixRanges.Count > 0)
        {
            component.Affixes.Clear();
            foreach (var affixRange in component.AffixRanges)
            {
                var value = _random.NextFloat(affixRange.Min, affixRange.Max);
                component.Affixes.Add(new ItemAffix
                {
                    AffixId = affixRange.AffixId,
                    Value = MathF.Round(value, 1) // Round to 1 decimal place
                });
            }
        }

        // Roll weapon damage multiplier
        if (component.WeaponDamageRange != null)
        {
            var range = component.WeaponDamageRange;
            component.WeaponDamageMultiplier = MathF.Round(
                _random.NextFloat(range.Min, range.Max), 2); // Round to 2 decimal places
        }

        // Roll softcrit bonus
        if (component.SoftcritRange != null)
        {
            var range = component.SoftcritRange;
            component.SoftcritBonus = _random.Next(range.Min, range.Max + 1);
        }

        // Roll death threshold bonus
        if (component.DeathThresholdRange != null)
        {
            var range = component.DeathThresholdRange;
            component.DeathThresholdBonus = _random.Next(range.Min, range.Max + 1);
        }

        Log.Debug($"Rolled stats for {ToPrettyString(uid)}: Stats={string.Join(", ", component.StatBonuses.Select(kv => $"{kv.Key}:{kv.Value}"))}, Affixes={component.Affixes.Count}");
    }

    /// <summary>
    /// Re-rolls all stats for an item. Useful for admin commands or special mechanics.
    /// </summary>
    public void RerollStats(EntityUid uid, ItemStatsComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        RollStats(uid, component);
        Dirty(uid, component);
    }
}
