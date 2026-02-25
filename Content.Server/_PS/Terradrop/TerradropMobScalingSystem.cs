using Content.Shared._PS.Terradrop;
using Content.Shared.CCVar;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Configuration;

namespace Content.Server._PS.Terradrop;

/// <summary>
/// Scales mob health and damage based on the terradrop dungeon level.
/// Mirrors the scaling approach used by <see cref="Content.Server._PS.Stats.Systems.ItemStatsInitSystem"/>.
/// </summary>
public sealed class TerradropMobScalingSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;

    private float _healthCoefficient = 0.2f;
    private float _damageCoefficient = 0.2f;
    private float _exponent = 1.5f;
    private float _maxMultiplier = 100f;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.TerradropMobHealthCoefficient, value => _healthCoefficient = value / 100f, true);
        Subs.CVar(_cfg, CCVars.TerradropMobDamageCoefficient, value => _damageCoefficient = value / 100f, true);
        Subs.CVar(_cfg, CCVars.TerradropMobScalingExponent, value => _exponent = value / 100f, true);
        Subs.CVar(_cfg, CCVars.TerradropMobScalingCap, value => _maxMultiplier = value, true);

        SubscribeLocalEvent<TerradropMobComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(EntityUid uid, TerradropMobComponent component, ComponentStartup args)
    {
        if (component.Initialized)
            return;

        var level = GetTerradropLevel(uid);
        component.SpawnLevel = level;

        // Power curve: multiplier = 1 + coefficient * level^exponent, capped
        var healthMultiplier = MathF.Min(1f + _healthCoefficient * MathF.Pow(level, _exponent), _maxMultiplier);
        var damageMultiplier = MathF.Min(1f + _damageCoefficient * MathF.Pow(level, _exponent), _maxMultiplier);

        ScaleHealth(uid, healthMultiplier);
        ScaleDamage(uid, damageMultiplier);

        component.HealthMultiplier = healthMultiplier;
        component.DamageMultiplier = damageMultiplier;
        component.Initialized = true;
        Dirty(uid, component);

        if (level > 0)
            Log.Debug($"Scaled mob {ToPrettyString(uid)} at level {level}: health={healthMultiplier:F2}x, damage={damageMultiplier:F2}x");
    }

    private void ScaleHealth(EntityUid uid, float multiplier)
    {
        if (multiplier <= 1f)
            return;

        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        // Snapshot current thresholds, then use public API to set scaled values.
        var existing = new Dictionary<FixedPoint2, MobState>(thresholds.Thresholds);
        foreach (var (damage, state) in existing)
        {
            if (state == MobState.Alive)
                continue;

            _mobThreshold.SetMobStateThreshold(uid, damage * multiplier, state, thresholds);
        }
    }

    private void ScaleDamage(EntityUid uid, float multiplier)
    {
        if (multiplier <= 1f)
            return;

        if (!TryComp<MeleeWeaponComponent>(uid, out var melee))
            return;

        melee.Damage = melee.Damage * multiplier;
        Dirty(uid, melee);
    }

    private int GetTerradropLevel(EntityUid uid)
    {
        if (!TryComp<TransformComponent>(uid, out var transform))
            return 0;

        var mapUid = transform.MapUid;
        if (mapUid == null || !TryComp<TerradropMapComponent>(mapUid, out var terradropMap))
            return 0;

        return terradropMap.Level;
    }
}
