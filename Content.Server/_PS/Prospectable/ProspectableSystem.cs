using Content.Shared._PS.Prospectable;
using Content.Shared._PS.Terradrop;

namespace Content.Server._PS.Prospectable;

public sealed class ProspectableSystem: SharedProspectableSystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RandomItemSpawnedEvent>(OnRandomItemSpawnedEvent);
    }

    /// <summary>
    /// Whenever a random item is spawned, this event is triggered.
    /// </summary>
    /// <param name="ev">The event containing the entity UID of the spawned item.</param>
    private void OnRandomItemSpawnedEvent(ref RandomItemSpawnedEvent ev)
    {
        GetMapLevel(ev.EntityUid, out int mapLevel);
        AssignComp(ev.EntityUid, mapLevel);
    }

    /// <summary>
    /// Get the map level. If there is no TerradropMapComponent, it defaults to the minimum item level.
    /// </summary>
    /// <param name="entityUid">The entity UID for which to get the map level.</param>
    /// <param name="mapLevel">The map level to be set.</param>
    private void GetMapLevel(EntityUid entityUid, out int mapLevel)
    {
        var mapUid = _map.GetMap(Transform(entityUid).MapID);
        if (TryComp<TerradropMapComponent>(mapUid, out var comp))
            mapLevel = comp.ThreatLevel;
        else
            mapLevel = MinItemLevel;
    }

    /// <summary>
    /// Assigns the ProspectableComponent to the entity with the specified map level.
    /// </summary>
    /// <param name="entityUid">The entity UID to which the component will be assigned.</param>
    /// <param name="mapLevel">The map level to be assigned to the component.</param>
    private void AssignComp(EntityUid entityUid, int mapLevel)
    {
        var comp = EnsureComp<ProspectableComponent>(entityUid);
        comp.Level = mapLevel;
    }
}
