using System.Numerics;
using Content.Server.Salvage.Expeditions;
using Content.Shared._PS.Terradrop;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Ghost;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._PS.Terradrop;

/// <summary>
/// All code regarding terradrop mission objectives and how events are handled go here.
/// ie. Objectives, Handling ghosts and players tp back when mission ends, etc.
/// </summary>
public sealed partial class TerradropSystem
{
    private void InitializeMissionHandling()
    {
        SubscribeLocalEvent<SalvageExpeditionComponent, EntityTerminatingEvent>(OnMapTerminating);
    }

    // Send ghosts back to the default map so they don't lose their stuff.
    private void OnMapTerminating(EntityUid uid, SalvageExpeditionComponent component, EntityTerminatingEvent ev)
    {
        var ghosts = EntityQueryEnumerator<GhostComponent, TransformComponent>();
        if (!TryComp<TerradropMapComponent>(uid, out var mapComponent) ||
            mapComponent.ReturnMarker is not { Valid: true })
            return;
        if (mapComponent.MapPrototype == null)
            return;

        var returnMarkerTransform = Transform(mapComponent.ReturnMarker.Value);

        var returnMarkerCoords =
            new MapCoordinates(returnMarkerTransform.Coordinates.Position, returnMarkerTransform.MapID);
        while (ghosts.MoveNext(out var ghostUid, out _, out var xform))
        {
            if (xform.MapUid == uid)
                _transform.SetMapCoordinates(ghostUid, returnMarkerCoords);
        }

        var players =
            AllEntityQuery<HumanoidProfileComponent, ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var playerUid, out _, out _, out _, out var xform))
        {
            if (xform.MapUid == uid)
            {
                // mostly just for vehicles
                _buckle.TryUnbuckle(playerUid, playerUid, true);

                // Kill the player
                if (TryComp<MobStateComponent>(playerUid, out var mobState) &&
                    TryComp<DamageableComponent>(playerUid, out _))
                {
                    if (mobState.CurrentState != MobState.Dead)
                    {
                        if (!_prototypeManager.TryIndex(mapComponent.MapPrototype.ReturnDamageType, out var prototype))
                        {
                            continue;
                        }

                        _damageableSystem.SetDamage(
                            playerUid,
                            new DamageSpecifier(prototype, FixedPoint2.New(mapComponent.MapPrototype.ReturnDamageAmount))
                        );
                    }
                }

                // Send players back dead. and in a body bag.
                var returnContainer = Spawn(mapComponent.MapPrototype.ReturnContainerProto);
                _entityStorageSystem.Insert(playerUid, returnContainer);
                _transform.SetMapCoordinates(returnContainer, returnMarkerCoords);
            }
        }
    }
}
