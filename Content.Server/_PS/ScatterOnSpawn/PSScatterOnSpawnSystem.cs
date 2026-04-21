using Content.Shared._PS.ScatterOnSpawn;
using Content.Shared.Throwing;
using Robust.Shared.Random;

namespace Content.Server._PS.ScatterOnSpawn;

/// <summary>
/// Throws the entity in a random direction on component startup, then removes itself.
/// Replicates how explosions scatter debris — items slide outward and decelerate via friction.
/// </summary>
public sealed class PSScatterOnSpawnSystem : EntitySystem
{
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PSScatterOnSpawnComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(EntityUid uid, PSScatterOnSpawnComponent comp, ComponentStartup args)
    {
        var angle = _random.NextAngle();
        var direction = angle.ToVec();
        _throwing.TryThrow(uid, direction, baseThrowSpeed: comp.Force, playSound: false, doSpin: true);
        RemCompDeferred<PSScatterOnSpawnComponent>(uid);
    }
}
