using System.Linq;
using Content.Shared._PS.Stats.Components;
using Content.Shared._PS.Terradrop;
using Robust.Shared.Map;
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

        // Get level from terradrop map if applicable and store it on the item
        var level = GetTerradropLevel(uid);
        component.SpawnLevel = level;

        // Level 10 = 10% bonus, Level 50 = 50% bonus, etc.
        var levelBonus = level * 0.01f;

        RollStats(uid, component, levelBonus);
        component.Initialized = true;
        Dirty(uid, component);
    }

    /// <summary>
    /// Gets the terradrop level from the map the entity is on.
    /// Returns 0 if not on a terradrop map.
    /// </summary>
    private int GetTerradropLevel(EntityUid uid)
    {
        if (!TryComp<TransformComponent>(uid, out var transform))
            return 0;

        var mapUid = transform.MapUid;
        if (mapUid == null || !TryComp<TerradropMapComponent>(mapUid, out var terradropMap))
            return 0;

        return terradropMap.Level;
    }

    /// <summary>
    /// Rolls all random stats for an item based on its defined ranges.
    /// </summary>
    /// <param name="uid">The entity to roll stats for.</param>
    /// <param name="component">The ItemStatsComponent.</param>
    /// <param name="levelBonus">Bonus multiplier from terradrop level (0.1 = 10% bonus).</param>
    public void RollStats(EntityUid uid, ItemStatsComponent component, float levelBonus = 0f)
    {
        var bonusMultiplier = 1f + levelBonus;

        // Roll stat bonuses
        if (component.StatRanges.Count > 0)
        {
            component.StatBonuses.Clear();
            foreach (var (stat, range) in component.StatRanges)
            {
                // Apply level bonus to the range
                var boostedMin = (int)MathF.Round(range.Min * bonusMultiplier);
                var boostedMax = (int)MathF.Round(range.Max * bonusMultiplier);
                var value = _random.Next(boostedMin, boostedMax + 1);
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
                // Apply level bonus to the range
                var boostedMin = affixRange.Min * bonusMultiplier;
                var boostedMax = affixRange.Max * bonusMultiplier;
                var value = _random.NextFloat(boostedMin, boostedMax);
                component.Affixes.Add(new ItemAffix
                {
                    AffixId = affixRange.AffixId,
                    Value = MathF.Round(value, 1)
                });
            }
        }

        // Roll weapon damage multiplier
        if (component.WeaponDamageRange != null)
        {
            var range = component.WeaponDamageRange;
            // For damage multiplier, boost above 1.0 (the bonus portion)
            var baseMin = range.Min - 1f;
            var baseMax = range.Max - 1f;
            var boostedMin = 1f + (baseMin * bonusMultiplier);
            var boostedMax = 1f + (baseMax * bonusMultiplier);
            component.WeaponDamageMultiplier = MathF.Round(
                _random.NextFloat(boostedMin, boostedMax), 2);
        }

        // Roll softcrit bonus
        if (component.SoftcritRange != null)
        {
            var range = component.SoftcritRange;
            var boostedMin = (int)MathF.Round(range.Min * bonusMultiplier);
            var boostedMax = (int)MathF.Round(range.Max * bonusMultiplier);
            component.SoftcritBonus = _random.Next(boostedMin, boostedMax + 1);
        }

        // Roll death threshold bonus
        if (component.DeathThresholdRange != null)
        {
            var range = component.DeathThresholdRange;
            var boostedMin = (int)MathF.Round(range.Min * bonusMultiplier);
            var boostedMax = (int)MathF.Round(range.Max * bonusMultiplier);
            component.DeathThresholdBonus = _random.Next(boostedMin, boostedMax + 1);
        }

        var levelInfo = levelBonus > 0 ? $", LevelBonus={levelBonus:P0}" : "";
        Log.Debug($"Rolled stats for {ToPrettyString(uid)}: Stats={string.Join(", ", component.StatBonuses.Select(kv => $"{kv.Key}:{kv.Value}"))}, Affixes={component.Affixes.Count}{levelInfo}");
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
